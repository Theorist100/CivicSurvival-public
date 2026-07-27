using System;
using System.Collections.Generic;
using CivicSurvival.Core.Features.Wellbeing;
using System.Threading;
using CivicSurvival.Core.Events;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Systems;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Utils;
using CivicSurvival.Localization;

namespace CivicSurvival.Services.DistrictState
{
    /// <summary>
    /// Thread-safe container for district state.
    /// Optimized: Manual locks for Hot Path (Simulation/Polling), Wrappers for Cold Path (User Actions).
    ///
    /// Implements:
    /// - IDistrictStateReader: For UI panels (read-only, hides Dispose)
    /// - IDistrictStateWriter: For systems that mutate state
    /// </summary>
    public sealed class ThreadSafeDistrictState : IDisposable, IDistrictStateReader, IDistrictStateWriter, IAutoDispatchStateWriter, IDistrictStateSerialization
    {
        private static readonly LogContext Log = new("DistrictState");
        private readonly ReaderWriterLockSlim m_Lock = new(LockRecursionPolicy.NoRecursion);
        private int m_IsDisposed;

        // EventBus subscription for district lifecycle events
        private IEventBus? m_EventBus;
        private int m_IsSubscribed;

        // Mutable State — keyed by district index (stable CS2 spatial index, not Entity.Index)
        [NonEntityIndex] private readonly Dictionary<int, HashSet<BuildingCategory>> m_Blackouts = new();
        [NonEntityIndex] private readonly Dictionary<int, DistrictOverride> m_DistrictOverrides = new();
        [NonEntityIndex] private readonly HashSet<int> m_Vips = new();
        [NonEntityIndex] private readonly HashSet<int> m_VipBypass = new();
        [NonEntityIndex] private readonly Dictionary<int, PreShedState> m_PreShedStates = new();
        [NonEntityIndex] private readonly Dictionary<int, int> m_Priorities = new();
        [NonEntityIndex] private readonly Dictionary<int, DistrictPenalties> m_Penalties = new();

        private float m_GameHour = Engine.Timing.DEFAULT_GAME_HOUR;
        private SchedulePreset m_CitySchedule = SchedulePreset.Manual;
        private int m_BlackoutStateEpoch;
        private bool m_BlackoutStateDirty;

        // Snapshot caching
        private readonly VersionedView<DistrictStateSnapshot> m_SnapshotView = new(DistrictStateSnapshot.Empty);
        private DistrictStateSnapshot m_CachedSnapshot;
        private int m_CachedSnapshotCursor = int.MinValue;

        // ============================================================================
        // HOT PATHS (Manual Locks = Zero Allocations)
        // Used by Simulation or frequent UI Polling
        // ============================================================================

        private bool IsDisposed => Volatile.Read(ref m_IsDisposed) == 1;

        /// <summary>Safe read lock acquisition. Returns false if disposed (race-safe).</summary>
        private bool TryEnterReadLock()
        {
            if (IsDisposed) return false;
            try { m_Lock.EnterReadLock(); return true; }
            catch (ObjectDisposedException) { return false; }
        }

        /// <summary>Safe write lock acquisition. Returns false if disposed (race-safe).</summary>
        private bool TryEnterWriteLock()
        {
            if (IsDisposed) return false;
            try { m_Lock.EnterWriteLock(); return true; }
            catch (ObjectDisposedException) { return false; }
        }

        public float GameHour
        {
            get
            {
                if (!TryEnterReadLock()) return 0;
                try { return m_GameHour; } finally { m_Lock.ExitReadLock(); }
            }
            set
            {
                if (!TryEnterWriteLock()) return;
                try
                {
                    m_GameHour = value;
                    PublishLocked();
                }
                finally { m_Lock.ExitWriteLock(); }
            }
        }

        public DistrictStateSnapshot TakeSnapshot()
        {
            if (TryEnterReadLock())
            {
                try
                {
                    int cachedSnapshotCursor = m_CachedSnapshotCursor;
                    if (!m_SnapshotView.Observe(ref cachedSnapshotCursor).Changed) return m_CachedSnapshot;
                }
                finally { m_Lock.ExitReadLock(); }
            }

            if (!TryEnterWriteLock()) return DistrictStateSnapshot.Empty;
            try
            {
                int cachedSnapshotCursor = m_CachedSnapshotCursor;
                if (!m_SnapshotView.Observe(ref cachedSnapshotCursor).Changed) return m_CachedSnapshot;

                PublishLocked();
                return m_CachedSnapshot;
            }
            finally { m_Lock.ExitWriteLock(); }
        }

        public int BlackoutStateVersion
        {
            get => Volatile.Read(ref m_BlackoutStateEpoch);
        }

        // --- Optimized Getters (No closure allocations) ---

        public bool IsVIP(int districtIndex)
        {
            if (!TryEnterReadLock()) return false;
            try { return m_Vips.Contains(districtIndex); }
            finally { m_Lock.ExitReadLock(); }
        }

        public bool IsAutoShedded(int districtIndex)
        {
            if (!TryEnterReadLock()) return false;
            try { return m_PreShedStates.ContainsKey(districtIndex); }
            finally { m_Lock.ExitReadLock(); }
        }

