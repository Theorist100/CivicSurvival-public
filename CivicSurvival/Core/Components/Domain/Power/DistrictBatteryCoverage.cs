using Unity.Entities;

namespace CivicSurvival.Core.Components.Domain.Power
{
    /// <summary>
    /// Per-district battery coverage stats.
    /// Buffer element on BackupPowerStateSingleton entity.
    /// Written by: BackupPowerRuntimeSystem (district aggregation pass)
    /// Read by: MentalHealthResolverSystem (per-district mitigation)
    /// </summary>
    [InternalBufferCapacity(16)]
    public struct DistrictBatteryCoverage : IBufferElementData
    {
#pragma warning disable CIVIC262 // Not an entity ref — vanilla district buffer position
        public int DistrictIndex;
#pragma warning restore CIVIC262
        public int HospitalsTotal;
        public int HospitalsPowered;
        public int SchoolsTotal;
        public int SchoolsPowered;
        public int PrivateTotal;
        public int PrivatePowered;

        /// <summary>Hospital coverage ratio (0-1).</summary>
        public float HospitalCoverage => HospitalsTotal > 0 ? (float)HospitalsPowered / HospitalsTotal : 0f;

        /// <summary>School coverage ratio (0-1).</summary>
        public float SchoolCoverage => SchoolsTotal > 0 ? (float)SchoolsPowered / SchoolsTotal : 0f;

        /// <summary>Private coverage ratio (0-1).</summary>
        public float PrivateCoverage => PrivateTotal > 0 ? (float)PrivatePowered / PrivateTotal : 0f;
    }
}


