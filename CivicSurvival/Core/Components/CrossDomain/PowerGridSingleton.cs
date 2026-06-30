using CivicSurvival.Core.Components.Lifecycle;
using Unity.Entities;

namespace CivicSurvival.Core.Components.CrossDomain
{
    /// <summary>
    /// Global power grid state as ECS singleton.
    /// Replaces IPowerGridService for data access.
    ///
    /// Access: SystemAPI.GetSingleton&lt;PowerGridSingleton&gt;()
    /// Modify: SystemAPI.GetSingletonRW&lt;PowerGridSingleton&gt;()
    ///
    /// Writer: PowerGridDataSystem (PowerGrid domain)
    /// Readers: Multiple domains (Engineering, Threats, AirDefense, Corruption, UI)
    ///
    /// Note: Removed ProducerCount/ConsumerCount (unused - query vanilla ElectricityStatisticsSystem if needed)
    /// </summary>
    public struct PowerGridSingleton : IComponentData
    {
        /// <summary>Total kW produced by all power plants</summary>
        public int Production;

        /// <summary>Total kW demand from all buildings (what they want)</summary>
        public int Demand;

        /// <summary>Scheduled active load — sum of enabled-category KW across non-blackout districts.
        /// NOT post-threshold fulfilled consumption. To compute delivered load, subtract
        /// ThresholdStateSingleton.CutoffKW (and other downstream cuts) from this value.</summary>
        public int Consumption;

        /// <summary>Production - Consumption before shadow export (raw surplus), kW</summary>
        public int RawBalance;

        /// <summary>Production - Consumption - ShadowExport (effective balance), kW</summary>
        public int Balance;

        /// <summary>Grid status: 0=normal, 1=warning, 2=critical</summary>
        public GridStatusType Status;

        /// <summary>External power from donors/imports in kW (already included in Production)</summary>
        public int ExternalPower;

        /// <summary>Shadow export drain in kW (already subtracted from Balance)</summary>
        public int ShadowExportDrain;

        // A2 FIX 2b: IsWinterActive moved to WinterStateSingleton (was multi-writer on PowerGridSingleton)

        public void SetDefaults() => this = default;

        public readonly string GetStatusString() => Status switch
        {
            GridStatusType.Normal => "normal",
            GridStatusType.Warning => "warning",
            GridStatusType.Critical => "critical",
            GridStatusType.Surplus => "surplus",
            _ => "unknown"
        };

        public static PowerGridSingleton Default => new();

        public static void EnsureExists(EntityManager em)
        {
            CivicSingleton.Ensure(em, Default);
        }
    }

    public enum GridStatusType : byte
    {
        Normal = 0,
        Warning = 1,
        Critical = 2,
        Surplus = 3
    }
}