        public bool TryGetPreShedState(int districtIndex, out PreShedState state)
        {
            if (!TryEnterReadLock()) { state = default; return false; }
            try
            {
                if (m_PreShedStates.TryGetValue(districtIndex, out var original))
                {
                    // Deep-copy: CategoriesOff is mutable HashSet, must not leak reference outside lock
                    state = new PreShedState(
                        original.Schedule,
                        new HashSet<BuildingCategory>(original.CategoriesOff),
                        original.WasVip,
                        original.HadExplicitSchedule,
                        original.WasVipBypass);
                    return true;
                }
                state = default;
                return false;
            }
            finally { m_Lock.ExitReadLock(); }
        }

        public bool IsCategoryOff(int districtIndex, BuildingCategory category)
        {
            if (!TryEnterReadLock()) return false;
            try { return m_Blackouts.TryGetValue(districtIndex, out var c) && c.Contains(category); }
            finally { m_Lock.ExitReadLock(); }
        }

        public SchedulePreset GetSchedule(int districtIndex)
        {
            if (!TryEnterReadLock()) return SchedulePreset.Manual;
            try { return m_DistrictOverrides.TryGetValue(districtIndex, out var o) ? o.Schedule : SchedulePreset.Manual; }
            finally { m_Lock.ExitReadLock(); }
        }

        public bool HasCustomSchedule(int districtIndex)
        {
            if (!TryEnterReadLock()) return false;
            try { return m_DistrictOverrides.ContainsKey(districtIndex); }
            finally { m_Lock.ExitReadLock(); }
        }

        public SchedulePreset GetEffectiveSchedule(int districtIndex)
        {
            if (!TryEnterReadLock()) return SchedulePreset.Manual;
            try
            {
                if (m_Vips.Contains(districtIndex)) return SchedulePreset.Manual;
                return m_DistrictOverrides.TryGetValue(districtIndex, out var o) ? o.Schedule : m_CitySchedule;
            }
            finally { m_Lock.ExitReadLock(); }
        }

        public bool IsScheduleBlackoutActive(int districtIndex)
        {
            if (!TryEnterReadLock()) return false;
            try
            {
                SchedulePreset schedule;
                if (m_Vips.Contains(districtIndex))
                    schedule = SchedulePreset.Manual;
                else if (m_DistrictOverrides.TryGetValue(districtIndex, out var o))
                    schedule = o.Schedule;
                else
                    schedule = m_CitySchedule;

                if (schedule == SchedulePreset.Manual) return false;
                return Core.Utils.ScheduleHelper.IsBlackoutActive((int)schedule, m_GameHour, districtIndex);
            }
            finally { m_Lock.ExitReadLock(); }
        }

        public bool IsVIPBypass(int districtIndex)
        {
            if (!TryEnterReadLock()) return false;
            try { return m_VipBypass.Contains(districtIndex); }
            finally { m_Lock.ExitReadLock(); }
        }

        public int GetPriority(int districtIndex)
        {
            int defaultPriority = Core.Config.BalanceConfig.Current.Districts.DefaultPriority;
            if (!TryEnterReadLock()) return defaultPriority;
            try { return m_Priorities.TryGetValue(districtIndex, out var p) ? p : defaultPriority; }
            finally { m_Lock.ExitReadLock(); }
        }

        public bool HasPenalty(int districtIndex, PenaltySource source)
        {
            if (!TryEnterReadLock()) return false;
            try { return m_Penalties.TryGetValue(districtIndex, out var p) && (p.ActiveSources & source) != 0; }
            finally { m_Lock.ExitReadLock(); }
        }

        public DistrictPenalties GetPenalties(int districtIndex)
        {
            if (!TryEnterReadLock()) return default;
            try { return m_Penalties.TryGetValue(districtIndex, out var p) ? p : default; }
            finally { m_Lock.ExitReadLock(); }
        }

        public float GetPositiveHappinessPenalty(int districtIndex)
        {
            if (!TryEnterReadLock()) return 0f;
            try
            {
                return m_Penalties.TryGetValue(districtIndex, out var p)
                    ? DistrictPenaltyCalculator.CalculatePositiveHappinessPenalty(in p)
                    : 0f;
            }
            finally { m_Lock.ExitReadLock(); }
        }

        public int AffectedDistrictsCount
        {
            get
            {
                if (!TryEnterReadLock()) return 0;
                try { return m_Penalties.Count; }
                finally { m_Lock.ExitReadLock(); }
            }
        }

        public string GetDistrictName(int districtIndex)
        {
            if (districtIndex == 0) return LocalizationManager.T("UI_DISTRICT_UNZONED");
            return $"District {districtIndex}";
        }

        public bool HasAnyState
        {
            get
            {
                if (!TryEnterReadLock()) return false;
                try
                {
                    return m_Blackouts.Count > 0 || m_DistrictOverrides.Count > 0 || m_Vips.Count > 0 ||
                           m_VipBypass.Count > 0 || m_Penalties.Count > 0 ||
                           m_PreShedStates.Count > 0 || m_Priorities.Count > 0 ||
                           m_CitySchedule != SchedulePreset.Manual;
                }
                finally { m_Lock.ExitReadLock(); }
            }
        }

