using System.Collections.Generic;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Interfaces.Domain.ThreatDamage
{
    /// <summary>
    /// Service for reading civilian building damage data for UI display.
    /// Provides read-only access to damaged civilian buildings.
    ///
    /// Implementor: CivilianDamageSystem (ThreatDamage domain)
    /// Consumer: PowerGridUISystem (PowerGrid domain)
    /// </summary>
    [GenerateNullObject]
    [OwnedByFeatureId(FeatureIds.ThreatDamageName)]
    public interface ICivilianDamageReader
    {
        /// <summary>
        /// Single atomic carrier for damaged civilian building snapshot and version.
        /// </summary>
        [NullReturnNull]
        IVersionedView<CivilianDamageSnapshot>? DamageView { get; }

        /// <summary>Count of buildings with damage (not repairing).</summary>
        int DamagedCount { get; }

        /// <summary>Count of buildings currently under repair.</summary>
        int RepairingCount { get; }

        /// <summary>
        /// Tuple return (out parameters are not supported by NullObjectGenerator
        /// — CIVIC420). Item1 is success flag; Item2 carries the view when true,
        /// or default(CivilianRepairView) otherwise.
        /// </summary>
        (bool found, CivilianRepairView view) GetRepairState(int buildingIndex, int buildingVersion);

        bool HasPendingRepairIntent(int buildingIndex, int buildingVersion);
    }
}
