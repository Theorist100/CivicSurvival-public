using System;
using CivicSurvival.Core.Components.Domain.Cognitive;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Types;
using Unity.Mathematics;

namespace CivicSurvival.Core.Logic
{
    /// <summary>
    /// Pure math for the pressure a LANDED discrete PSYOPS attack exerts on a stratum.
    ///
    /// A landed attack is a STATE, not an event: it presses for its whole landed window
    /// (height × duration is the balance profile — Propaganda low, FakeVideo high/short).
    /// MentalHealthResolverSystem reads the live <see cref="PsyOpsAttack"/> entities each fire
    /// and integrates this pressure over its slot dt; the launcher only advances the lifecycle.
    /// </summary>
    public static class PsyOpsRaidPressure
    {
        /// <summary>
        /// Effective per-stratum pressure of one landed attack: the wave-scaled intensity
        /// biased by the hit stratum's vulnerability, plus the rumor's shortage-confirmation
        /// top-up (its own real Food run lends it credibility). Clamped to the same [0,1]
        /// range the old emit path produced.
        /// </summary>
        public static float EffectiveIntensity(in PsyOpsAttack atk, SocialStratum stratum, CognitiveConfig cfg)
            => math.clamp(atk.Intensity * VulnMult(stratum, cfg) + RumorConfirmation(in atk, cfg), 0f, 1f);

        /// <summary>Per-stratum vulnerability multiplier (contract: Cognitive.VulnMult*).</summary>
        public static float VulnMult(SocialStratum stratum, CognitiveConfig cfg) => stratum switch
        {
            SocialStratum.Poor => cfg.VulnMultPoor,
            SocialStratum.Middle => cfg.VulnMultMiddle,
            SocialStratum.Wealthy => cfg.VulnMultWealthy,
            SocialStratum.Unknown => 1f, // not yet classified — neutral, no vuln bias
            _ => throw new ArgumentOutOfRangeException(nameof(stratum), stratum, "Unknown SocialStratum — add case to PsyOpsRaidPressure.VulnMult")
        };

        /// <summary>
        /// Confirmation top-up: a Rumor's own real shortage lends it extra credibility —
        /// a small, capped add proportional to THIS attack's intensity (its own run
        /// contribution, so simultaneous rumors do not compound). Zero for non-Rumor types.
        /// </summary>
        public static float RumorConfirmation(in PsyOpsAttack atk, CognitiveConfig cfg)
        {
            if (atk.Type != PsyOpsAttackType.Rumor)
                return 0f;
            return math.clamp(atk.Intensity * cfg.ResourceRunConfirmationWeight, 0f, cfg.ResourceRunConfirmationCap);
        }
    }
}