        public IReadOnlyCollection<int> GetAutoSheddedDistricts()
        {
            if (!TryEnterReadLock()) return new HashSet<int>();
            try { return new HashSet<int>(m_PreShedStates.Keys); }
            finally { m_Lock.ExitReadLock(); }
        }

        public int GetAutoSheddedCount()
        {
            if (!TryEnterReadLock()) return 0;
            try { return m_PreShedStates.Count; }
            finally { m_Lock.ExitReadLock(); }
        }

        // ============================================================================
        // COLD PATHS (Wrappers OK)
        // User Actions / Rare Events / Serialization
        // ============================================================================

        private void ModifyState(Action action)
        {
            if (IsDisposed) return;
            StateRollbackFrame rollbackFrame = default;
            bool hasRollbackFrame = false;
            try
            {
                m_Lock.EnterWriteLock();
                if (IsDisposed) return; // TOCTOU
                rollbackFrame = CaptureRollbackFrameLocked();
                hasRollbackFrame = true;
                action();
                FlushBlackoutStateVersionDirtyLocked();
                PublishLocked();
            }
            catch (ObjectDisposedException)
            {
                if (hasRollbackFrame && m_Lock.IsWriteLockHeld)
                    RestoreRollbackFrameLocked(in rollbackFrame);
                /* Lock disposed during shutdown - safe to ignore */
            }
            catch
            {
                if (hasRollbackFrame && m_Lock.IsWriteLockHeld)
                    RestoreRollbackFrameLocked(in rollbackFrame);
                throw;
            }
            finally { if (m_Lock.IsWriteLockHeld) m_Lock.ExitWriteLock(); }
        }

        [CallerHoldsLock(nameof(m_Lock))]
        private StateRollbackFrame CaptureRollbackFrameLocked()
        {
            return new StateRollbackFrame(
                DistrictStateSerializer.CopyBlackouts(m_Blackouts),
                DistrictStateSerializer.CopyDistrictOverrides(m_DistrictOverrides),
                DistrictStateSerializer.CopyIntSet(m_Vips),
                DistrictStateSerializer.CopyIntSet(m_VipBypass),
                DistrictStateSerializer.CopyPreShedStates(m_PreShedStates),
                new Dictionary<int, int>(m_Priorities),
                DistrictStateSerializer.CopyPenalties(m_Penalties),
                m_GameHour,
                m_CitySchedule,
                m_BlackoutStateEpoch,
                m_BlackoutStateDirty);
        }

        [CallerHoldsLock(nameof(m_Lock))]
        private void RestoreRollbackFrameLocked(in StateRollbackFrame frame)
        {
            DistrictStateSerializer.LoadBlackouts(m_Blackouts, frame.Blackouts);
            DistrictStateSerializer.LoadDistrictOverrides(m_DistrictOverrides, frame.DistrictOverrides);
            DistrictStateSerializer.LoadIntSet(m_Vips, frame.Vips);
            DistrictStateSerializer.LoadIntSet(m_VipBypass, frame.VipBypass);
            DistrictStateSerializer.LoadPreShedStates(m_PreShedStates, frame.PreShedStates);

            m_Priorities.Clear();
            foreach (var kvp in frame.Priorities)
                m_Priorities[kvp.Key] = kvp.Value;

            m_Penalties.Clear();
            foreach (var kvp in frame.Penalties)
                m_Penalties[kvp.Key] = kvp.Value;

            m_GameHour = frame.GameHour;
            m_CitySchedule = frame.CitySchedule;
            m_BlackoutStateEpoch = frame.BlackoutStateVersion;
            m_BlackoutStateDirty = frame.BlackoutStateDirty;
        }

        private readonly struct StateRollbackFrame
        {
            // Keyed by district index (stable CS2 spatial index, not Entity.Index) — mirrors parent fields.
            [NonEntityIndex] public readonly Dictionary<int, HashSet<BuildingCategory>> Blackouts;
            [NonEntityIndex] public readonly Dictionary<int, DistrictOverride> DistrictOverrides;
            [NonEntityIndex] public readonly HashSet<int> Vips;
            [NonEntityIndex] public readonly HashSet<int> VipBypass;
            [NonEntityIndex] public readonly Dictionary<int, PreShedState> PreShedStates;
            [NonEntityIndex] public readonly Dictionary<int, int> Priorities;
            [NonEntityIndex] public readonly Dictionary<int, DistrictPenalties> Penalties;
            public readonly float GameHour;
            public readonly SchedulePreset CitySchedule;
            public readonly int BlackoutStateVersion;
            public readonly bool BlackoutStateDirty;

            public StateRollbackFrame(
                Dictionary<int, HashSet<BuildingCategory>> blackouts,
                Dictionary<int, DistrictOverride> districtOverrides,
                HashSet<int> vips,
                HashSet<int> vipBypass,
                Dictionary<int, PreShedState> preShedStates,
                Dictionary<int, int> priorities,
                Dictionary<int, DistrictPenalties> penalties,
                float gameHour,
                SchedulePreset citySchedule,
                int blackoutStateVersion,
                bool blackoutStateDirty)
            {
                Blackouts = blackouts;
                DistrictOverrides = districtOverrides;
                Vips = vips;
                VipBypass = vipBypass;
                PreShedStates = preShedStates;
                Priorities = priorities;
                Penalties = penalties;
                GameHour = gameHour;
                CitySchedule = citySchedule;
                BlackoutStateVersion = blackoutStateVersion;
                BlackoutStateDirty = blackoutStateDirty;
            }
        }

