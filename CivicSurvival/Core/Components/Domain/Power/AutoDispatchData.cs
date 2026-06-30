using Unity.Entities;

namespace CivicSurvival.Core.Components.Domain.Power
{
    /// <summary>
    /// Singleton component for Auto-Dispatch state.
    /// Automatically sheds load when grid stress exceeds threshold.
    /// </summary>
    public struct AutoDispatchData : IComponentData
    {
        public const int MAX_SHED_COUNT = 10000;

        /// <summary>True when auto-dispatch is enabled by player.</summary>
        public bool Enabled;

        /// <summary>Number of districts currently shed by auto-dispatch.</summary>
        public int AutoSheddedCount;

        /// <summary>
        /// True when PANIC mode active but all non-VIP districts already shed.
        /// UI should warn player that VIP protection is blocking load shedding.
        /// </summary>
        public bool IsBlockedByVip;

        public static AutoDispatchData CreateDefault() => new()
        {
            Enabled = false,
            AutoSheddedCount = 0,
            IsBlockedByVip = false
        };

        public void SetDefaults() => this = CreateDefault();
    }
}


