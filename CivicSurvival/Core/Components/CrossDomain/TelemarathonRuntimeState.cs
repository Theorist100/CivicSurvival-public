using Unity.Entities;
using CivicSurvival.Core.Components.Domain.Cognitive;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Components.CrossDomain
{
    /// <summary>
    /// Published runtime facts from the Telemarathon system.
    /// Cross-domain readable — any domain may read these facts.
    ///
    /// Writer: TelemarathonSystem (Cognitive domain)
    /// Readers: SpotterAggregateSystem, AirDefenseOrchestrator, MentalHealthResolverSystem, UI
    ///
    /// Split from TelemarathonState — separates cross-domain runtime facts
    /// from owner-private config (TelemarathonConfig in Domains/Cognitive).
    ///
    /// Precomputed fields (SpotterDetectionBonus, EffectivenessMult, StressRate)
    /// are computed by TelemarathonSystem each update to avoid Cfg_* leaking cross-domain.
    /// </summary>
    public struct TelemarathonRuntimeState : IComponentData
    {
        public const float DefaultTrust = TelemarathonDefaults.DefaultTrust;

        /// <summary>Is the marathon broadcasting?</summary>
        public bool IsActive;

        /// <summary>Current narrative tone.</summary>
        public NarrativeMode Mode;

        /// <summary>Trust in official media (0–1). Falls if narrative doesn't match reality.</summary>
        public float Trust;

        /// <summary>Game hour when mode was last changed.</summary>
        public float LastModeChangeHour;

        /// <summary>Hours in Shock state (Soothing during attack). 0 = no shock.</summary>
        public float ShockHoursRemaining;

        /// <summary>Absolute game hour when shock ends. 0 = no shock.</summary>
        public float ShockEndHour;

        /// <summary>Cumulative audience fatigue (0–1). Reduces effectiveness.</summary>
        public float AudienceFatigue;

        /// <summary>Game hour when shock cooldown expires.</summary>
        public float ShockCooldownEndHour;

        /// <summary>
        /// Spotter detection bonus for this update.
        /// Precomputed: Cfg_AlarmistSpotterBonus when Mode=Alarmist, else 0.
        /// </summary>
        public float SpotterDetectionBonus;

        /// <summary>
        /// Effectiveness multiplier based on audience fatigue (1.0 = fresh, ≥0.25 = stale).
        /// Precomputed: 1f - (AudienceFatigue * Cfg_FatigueMaxReduction).
        /// </summary>
        public float EffectivenessMult;

        /// <summary>
        /// Stress rate per hour for this update.
        /// Precomputed: Cfg_AlarmistStressRate when Mode=Alarmist, else 0.
        /// </summary>
        public float StressRate;

        /// <summary>Is population in Shock state?</summary>
        public readonly bool IsInShock => ShockHoursRemaining > 0f;

        public static TelemarathonRuntimeState Default => new()
        {
            IsActive = false,
            Mode = NarrativeMode.Realistic,
            Trust = DefaultTrust,
            LastModeChangeHour = -1f,
            EffectivenessMult = 1f,
        };
    }
}