        // --- Mutators (with Logging) ---

        public void ToggleDistrictBlackout(int districtIndex) => ModifyState(() =>
        {
            m_PreShedStates.Remove(districtIndex);
            if (m_Blackouts.TryGetValue(districtIndex, out var cats) && cats.Count == Engine.Districts.TOTAL_BUILDING_CATEGORIES)
            {
                m_Blackouts.Remove(districtIndex);
                Log.Info($"D{districtIndex}: ALL ON");
            }
            else
            {
                m_Blackouts[districtIndex] = new HashSet<BuildingCategory>(BuildingCategories.All);
                Log.Info($"D{districtIndex}: ALL OFF");
            }
            BumpBlackoutStateVersion();
        });

        public void SetDistrictBlackout(int districtIndex, bool blackedOut) => ModifyState(() =>
        {
            bool changed = m_PreShedStates.Remove(districtIndex);
            if (blackedOut)
            {
                if (!m_Blackouts.TryGetValue(districtIndex, out var categories) || !HasAllBlackoutCategories(categories))
                {
                    m_Blackouts[districtIndex] = new HashSet<BuildingCategory>(BuildingCategories.All);
                    Log.Info($"D{districtIndex}: ALL OFF");
                    changed = true;
                }
            }
            else
            {
                if (m_Blackouts.Remove(districtIndex))
                {
                    Log.Info($"D{districtIndex}: ALL ON");
                    changed = true;
                }
            }

            if (changed)
                BumpBlackoutStateVersion();
        });

        public void ToggleDistrictCategory(int districtIndex, BuildingCategory category) => ModifyState(() =>
        {
            m_PreShedStates.Remove(districtIndex);
            ToggleCategoryInternal(districtIndex, category);
        });

        /// <summary>AutoDispatch-only: toggle category without updating PreShedState.</summary>
        public void ToggleDistrictCategoryAutoDispatch(int districtIndex, BuildingCategory category) => ModifyState(() =>
        {
            ToggleCategoryInternal(districtIndex, category);
        });

        private void ToggleCategoryInternal(int districtIndex, BuildingCategory category)
        {
            if (!m_Blackouts.TryGetValue(districtIndex, out var categories))
            {
                categories = new HashSet<BuildingCategory>();
                m_Blackouts[districtIndex] = categories;
            }
            if (!categories.Remove(category))
            {
                categories.Add(category);
                Log.Info($"D{districtIndex}: {category} OFF");
            }
            else
            {
                Log.Info($"D{districtIndex}: {category} ON");
                if (categories.Count == 0) m_Blackouts.Remove(districtIndex);
            }
            BumpBlackoutStateVersion();
        }

        public void SetDistrictSchedule(int districtIndex, SchedulePreset schedule) => ModifyState(() =>
        {
            bool changed = m_PreShedStates.Remove(districtIndex);
            changed |= SetScheduleInternal(districtIndex, schedule);
            if (changed)
                BumpBlackoutStateVersion();
        });

        /// <summary>AutoDispatch-only: set schedule without updating PreShedState.</summary>
        public void SetDistrictScheduleAutoDispatch(int districtIndex, SchedulePreset schedule) => ModifyState(() =>
        {
            if (SetScheduleInternal(districtIndex, schedule))
                BumpBlackoutStateVersion();
        });

        /// <summary>AutoDispatch-only: clear explicit schedule override without updating PreShedState.</summary>
        public void ClearDistrictScheduleAutoDispatch(int districtIndex) => ModifyState(() =>
        {
            if (m_DistrictOverrides.Remove(districtIndex))
            {
                Log.Info($"D{districtIndex}: Schedule inherits city schedule");
                BumpBlackoutStateVersion();
            }
        });

        private bool SetScheduleInternal(int districtIndex, SchedulePreset schedule)
        {
            if (m_DistrictOverrides.TryGetValue(districtIndex, out var existing) && existing.Schedule == schedule)
                return false;

            if (schedule == SchedulePreset.Manual)
            {
                m_DistrictOverrides[districtIndex] = DistrictOverride.AlwaysOn;
                Log.Info($"D{districtIndex}: Schedule Manual override");
            }
            else
            {
                m_DistrictOverrides[districtIndex] = DistrictOverride.Scheduled(schedule);
                Log.Info($"D{districtIndex}: Schedule {schedule}");
            }

            return true;
        }

        public void ToggleVIP(int districtIndex) => ModifyState(() =>
        {
            m_PreShedStates.Remove(districtIndex);
            ToggleVIPInternal(districtIndex);
        });

        /// <summary>AutoDispatch-only: toggle VIP without updating PreShedState.</summary>
        public void ToggleVIPAutoDispatch(int districtIndex) => ModifyState(() =>
        {
            ToggleVIPInternal(districtIndex);
        });

