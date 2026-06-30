using CivicSurvival.Core.Components.Lifecycle;
using Unity.Entities;

namespace CivicSurvival.Core.Components.CrossDomain
{
    /// <summary>
    /// Aggregated corruption state as ECS singleton.
    /// Replaces CorruptionDataAdapter by aggregating values from multiple singletons.
    ///
    /// Access: SystemAPI.GetSingleton&lt;CorruptionSingleton&gt;()
    ///
    /// Writer: CorruptionStateUpdateSystem (updates each frame)
    /// Readers: CountermeasuresUpdateSystem, CountermeasuresRequestSystem,
    ///          MobilizationSystem, CorruptionCalculator
    ///
    /// Note: VIPDistricts/VIPBypass remain in ThreadSafeDistrictState.
    /// Consumers needing district data should access it directly via ServiceRegistry.
    /// </summary>
    public struct CorruptionSingleton : IComponentData
    {
        /// <summary>Export percentage (0-100) from ShadowExportState.</summary>
        public int ExportPercentage;

        /// <summary>Offshore account balance from ShadowWalletSingleton.</summary>
        public long OffshoreBalance;

        /// <summary>Amount withdrawn from emergency fund (corruption indicator).</summary>
#pragma warning disable CIVIC167 // ECS IComponentData: decimal not supported in Burst/blittable
        public double EmergencyFundWithdrawn;
#pragma warning restore CIVIC167

        /// <summary>Fuel siphoning percentage (0, 15, 30, 50).</summary>
        public int FuelSiphonPercent;

        /// <summary>Count of active shady contracts.</summary>
        public int ShadyContractCount;

        /// <summary>Count of VIP districts (for corruption calculations).</summary>
        public int VIPDistrictCount;

        /// <summary>Count of VIP bypass districts (for corruption calculations).</summary>
        public int VIPBypassCount;

        /// <summary>
        /// Accumulated corruption exposure from events (kickbacks, shadow trade, disasters).
        /// Decays over time. Added to corruption score via ExposureWeight.
        /// </summary>
        public float AccumulatedExposure;

        /// <summary>Default state.</summary>
        public static CorruptionSingleton Default => new()
        {
            ExportPercentage = 0,
            OffshoreBalance = 0,
            EmergencyFundWithdrawn = 0,
            FuelSiphonPercent = 0,
            ShadyContractCount = 0,
            VIPDistrictCount = 0,
            VIPBypassCount = 0,
            AccumulatedExposure = 0f
        };

        public static void EnsureExists(EntityManager em)
        {
            CivicSingleton.Ensure(em, Default);
        }
    }
}
