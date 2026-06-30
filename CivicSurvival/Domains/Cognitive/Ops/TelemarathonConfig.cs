using Unity.Entities;

namespace CivicSurvival.Domains.Cognitive.Ops
{
    /// <summary>
    /// Owner-private config cache for the Telemarathon system.
    /// Values loaded from BalanceConfig at creation, never mutated at runtime.
    ///
    /// Writer: TelemarathonSystem (OnCreate only)
    /// Readers: TelemarathonSystem only
    ///
    /// Kept separate from TelemarathonRuntimeState so config values
    /// do not leak into the cross-domain published API.
    /// </summary>
    public struct TelemarathonConfig : IComponentData
    {
        // FIX W2-L5: Removed dead fields SoothingPanicMult, RealisticPanicMult (never read)
        public float SoothingTrustDecay;
        public float AlarmistTrustDecay;
        public float RealisticTrustRecovery;
        public float AlarmistSpotterBonus;
        public float AlarmistStressRate;
        public float FatigueMaxReduction;
        public float FatigueRatePerHour;
        // FIX W2-M7: Previously read live from BalanceConfig.Current (no null-safety, inconsistent)
        public float ShockDurationHours;
        public float ShockTrustPenalty;
        public float ShockCooldownHours;
        public float FatigueDecayOnSwitch;
    }
}