        private void ToggleVIPInternal(int districtIndex)
        {
            if (m_Vips.Remove(districtIndex))
                Log.Info($"D{districtIndex}: VIP OFF");
            else
            {
                m_Vips.Add(districtIndex);
                Log.Info($"D{districtIndex}: VIP ON");
            }
            BumpBlackoutStateVersion();
        }

        private static bool HasAllBlackoutCategories(HashSet<BuildingCategory> categories)
            => categories.Count == BuildingCategories.All.Count && categories.IsSupersetOf(BuildingCategories.All);

        public void ToggleVIPBypass(int districtIndex) => ModifyState(() =>
        {
            m_PreShedStates.Remove(districtIndex);
            ToggleVIPBypassInternal(districtIndex);
        });

        /// <summary>AutoDispatch-only: toggle VIP bypass without updating PreShedState.</summary>
        public void ToggleVIPBypassAutoDispatch(int districtIndex) => ModifyState(() =>
        {
            ToggleVIPBypassInternal(districtIndex);
        });

        private void ToggleVIPBypassInternal(int districtIndex)
        {
            if (m_VipBypass.Remove(districtIndex))
                Log.Info($"D{districtIndex}: VIP Bypass OFF");
            else
            {
                m_VipBypass.Add(districtIndex);
                Log.Info($"D{districtIndex}: VIP Bypass ON");
            }
            BumpBlackoutStateVersion();
        }

        public void SetPriority(int districtIndex, int priority) => ModifyState(() =>
        {
            var districtsConfig = Core.Config.BalanceConfig.Current.Districts;
            int val = Math.Max(districtsConfig.MinPriority, Math.Min(districtsConfig.MaxPriority, priority));
            m_Priorities[districtIndex] = val;
            Log.Info($"D{districtIndex}: Priority {val}");
        });

        public void RegisterPenalty(int districtIndex, PenaltySource source) => ModifyState(() =>
        {
            if (!m_Penalties.TryGetValue(districtIndex, out var p)) p = new DistrictPenalties();
            if (DistrictPenaltyCalculator.AddSource(ref p, source))
            {
                m_Penalties[districtIndex] = p;
                Log.Info($"D{districtIndex}: Added penalty {source}");
            }
        });

        public void RemovePenalty(int districtIndex, PenaltySource source) => ModifyState(() =>
        {
            if (m_Penalties.TryGetValue(districtIndex, out var p) && DistrictPenaltyCalculator.RemoveSource(ref p, source))
            {
                if (DistrictPenaltyCalculator.IsEmpty(p))
                {
                    m_Penalties.Remove(districtIndex);
                    Log.Info($"D{districtIndex}: Cleared penalties");
                }
                else
                {
                    m_Penalties[districtIndex] = p;
                    Log.Info($"D{districtIndex}: Removed penalty {source}");
                }
            }
        });

        public void ClearPenalties() => ModifyState(() =>
        {
            if (m_Penalties.Count == 0)
                return;

            int count = m_Penalties.Count;
            m_Penalties.Clear();
            Log.Info($"Cleared {count} district penalty entries");
        });

        public void SetAutoShedded(int districtIndex, PreShedState state) => ModifyState(() =>
        {
            if (!m_PreShedStates.ContainsKey(districtIndex))
            {
                m_PreShedStates[districtIndex] = state;
                Log.Info($"D{districtIndex}: AUTO-SHEDDED (saved: schedule={state.Schedule}, catsOff={state.CategoriesOff.Count})");
                BumpBlackoutStateVersion();
            }
            // Edge Case 2: cascade shed — do NOT overwrite original player intent
        });

        public void ClearAutoShedded(int districtIndex) => ModifyState(() =>
        {
            if (m_PreShedStates.Remove(districtIndex))
            {
                Log.Info($"D{districtIndex}: AUTO-SHED CLEARED");
                BumpBlackoutStateVersion();
            }
        });

        public void ClearAllAutoShedded() => ModifyState(() =>
        {
            if (m_PreShedStates.Count > 0)
            {
                Log.Info($"Cleared {m_PreShedStates.Count} auto-shedded districts");
                m_PreShedStates.Clear();
                BumpBlackoutStateVersion();
            }
        });

        public List<int> RestoreAllAutoShedded()
        {
            var restored = new List<int>();
            ModifyState(() =>
            {
                if (m_PreShedStates.Count == 0)
                    return;

                foreach (var kvp in m_PreShedStates)
                {
                    int idx = kvp.Key;
                    var preShed = kvp.Value;

                    // Restore categories: turn ON everything AutoDispatch turned off
                    if (m_Blackouts.TryGetValue(idx, out var currentOff))
                    {
                        foreach (var cat in new List<BuildingCategory>(currentOff))
                        {
                            bool wasOffByPlayer = preShed.CategoriesOff.Contains(cat);
                            if (!wasOffByPlayer)
                                currentOff.Remove(cat);
                        }
                        if (currentOff.Count == 0) m_Blackouts.Remove(idx);
                    }

                    // Restore schedule intent: inherited city schedule must remain inherited.
                    if (preShed.HadExplicitSchedule)
                        SetScheduleInternal(idx, preShed.Schedule);
                    else if (m_DistrictOverrides.Remove(idx))
                        Log.Info($"D{idx}: Schedule inherits city schedule");

                    RestoreVipIntentLocked(idx, preShed);

                    restored.Add(idx);
                }

                Log.Info($"Restored {m_PreShedStates.Count} auto-shedded districts to player intent");
                m_PreShedStates.Clear();
                BumpBlackoutStateVersion();
            });
            return restored;
        }

