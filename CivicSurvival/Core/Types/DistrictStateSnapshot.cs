using System;
using System.Collections.Generic;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Features.Wellbeing;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Systems;

namespace CivicSurvival.Core.Types
{
    /// <summary>
    /// Immutable snapshot of district state for safe iteration by game thread.
    /// Created by ThreadSafeDistrictState.TakeSnapshot().
    ///
    /// All collections are exposed as readonly interfaces to prevent accidental mutation.
    /// </summary>
    public readonly struct DistrictStateSnapshot : IEquatable<DistrictStateSnapshot>
    {
        /// <summary>
        /// Empty snapshot for null-safe fallback when ThreadSafeDistrictState is unavailable.
        /// Each field gets its own empty collection instance to prevent cross-contamination.
        /// </summary>
        public static DistrictStateSnapshot Empty => new(
            new Dictionary<int, IReadOnlyCollection<BuildingCategory>>(),
            new Dictionary<int, SchedulePreset>(),
            new HashSet<int>(),
            new HashSet<int>(),
            new Dictionary<int, DistrictPenalties>(),
            new Dictionary<int, int>(),
            new HashSet<int>(),
            0f,
            SchedulePreset.Manual
        );

        public readonly IReadOnlyDictionary<int, IReadOnlyCollection<BuildingCategory>> DistrictBlackouts;
        public readonly IReadOnlyDictionary<int, SchedulePreset> DistrictSchedules;
        public readonly IReadOnlyCollection<int> VIPDistricts;
        public readonly IReadOnlyCollection<int> VIPBypass;
        public readonly IReadOnlyDictionary<int, DistrictPenalties> DistrictPenalties;
        public readonly IReadOnlyDictionary<int, int> DistrictPriorities;
        public readonly IReadOnlyCollection<int> AutoSheddedDistricts;
        public readonly float GameHour;
        public readonly SchedulePreset CitySchedule;

        // Internal storage for Contains() operations — district indices
        [NonEntityIndex] private readonly HashSet<int> _vipDistrictsSet;
        [NonEntityIndex] private readonly HashSet<int> _vipBypassSet;
        [NonEntityIndex] private readonly HashSet<int> _autoSheddedSet;

        /// <summary>
        /// Internal constructor — takes ownership of already-copied collections (no second copy).
        /// Used by TakeSnapshot() which provides fresh copies under read lock.
        /// </summary>
        internal DistrictStateSnapshot(
            Dictionary<int, IReadOnlyCollection<BuildingCategory>> blackouts,
            Dictionary<int, SchedulePreset> schedules,
            HashSet<int> vips,
            HashSet<int> vipBypass,
            Dictionary<int, DistrictPenalties> penalties,
            Dictionary<int, int> priorities,
            HashSet<int> autoShedded,
            float gameHour,
            SchedulePreset citySchedule)
        {
            DistrictBlackouts = blackouts;
            DistrictSchedules = schedules;
            VIPDistricts = vips;
            VIPBypass = vipBypass;
            DistrictPenalties = penalties;
            DistrictPriorities = priorities;
            AutoSheddedDistricts = autoShedded;
            GameHour = gameHour;
            CitySchedule = citySchedule;

            _vipDistrictsSet = vips;
            _vipBypassSet = vipBypass;
            _autoSheddedSet = autoShedded;
        }

        /// <summary>
        /// Check if blackout should be active based on schedule (with phase offset).
        /// Phase offset ensures districts don't all blackout simultaneously - each district
        /// uses its index to stagger the start time (e.g., District 0 at 8:00, District 1 at 8:30, etc.).
        /// Falls back to CitySchedule if district has no custom schedule.
        /// Explicit Manual entries mean "always on" and do not fall back.
        /// </summary>
        public bool IsBlackoutActiveForSchedule(int districtIndex)
        {
            // VIP districts are exempt from scheduled blackouts
            if (_vipDistrictsSet.Contains(districtIndex))
                return false;

            // Get schedule: district custom schedule or fall back to city-wide schedule
            SchedulePreset schedule;
            if (DistrictSchedules.TryGetValue(districtIndex, out var customSchedule))
            {
                schedule = customSchedule;
            }
            else
            {
                schedule = CitySchedule;
            }

            if (schedule == SchedulePreset.Manual)
                return false;

            // Use districtIndex as phase offset to stagger blackouts across districts
            // (prevents all districts from blacking out simultaneously)
            return Core.Utils.ScheduleHelper.IsBlackoutActive((int)schedule, GameHour, districtIndex);
        }

        /// <summary>
        /// Check if any blackout (manual, schedule, or auto-dispatch) is active for district.
        /// </summary>
        public bool IsDistrictInBlackout(int districtIndex)
        {
            if (_vipDistrictsSet.Contains(districtIndex))
                return false;

            // Check manual/auto-dispatch blackouts (any category off = partially blacked out)
            // NOTE: No _autoSheddedSet short-circuit — auto-shedded districts in intermediate
            // restore (Q1 ON phase) have power flowing and should not count as blacked out.
            // AutoDispatch writes categories/schedules directly, so these checks cover all cases.
            if (DistrictBlackouts.TryGetValue(districtIndex, out var categories) && categories.Count > 0)
                return true;

            // Check schedule blackouts
            return IsBlackoutActiveForSchedule(districtIndex);
        }

        /// <summary>
        /// Authoritative non-VIP blackout scan for consumers that need city-schedule
        /// inheritance across live districts without duplicating schedule logic.
        /// VIP bypass is partial per-building protection, so it does not exclude
        /// the whole district from non-VIP blackout detection.
        /// </summary>
        public bool AnyActiveBlackoutForNonVip(IEnumerable<int> liveDistricts)
        {
            foreach (int districtIndex in liveDistricts)
            {
                if (IsVIP(districtIndex))
                    continue;

                if (DistrictBlackouts.TryGetValue(districtIndex, out var categories) && categories.Count > 0)
                    return true;

                if (IsBlackoutActiveForSchedule(districtIndex))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Check if district has VIP status.
        /// </summary>
        public bool IsVIP(int districtIndex) => _vipDistrictsSet.Contains(districtIndex);

        /// <summary>
        /// Count of VIP districts (for income calculation).
        /// </summary>
        public int VipCount => _vipDistrictsSet.Count;

        /// <summary>
        /// Check if district has VIP Bypass (wealthy protection).
        /// </summary>
        public bool HasVIPBypass(int districtIndex) => _vipBypassSet.Contains(districtIndex);

        /// <summary>
        /// Check if district was auto-shedded by AutoDispatch.
        /// </summary>
        public bool IsAutoShedded(int districtIndex) => _autoSheddedSet.Contains(districtIndex);

        /// <summary>
        /// Get penalties for a district.
        /// </summary>
        public DistrictPenalties GetPenalties(int districtIndex)
        {
            return DistrictPenalties.TryGetValue(districtIndex, out var penalties)
                ? penalties
                : default;
        }

        /// <summary>
        /// Get priority for a district without taking another service lock.
        /// </summary>
        public int GetPriority(int districtIndex)
        {
            return DistrictPriorities.TryGetValue(districtIndex, out var priority)
                ? priority
                : BalanceConfig.Current.Districts.DefaultPriority;
        }

        /// <summary>
        /// Get blackout source for a district.
        /// Priority: Auto > Schedule > Manual > None
        /// </summary>
        public string GetBlackoutSource(int districtIndex)
        {
            // Not actually blacked out → no source (prevents stale "auto" badge during intermediate restore)
            if (!IsDistrictInBlackout(districtIndex))
                return "none";

            // Auto-dispatch (stress-based shedding)
            if (_autoSheddedSet.Contains(districtIndex))
                return "auto";

            // Schedule-based blackout
            if (IsBlackoutActiveForSchedule(districtIndex))
                return "schedule";

            // Manual blackout (user toggled categories)
            if (DistrictBlackouts.TryGetValue(districtIndex, out var cats) && cats.Count > 0)
                return "manual";

            return "none";
        }

        public bool Equals(DistrictStateSnapshot other)
            => GameHour.Equals(other.GameHour)
                && CitySchedule == other.CitySchedule
                && DictionaryEquals(DistrictSchedules, other.DistrictSchedules)
                && DictionaryEquals(DistrictPriorities, other.DistrictPriorities)
                && DictionaryEquals(DistrictPenalties, other.DistrictPenalties)
                && BlackoutsEquals(DistrictBlackouts, other.DistrictBlackouts)
                && CollectionEquals(VIPDistricts, other.VIPDistricts)
                && CollectionEquals(VIPBypass, other.VIPBypass)
                && CollectionEquals(AutoSheddedDistricts, other.AutoSheddedDistricts);

        public override bool Equals(object? obj)
            => obj is DistrictStateSnapshot other && Equals(other);

        public override int GetHashCode()
            => HashCode.Combine(
                GameHour,
                (int)CitySchedule,
                HashCode.Combine(
                    DistrictSchedules is null ? 0 : DistrictSchedules.Count,
                    DistrictBlackouts is null ? 0 : DistrictBlackouts.Count,
                    VIPDistricts is null ? 0 : VIPDistricts.Count,
                    VIPBypass is null ? 0 : VIPBypass.Count,
                    AutoSheddedDistricts is null ? 0 : AutoSheddedDistricts.Count,
                    DistrictPenalties is null ? 0 : DistrictPenalties.Count));

        public static bool operator ==(DistrictStateSnapshot left, DistrictStateSnapshot right)
            => left.Equals(right);

        public static bool operator !=(DistrictStateSnapshot left, DistrictStateSnapshot right)
            => !left.Equals(right);

        private static bool DictionaryEquals<TKey, TValue>(
            IReadOnlyDictionary<TKey, TValue>? left,
            IReadOnlyDictionary<TKey, TValue>? right)
            where TKey : notnull
        {
            if (ReferenceEquals(left, right)) return true;
            if (left is null || right is null) return false;
            if (left.Count != right.Count) return false;
            var comparer = EqualityComparer<TValue>.Default;
            foreach (var pair in left)
            {
                if (!right.TryGetValue(pair.Key, out var otherValue))
                    return false;
                if (!comparer.Equals(pair.Value, otherValue))
                    return false;
            }
            return true;
        }

        private static bool CollectionEquals<T>(
            IReadOnlyCollection<T>? left,
            IReadOnlyCollection<T>? right)
        {
            if (ReferenceEquals(left, right)) return true;
            if (left is null || right is null) return false;
            if (left.Count != right.Count) return false;
            // Treat as set semantics (HashSet-backed collections):
            // every element of left must exist in right.
            foreach (var item in left)
            {
                bool found = false;
                foreach (var candidate in right)
                {
                    if (EqualityComparer<T>.Default.Equals(item, candidate))
                    {
                        found = true;
                        break;
                    }
                }
                if (!found) return false;
            }
            return true;
        }

        private static bool BlackoutsEquals(
            IReadOnlyDictionary<int, IReadOnlyCollection<BuildingCategory>>? left,
            IReadOnlyDictionary<int, IReadOnlyCollection<BuildingCategory>>? right)
        {
            if (ReferenceEquals(left, right)) return true;
            if (left is null || right is null) return false;
            if (left.Count != right.Count) return false;
            foreach (var pair in left)
            {
                if (!right.TryGetValue(pair.Key, out var otherValue))
                    return false;
                if (!CollectionEquals(pair.Value, otherValue))
                    return false;
            }
            return true;
        }
    }
}
