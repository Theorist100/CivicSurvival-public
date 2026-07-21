using System.Collections.Generic;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.UI.DomainState;

namespace CivicSurvival.Core.Interfaces.Domain.Corruption
{
    /// <summary>
    /// Service interface for District Modernization (Shadow Procurement) operations.
    /// Provides read access to program state and cost calculations.
    ///
    /// Note: RecordFire() uses FireRecordRequest (Data-Driven Commands pattern).
    ///
    /// Implementor: DistrictModernizationSystem (Corruption domain)
    /// Consumers: BackupPowerUIPanel (PowerBackup), CounterfeitBatteryFireSystem (PowerBackup)
    /// Null-object: GetProgram returns null (no program ever launched), KnownDistricts/
    /// PendingCounterfeitCleanupDistricts empty, costs/version = 0, eligibility = default
    /// (no scheme available).
    /// </summary>
    [GenerateNullObject]
    [OwnedByFeatureId(FeatureIds.CorruptionName)]
    public interface IDistrictModernizationService
    {
        [NullReturnNull]
        IVersionedView<ModernizationProgramsSnapshot>? ProgramsView { get; }

        /// <summary>
        /// Program data for the given district, or null when no program has been
        /// launched. Replaces the live-dictionary leak of the previous Programs
        /// getter; consumers no longer see implementor's mutable state.
        /// </summary>
        [NullReturnNull]
        DistrictModernizationData? GetProgram(int districtIndex);

        /// <summary>
        /// District indices that currently have an active modernization program.
        /// Caller-owned iteration: snapshot taken by the implementor; consumers
        /// must not retain across frames.
        /// </summary>
        [NullReturnEmpty]
        IReadOnlyCollection<int> ActiveProgramDistricts { get; }

        /// <summary>
        /// Snapshot of all district indices with at least one electricity-consumer building.
        /// Callers may iterate these indices and ask CanModernizeDistrict/GetEstimatedCost
        /// even when no program has been launched yet.
        /// </summary>
        [NullReturnEmpty]
        IReadOnlyCollection<int> KnownDistricts { get; }

        /// <summary>Days until next procurement is allowed (cooldown).</summary>
        int DaysUntilNextProcurement { get; }

        /// <summary>
        /// Calculate estimated cost for district modernization.
        /// </summary>
        /// <param name="districtIndex">District to calculate cost for.</param>
        /// <returns>Estimated cost in city budget currency; 0 when the district has no eligible buildings.</returns>
        int GetEstimatedCost(int districtIndex);

        /// <summary>
        /// Backend-owned eligibility verdict for launching modernization.
        /// </summary>
        EligibilityFlag GetModernizationEligibility(int districtIndex, ContractorType contractor);

        /// <summary>
        /// Snapshot of district indices pending counterfeit battery cleanup.
        /// Prefer <see cref="CopyPendingCounterfeitCleanupDistricts"/> in per-frame consumers.
        /// </summary>
        [NullReturnEmpty]
        IReadOnlyCollection<int> PendingCounterfeitCleanupDistricts { get; }

        /// <summary>
        /// Copy pending cleanup districts into caller-owned storage without exposing the live set.
        /// Caller should clear via <see cref="ClearPendingCounterfeitCleanup"/> after processing.
        /// </summary>
        void CopyPendingCounterfeitCleanupDistricts(List<int> target);

        void CopyPendingCounterfeitCleanupBuildingKeys(List<long> target);

        /// <summary>Clear pending cleanup set after MCS has processed it.</summary>
        void ClearPendingCounterfeitCleanup();
    }
}