        public void ClearAll() => ModifyState(() =>
        {
            bool blackoutChanged = HasBlackoutStateLocked();
            m_Blackouts.Clear();
            m_DistrictOverrides.Clear();
            m_Vips.Clear();
            m_VipBypass.Clear();
            m_PreShedStates.Clear();
            m_Priorities.Clear();
            m_Penalties.Clear();
            m_GameHour = Engine.Timing.DEFAULT_GAME_HOUR;
            m_CitySchedule = SchedulePreset.Manual;
            if (blackoutChanged)
                BumpBlackoutStateVersion();
            Log.Info("Cleared All State");
        });

        [CallerHoldsLock(nameof(m_Lock))]
        private bool HasBlackoutStateLocked()
        {
            return m_Blackouts.Count > 0 ||
                   m_DistrictOverrides.Count > 0 ||
                   m_Vips.Count > 0 ||
                   m_VipBypass.Count > 0 ||
                   m_PreShedStates.Count > 0 ||
                   m_CitySchedule != SchedulePreset.Manual;
        }

        [CallerHoldsLock(nameof(m_Lock))]
        private void RestoreVipIntentLocked(int districtIndex, in PreShedState preShed)
        {
            bool changed = false;
            if (preShed.WasVip && !m_Vips.Contains(districtIndex))
            {
                m_Vips.Add(districtIndex);
                changed = true;
            }
            else if (!preShed.WasVip && m_Vips.Remove(districtIndex))
            {
                changed = true;
            }

            if (preShed.WasVipBypass && !m_VipBypass.Contains(districtIndex))
            {
                m_VipBypass.Add(districtIndex);
                changed = true;
            }
            else if (!preShed.WasVipBypass && m_VipBypass.Remove(districtIndex))
            {
                changed = true;
            }

            if (changed)
                Log.Info($"D{districtIndex}: restored VIP intent (vip={preShed.WasVip}, bypass={preShed.WasVipBypass})");
        }

        public SchedulePreset CitySchedule
        {
            get
            {
                if (!TryEnterReadLock()) return SchedulePreset.Manual;
                try { return m_CitySchedule; }
                finally { m_Lock.ExitReadLock(); }
            }
            set => ModifyState(() =>
            {
                if (m_CitySchedule == value)
                    return;

                m_CitySchedule = value;
                BumpBlackoutStateVersion();
                Log.Info($"City Schedule: {value}");
            });
        }

        // --- Serialization ---

        public DistrictSerializationData GetSerializationData(IReadOnlyDictionary<int, DistrictRef> liveDistrictRefs)
        {
            if (!TryEnterReadLock()) return DistrictSerializationData.Empty;
            try
            {
                return new DistrictSerializationData(
                    ProjectBlackoutsToDistrictRefs(m_Blackouts, liveDistrictRefs),
                    ProjectDictionaryToDistrictRefs(m_DistrictOverrides, liveDistrictRefs),
                    ProjectSetToDistrictRefs(m_Vips, liveDistrictRefs),
                    ProjectSetToDistrictRefs(m_VipBypass, liveDistrictRefs),
                    ProjectDictionaryToDistrictRefs(m_Penalties, liveDistrictRefs),
                    ProjectPreShedToDistrictRefs(m_PreShedStates, liveDistrictRefs),
                    m_CitySchedule,
                    ProjectDictionaryToDistrictRefs(m_Priorities, liveDistrictRefs)
                );
            }
            finally { m_Lock.ExitReadLock(); }
        }

        public void LoadSerializationData(in DistrictSerializationData data)
        {
            if (!TryEnterWriteLock())
            {
                Log.Error("LoadSerializationData: failed to acquire write lock — save data DROPPED");
                return;
            }
            try
            {
                DistrictStateSerializer.LoadBlackouts(m_Blackouts, ProjectBlackoutsToRuntimeIndices(data.Blackouts));
                DistrictStateSerializer.LoadDistrictOverrides(m_DistrictOverrides, ProjectDictionaryToRuntimeIndices(data.DistrictOverrides));
                DistrictStateSerializer.LoadIntSet(m_Vips, ProjectSetToRuntimeIndices(data.Vips));
                DistrictStateSerializer.LoadIntSet(m_VipBypass, ProjectSetToRuntimeIndices(data.VipBypass));
                DistrictStateSerializer.LoadPenalties(m_Penalties, ProjectDictionaryToRuntimeIndices(data.Penalties));
                DistrictStateSerializer.LoadPreShedStates(m_PreShedStates, ProjectPreShedToRuntimeIndices(data.PreShedStates));
                m_Priorities.Clear();
                foreach (var kvp in data.Priorities)
                    m_Priorities[kvp.Key.Index] = kvp.Value;
                m_CitySchedule = data.CitySchedule;
                BumpBlackoutStateVersion();
                FlushBlackoutStateVersionDirtyLocked();
                PublishLocked();
                Log.Info($"Loaded: citySchedule={m_CitySchedule}, {m_DistrictOverrides.Count} district overrides, " +
                        $"{m_Blackouts.Count} blackouts, {m_Vips.Count} VIPs, {m_VipBypass.Count} VIPBypass, " +
                        $"{m_Penalties.Count} penalties, {m_PreShedStates.Count} preShedStates, {m_Priorities.Count} priorities");
            }
            finally { m_Lock.ExitWriteLock(); }
        }

