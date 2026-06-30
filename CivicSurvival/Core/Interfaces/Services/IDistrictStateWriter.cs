using System.Collections.Generic;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Interfaces.Services
{
    /// <summary>
    /// Write interface for district state mutations.
    /// Separate from IDistrictStateReader for clean separation of concerns.
    ///
    /// Implemented by: ThreadSafeDistrictState
    /// Used by: Systems that need to modify district state
    /// </summary>
    [InfrastructureService]
    public interface IDistrictStateWriter
    {
        /// <summary>
        /// Removes districts that no longer exist in the game.
        /// Called periodically by UI panels to sync with game state.
        /// </summary>
        /// <param name="validIds">Set of district indices that currently exist.</param>
        /// <returns>Number of districts removed.</returns>
        int CleanupDeletedDistricts(HashSet<int> validIds);

        /// <summary>Current game hour (for schedule evaluation).</summary>
        float GameHour { get; set; }

        /// <summary>Sets schedule preset for a district.</summary>
        void SetDistrictSchedule(int districtIndex, SchedulePreset schedule);

        /// <summary>Toggles all categories on/off for a district.</summary>
        void ToggleDistrictBlackout(int districtIndex);

        /// <summary>Idempotently sets all categories off/on for a district.</summary>
        void SetDistrictBlackout(int districtIndex, bool blackedOut);

        /// <summary>Toggles a specific building category on/off for a district.</summary>
        void ToggleDistrictCategory(int districtIndex, BuildingCategory category);

        /// <summary>Toggles VIP status for a district.</summary>
        void ToggleVIP(int districtIndex);

        /// <summary>Toggles VIP Bypass (wealthy elite bypass) for a district.</summary>
        void ToggleVIPBypass(int districtIndex);

        /// <summary>Sets district priority (clamped to config min/max).</summary>
        void SetPriority(int districtIndex, int priority);

        /// <summary>Registers a penalty source for a district.</summary>
        void RegisterPenalty(int districtIndex, PenaltySource source);

        /// <summary>Removes a penalty source from a district.</summary>
        void RemovePenalty(int districtIndex, PenaltySource source);

        /// <summary>Clears district penalty state only, preserving blackout schedules, VIP flags, priorities, and auto-shed state.</summary>
        void ClearPenalties();

        /// <summary>Clears all district state (blackouts, schedules, VIPs, penalties, etc).</summary>
        void ClearAll();

        /// <summary>City-wide schedule preset.</summary>
        SchedulePreset CitySchedule { get; set; }
    }

    /// <summary>
    /// AutoDispatch-only district state mutations. These bypass player-intent preservation
    /// in controlled ways and should not be exposed to general district writers.
    /// </summary>
    [InfrastructureService]
    public interface IAutoDispatchStateWriter
    {
        /// <summary>Marks district as auto-shedded and saves player's pre-shed state.</summary>
        void SetAutoShedded(int districtIndex, PreShedState state);

        /// <summary>Clears auto-shedded flag and removes pre-shed state.</summary>
        void ClearAutoShedded(int districtIndex);

        /// <summary>Clears all auto-shedded districts (discards PreShedState without restoring).</summary>
        void ClearAllAutoShedded();

        /// <summary>Restores all auto-shedded districts to player intent, then clears PreShedState. Returns restored district indices for event publication.</summary>
        List<int> RestoreAllAutoShedded();

        /// <summary>Set schedule without updating PreShedState.</summary>
        void SetDistrictScheduleAutoDispatch(int districtIndex, SchedulePreset schedule);

        /// <summary>Clear explicit schedule override without updating PreShedState.</summary>
        void ClearDistrictScheduleAutoDispatch(int districtIndex);

        /// <summary>Toggle category without updating PreShedState.</summary>
        void ToggleDistrictCategoryAutoDispatch(int districtIndex, BuildingCategory category);

        /// <summary>Toggle VIP without updating PreShedState.</summary>
        void ToggleVIPAutoDispatch(int districtIndex);

        /// <summary>Toggle VIP bypass without updating PreShedState.</summary>
        void ToggleVIPBypassAutoDispatch(int districtIndex);
    }

    [OwnedByFeatureId(FeatureIds.PowerGridName)]
    public interface IAutoDispatchSettingsWriter
    {
        bool TryToggleAutoDispatch(out bool enabled, out List<int> restoredDistricts);
    }

    [OwnedByFeatureId(FeatureIds.PowerBackupName)]
    public interface IBackupPowerPolicyWriter
    {
        bool TrySetBackupPolicy(BackupPolicy policy);
    }

    [OwnedByFeatureId(FeatureIds.CorruptionName)]
    public interface IFuelSiphoningSettingsWriter
    {
        bool TrySetFuelSiphonPercent(int percent);
    }

    [OwnedByFeatureId(FeatureIds.CorruptionName)]
    public interface IFuelSiphoningStateReader
    {
        int SiphonPercent { get; }
        float ConsumptionMultiplier { get; }
    }
}
