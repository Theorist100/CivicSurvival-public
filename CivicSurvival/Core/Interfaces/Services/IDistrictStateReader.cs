using System.Collections.Generic;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Systems;
using CivicSurvival.Core.Features.Wellbeing;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Interfaces.Services
{
    /// <summary>
    /// Read-only interface for accessing district state.
    /// Thread-safe — all methods return snapshots or immutable data.
    ///
    /// Implemented by: ThreadSafeDistrictState
    /// </summary>
    [InfrastructureService]
    public interface IDistrictStateReader
    {
        /// <summary>Thread-safe snapshot of current district state.</summary>
        DistrictStateSnapshot TakeSnapshot();

        /// <summary>Dirty version for blackout, schedule, and auto-shed state.</summary>
        int BlackoutStateVersion { get; }

        /// <summary>Current game hour (for schedule evaluation).</summary>
        float GameHour { get; }

        /// <summary>Gets district priority (1-5, default 3).</summary>
        int GetPriority(int districtIndex);

        /// <summary>Is district marked as VIP (protected from load-shedding).</summary>
        bool IsVIP(int districtIndex);

        /// <summary>Is district currently auto-shedded by dispatch system.</summary>
        bool IsAutoShedded(int districtIndex);

        /// <summary>Gets pre-shed state for an auto-shedded district. Returns false if not auto-shedded.</summary>
        bool TryGetPreShedState(int districtIndex, out PreShedState state);

        /// <summary>Is a building category turned off in this district.</summary>
        bool IsCategoryOff(int districtIndex, BuildingCategory category);

        /// <summary>Gets schedule preset for a district.</summary>
        SchedulePreset GetSchedule(int districtIndex);

        /// <summary>True when the district has an explicit schedule override instead of inheriting the city schedule.</summary>
        bool HasCustomSchedule(int districtIndex);

        /// <summary>Is the district in schedule-based blackout right now.</summary>
        bool IsScheduleBlackoutActive(int districtIndex);

        /// <summary>Gets display name of a district.</summary>
        string GetDistrictName(int districtIndex);

        /// <summary>Gets the set of district indices currently auto-shedded.</summary>
        IReadOnlyCollection<int> GetAutoSheddedDistricts();

        /// <summary>Gets the count of auto-shedded districts without allocating.</summary>
        int GetAutoSheddedCount();

        /// <summary>Is district marked as VIP Bypass (wealthy elite bypass).</summary>
        bool IsVIPBypass(int districtIndex);

        /// <summary>Does district have a specific penalty source active.</summary>
        bool HasPenalty(int districtIndex, PenaltySource source);

        /// <summary>Gets full penalty data for a district.</summary>
        DistrictPenalties GetPenalties(int districtIndex);

        /// <summary>Gets non-negative district happiness penalties for citywide penalty consumers.</summary>
        float GetPositiveHappinessPenalty(int districtIndex);

        /// <summary>Number of districts with active penalties.</summary>
        int AffectedDistrictsCount { get; }

        /// <summary>
        /// True if any runtime district state exists: blackouts, schedules, VIP flags,
        /// VIP bypass, penalties, pre-shed state, auto-shed state, or non-Manual city schedule.
        /// District priority metadata is intentionally excluded.
        /// </summary>
        bool HasAnyState { get; }

        /// <summary>City-wide schedule preset.</summary>
        SchedulePreset CitySchedule { get; }
    }
}