        private static bool TryResolveLiveDistrictRef(
            int districtIndex,
            IReadOnlyDictionary<int, DistrictRef> liveDistrictRefs,
            out DistrictRef district)
        {
            if (districtIndex == 0)
            {
                district = DistrictRef.Null;
                return true;
            }

            if (liveDistrictRefs != null && liveDistrictRefs.TryGetValue(districtIndex, out district))
                return true;

            district = default;
            return false;
        }

        private static Dictionary<DistrictRef, HashSet<BuildingCategory>> ProjectBlackoutsToDistrictRefs(
            Dictionary<int, HashSet<BuildingCategory>> source,
            IReadOnlyDictionary<int, DistrictRef> liveDistrictRefs)
        {
            var result = new Dictionary<DistrictRef, HashSet<BuildingCategory>>(source.Count);
            foreach (var kvp in source)
            {
                if (TryResolveLiveDistrictRef(kvp.Key, liveDistrictRefs, out var district))
                    result[district] = new HashSet<BuildingCategory>(kvp.Value);
            }
            return result;
        }

        private static Dictionary<DistrictRef, TValue> ProjectDictionaryToDistrictRefs<TValue>(
            Dictionary<int, TValue> source,
            IReadOnlyDictionary<int, DistrictRef> liveDistrictRefs)
        {
            var result = new Dictionary<DistrictRef, TValue>(source.Count);
            foreach (var kvp in source)
            {
                if (TryResolveLiveDistrictRef(kvp.Key, liveDistrictRefs, out var district))
                    result[district] = kvp.Value;
            }
            return result;
        }

        private static HashSet<DistrictRef> ProjectSetToDistrictRefs(
            HashSet<int> source,
            IReadOnlyDictionary<int, DistrictRef> liveDistrictRefs)
        {
            var result = new HashSet<DistrictRef>();
            foreach (int districtIndex in source)
            {
                if (TryResolveLiveDistrictRef(districtIndex, liveDistrictRefs, out var district))
                    result.Add(district);
            }
            return result;
        }

        private static Dictionary<DistrictRef, PreShedState> ProjectPreShedToDistrictRefs(
            Dictionary<int, PreShedState> source,
            IReadOnlyDictionary<int, DistrictRef> liveDistrictRefs)
        {
            var result = new Dictionary<DistrictRef, PreShedState>(source.Count);
            foreach (var kvp in source)
            {
                if (!TryResolveLiveDistrictRef(kvp.Key, liveDistrictRefs, out var district))
                    continue;

                result[district] = new PreShedState(
                    kvp.Value.Schedule,
                    new HashSet<BuildingCategory>(kvp.Value.CategoriesOff),
                    kvp.Value.WasVip,
                    kvp.Value.HadExplicitSchedule,
                    kvp.Value.WasVipBypass);
            }
            return result;
        }

        private static Dictionary<int, HashSet<BuildingCategory>> ProjectBlackoutsToRuntimeIndices(
            IReadOnlyDictionary<DistrictRef, HashSet<BuildingCategory>> source)
        {
            var result = new Dictionary<int, HashSet<BuildingCategory>>(source.Count);
            foreach (var kvp in source)
                result[kvp.Key.Index] = new HashSet<BuildingCategory>(kvp.Value);
            return result;
        }

        private static Dictionary<int, TValue> ProjectDictionaryToRuntimeIndices<TValue>(
            IReadOnlyDictionary<DistrictRef, TValue> source)
        {
            var result = new Dictionary<int, TValue>(source.Count);
            foreach (var kvp in source)
                result[kvp.Key.Index] = kvp.Value;
            return result;
        }

        private static HashSet<int> ProjectSetToRuntimeIndices(IReadOnlyCollection<DistrictRef> source)
        {
            var result = new HashSet<int>();
            foreach (var district in source)
                result.Add(district.Index);
            return result;
        }

        private static Dictionary<int, PreShedState> ProjectPreShedToRuntimeIndices(
            IReadOnlyDictionary<DistrictRef, PreShedState> source)
        {
            var result = new Dictionary<int, PreShedState>(source.Count);
            foreach (var kvp in source)
            {
                result[kvp.Key.Index] = new PreShedState(
                    kvp.Value.Schedule,
                    new HashSet<BuildingCategory>(kvp.Value.CategoriesOff),
                    kvp.Value.WasVip,
                    kvp.Value.HadExplicitSchedule,
                    kvp.Value.WasVipBypass);
            }
            return result;
        }

        // --- EventBus Integration ---

