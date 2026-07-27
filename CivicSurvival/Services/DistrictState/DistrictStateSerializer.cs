using System.Collections.Generic;
using CivicSurvival.Core.Systems;
using CivicSurvival.Core.Features.Wellbeing;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Utils
{
    /// <summary>
    /// Serialization helpers for district state.
    /// Creates deep copies for save/load operations.
    /// </summary>
    public static class DistrictStateSerializer
    {
        /// <summary>
        /// Deep copy blackouts dictionary.
        /// </summary>
        public static Dictionary<int, HashSet<BuildingCategory>> CopyBlackouts(
            Dictionary<int, HashSet<BuildingCategory>> source)
        {
            if (source == null) return new Dictionary<int, HashSet<BuildingCategory>>();

            var copy = new Dictionary<int, HashSet<BuildingCategory>>(source.Count);
            foreach (var kvp in source)
            {
                copy[kvp.Key] = new HashSet<BuildingCategory>(kvp.Value);
            }
            return copy;
        }

        /// <summary>
        /// Deep copy blackouts for immutable snapshots.
        /// </summary>
        public static Dictionary<int, IReadOnlyCollection<BuildingCategory>> CopyBlackoutsForSnapshot(
            Dictionary<int, HashSet<BuildingCategory>> source)
        {
            if (source == null) return new Dictionary<int, IReadOnlyCollection<BuildingCategory>>();

            var copy = new Dictionary<int, IReadOnlyCollection<BuildingCategory>>(source.Count);
            foreach (var kvp in source)
            {
                var values = new BuildingCategory[kvp.Value.Count];
                kvp.Value.CopyTo(values);
                copy[kvp.Key] = values;
            }
            return copy;
        }

        /// <summary>
        /// Deep copy schedules dictionary.
        /// </summary>
        public static Dictionary<int, SchedulePreset> CopySchedules(
            Dictionary<int, SchedulePreset> source)
        {
            if (source == null) return new Dictionary<int, SchedulePreset>();
            return new Dictionary<int, SchedulePreset>(source);
        }

        /// <summary>
        /// Copy explicit district overrides into the schedule view used by read snapshots.
        /// </summary>
        public static Dictionary<int, SchedulePreset> CopySchedules(
            Dictionary<int, DistrictOverride> source)
        {
            if (source == null) return new Dictionary<int, SchedulePreset>();

            var copy = new Dictionary<int, SchedulePreset>(source.Count);
            foreach (var kvp in source)
            {
                copy[kvp.Key] = kvp.Value.Schedule;
            }
            return copy;
        }

        /// <summary>
        /// Deep copy explicit district overrides.
        /// </summary>
        public static Dictionary<int, DistrictOverride> CopyDistrictOverrides(
            Dictionary<int, DistrictOverride> source)
        {
            if (source == null) return new Dictionary<int, DistrictOverride>();
            return new Dictionary<int, DistrictOverride>(source);
        }

        /// <summary>
        /// Deep copy HashSet of integers.
        /// </summary>
        public static HashSet<int> CopyIntSet(HashSet<int> source)
        {
            if (source == null) return new HashSet<int>();
            return new HashSet<int>(source);
        }

        /// <summary>
        /// Deep copy penalties dictionary.
        /// </summary>
        public static Dictionary<int, DistrictPenalties> CopyPenalties(
            Dictionary<int, DistrictPenalties> source)
        {
            if (source == null) return new Dictionary<int, DistrictPenalties>();
            return new Dictionary<int, DistrictPenalties>(source);
        }

        /// <summary>
        /// Deep copy PreShedState dictionary (CategoriesOff is a mutable HashSet — must clone).
        /// </summary>
        public static Dictionary<int, PreShedState> CopyPreShedStates(
            Dictionary<int, PreShedState> source)
        {
            if (source == null) return new Dictionary<int, PreShedState>();
            var copy = new Dictionary<int, PreShedState>(source.Count);
            foreach (var kvp in source)
            {
                copy[kvp.Key] = new PreShedState(
                    kvp.Value.Schedule,
                    new HashSet<BuildingCategory>(kvp.Value.CategoriesOff),
                    kvp.Value.WasVip,
                    kvp.Value.HadExplicitSchedule,
                    kvp.Value.WasVipBypass);
            }
            return copy;
        }

        /// <summary>
        /// Load blackouts from source into target (clears target first).
        /// </summary>
        public static void LoadBlackouts(
            Dictionary<int, HashSet<BuildingCategory>> target,
            IReadOnlyDictionary<int, HashSet<BuildingCategory>> source)
        {
            if (target == null || source == null) return;

            target.Clear();
            foreach (var kvp in source)
            {
                target[kvp.Key] = new HashSet<BuildingCategory>(kvp.Value);
            }
        }

        /// <summary>
        /// Load schedules from source into target (clears target first).
        /// </summary>
        public static void LoadSchedules(
            Dictionary<int, SchedulePreset> target,
            IReadOnlyDictionary<int, SchedulePreset> source)
        {
            if (target == null || source == null) return;

            target.Clear();
            foreach (var kvp in source)
            {
                target[kvp.Key] = kvp.Value;
            }
        }

        /// <summary>
        /// Load explicit district overrides from source into target (clears target first).
        /// </summary>
        public static void LoadDistrictOverrides(
            Dictionary<int, DistrictOverride> target,
            IReadOnlyDictionary<int, DistrictOverride> source)
        {
            if (target == null || source == null) return;

            target.Clear();
            foreach (var kvp in source)
            {
                target[kvp.Key] = kvp.Value;
            }
        }

        /// <summary>
        /// Load HashSet from source into target (clears target first).
        /// </summary>
        public static void LoadIntSet(HashSet<int> target, IReadOnlyCollection<int> source)
        {
            if (target == null || source == null) return;

            target.Clear();
            foreach (var item in source)
            {
                target.Add(item);
            }
        }

        /// <summary>
        /// Load penalties from source into target (clears target first).
        /// </summary>
        public static void LoadPenalties(
            Dictionary<int, DistrictPenalties> target,
            IReadOnlyDictionary<int, DistrictPenalties> source)
        {
            if (target == null || source == null) return;

            target.Clear();
            foreach (var kvp in source)
            {
                var penalties = kvp.Value;
                penalties.ActiveSources = PenaltySources.Sanitize((int)penalties.ActiveSources);
                DistrictPenaltyCalculator.Recalculate(ref penalties);
                if (!DistrictPenaltyCalculator.IsEmpty(penalties))
                    target[kvp.Key] = penalties;
            }
        }

        /// <summary>
        /// Load pre-shed states from source into target (clears target first).
        /// </summary>
        public static void LoadPreShedStates(
            Dictionary<int, PreShedState> target,
            IReadOnlyDictionary<int, PreShedState> source)
        {
            if (target == null || source == null) return;

            target.Clear();
            foreach (var kvp in source)
            {
                target[kvp.Key] = new PreShedState(
                    kvp.Value.Schedule,
                    new HashSet<BuildingCategory>(kvp.Value.CategoriesOff),
                    kvp.Value.WasVip,
                    kvp.Value.HadExplicitSchedule,
                    kvp.Value.WasVipBypass);
            }
        }
    }
}
