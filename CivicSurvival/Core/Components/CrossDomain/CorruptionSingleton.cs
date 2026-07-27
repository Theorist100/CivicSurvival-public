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
        /// <summary>Export slider position (0-100) from ShadowExportState — INTENT, not sale.</summary>
        public int ExportPercentage;

        /// <summary>
        /// Share of the city's own dispatchable capacity actually sold covertly (0-100), mirrored
        /// from <see cref="ShadowExportState.ExportLoadPercent"/>. Read by MobilizationSystem: the
        /// morale hit follows power that really left the city, not where the slider sits.
        /// The corruption SCORE deliberately keeps reading <see cref="ExportPercentage"/> — running
        /// the scheme is what creates exposure, whether or not a given day found a buyer.
        /// </summary>
        public int ExportLoadPercent;

        /// <summary>Offshore account balance from ShadowWalletSingleton.</summary>
        public long OffshoreBalance;

        /// <summary>Amount withdrawn from emergency fund (corruption indicator).</summary>
#pragma warning disable CIVIC167 // ECS IComponentData: decimal not supported in Burst/blittable
        public double EmergencyFundWithdrawn;
#pragma warning restore CIVIC167

        /// <summary>Fuel siphoning percentage (0, 15, 30, 50).</summary>
        public int FuelSiphonPercent;

        /// <summary>Draft-deferral sale rate percentage (0, 15, 30, 50) from DraftExemptionSingleton.</summary>
        public int DraftDeferralPercent;

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
            ExportLoadPercent = 0,
            OffshoreBalance = 0,
            EmergencyFundWithdrawn = 0,
            FuelSiphonPercent = 0,
            DraftDeferralPercent = 0,
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