        /// <summary>
        /// Initialize EventBus subscription for district lifecycle events.
        /// Call after construction, when EventBus is available.
        /// </summary>
        public void Initialize(IEventBus eventBus)
        {
            if (eventBus == null) return;
            if (Interlocked.CompareExchange(ref m_IsSubscribed, 1, 0) != 0)
                return;

            m_EventBus = eventBus;
            try
            {
                m_EventBus.Subscribe<DistrictLifecycleEvent>(OnDistrictLifecycleEvent);
            }
            catch
            {
                m_EventBus = null;
                Interlocked.Exchange(ref m_IsSubscribed, 0);
                throw;
            }
            Log.Info("Subscribed to DistrictLifecycleEvent");
        }

        private void OnDistrictLifecycleEvent(DistrictLifecycleEvent evt)
        {
            if (evt.Lifecycle == DistrictLifecycle.Destroyed)
            {
                OnDistrictDestroyed(evt.DistrictIndex);
            }
        }

        // --- Cleanup Logic ---

        public void OnDistrictDestroyed(int districtIndex) => ModifyState(() =>
        {
            bool removed = false;
            removed |= m_Blackouts.Remove(districtIndex);
            removed |= m_DistrictOverrides.Remove(districtIndex);
            removed |= m_Vips.Remove(districtIndex);
            removed |= m_VipBypass.Remove(districtIndex);
            removed |= m_PreShedStates.Remove(districtIndex);
            removed |= m_Priorities.Remove(districtIndex);
            removed |= m_Penalties.Remove(districtIndex);
            if (removed && Log.IsDebugEnabled) Log.Debug($"Cleaned destroyed D{districtIndex}");
            if (removed) BumpBlackoutStateVersion();
        });

        public int CleanupDeletedDistricts(HashSet<int> validIds)
        {
            if (validIds == null) return 0;
            // Empty validIds = all districts demolished — clean everything (don't early-exit)
            int removed = 0;
            ModifyState(() =>
            {
                removed += CleanupDict(m_Blackouts, validIds);
                removed += CleanupDict(m_DistrictOverrides, validIds);
                removed += CleanupDict(m_Priorities, validIds);
                removed += CleanupDict(m_Penalties, validIds);
                removed += m_Vips.RemoveWhere(i => !validIds.Contains(i));
                removed += m_VipBypass.RemoveWhere(i => !validIds.Contains(i));
                foreach (var key in new List<int>(m_PreShedStates.Keys))
                    if (!validIds.Contains(key)) { m_PreShedStates.Remove(key); removed++; }
                if (removed > 0) BumpBlackoutStateVersion();
            });
            if (removed > 0) Log.Info($"Cleaned {removed} zombie entries");
            return removed;
        }

        private static int CleanupDict<T>(Dictionary<int, T> dict, HashSet<int> validIds)
        {
            int removed = 0;
            foreach (var key in new List<int>(dict.Keys))
            {
                if (!validIds.Contains(key))
                {
                    dict.Remove(key);
                    removed++;
                }
            }
            return removed;
        }

        private void BumpBlackoutStateVersion()
        {
            // State publication is centralized in ModifyState/LoadSerializationData,
            // but blackout consumers need a mutation-only cursor separate from GameHour.
            m_BlackoutStateDirty = true;
        }

        [CallerHoldsLock(nameof(m_Lock))]
        private void FlushBlackoutStateVersionDirtyLocked()
        {
            if (!m_BlackoutStateDirty)
                return;

            unchecked { m_BlackoutStateEpoch++; }
            m_BlackoutStateDirty = false;
        }

        [CallerHoldsLock(nameof(m_Lock))]
        private void PublishLocked()
        {
            m_CachedSnapshot = new DistrictStateSnapshot(
                DistrictStateSerializer.CopyBlackoutsForSnapshot(m_Blackouts),
                DistrictStateSerializer.CopySchedules(m_DistrictOverrides),
                DistrictStateSerializer.CopyIntSet(m_Vips),
                DistrictStateSerializer.CopyIntSet(m_VipBypass),
                DistrictStateSerializer.CopyPenalties(m_Penalties),
                new Dictionary<int, int>(m_Priorities),
                new HashSet<int>(m_PreShedStates.Keys),
                m_GameHour,
                m_CitySchedule
            );
            m_SnapshotView.Publish(m_CachedSnapshot);
            int cachedSnapshotCursor = m_CachedSnapshotCursor;
            m_SnapshotView.Observe(ref cachedSnapshotCursor);
            m_CachedSnapshotCursor = cachedSnapshotCursor;
        }

        public void Dispose()
        {
            if (Interlocked.CompareExchange(ref m_IsDisposed, 1, 0) == 1) return;

            // Unsubscribe from EventBus
            if (Interlocked.Exchange(ref m_IsSubscribed, 0) == 1 && m_EventBus != null)
            {
                m_EventBus.Unsubscribe<DistrictLifecycleEvent>(OnDistrictLifecycleEvent);
            }

#pragma warning disable S108, CIVIC052 // Dispose teardown — exceptions expected and safe to ignore
            try { m_Lock.Dispose(); }
            catch (ObjectDisposedException) { }
            catch (System.Threading.SynchronizationLockException) { }
#pragma warning restore S108, CIVIC052
            Log.Info("Disposed");
        }
    }
}
