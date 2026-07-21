using Unity.Mathematics;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Components.CrossDomain
{
    /// <summary>
    /// Effective rate formulas that combine CognitiveState (internet mode, base rates)
    /// with HeroDeploymentState (hero status, hero modifiers).
    ///
    /// Lives outside both structs because the computation crosses two singletons —
    /// neither component owns the formula on its own.
    /// </summary>
    public static class CognitiveRates
    {
        /// <summary>
        /// Effective infection rate considering global mode and hero status.
        /// Open: 100% infection, Firewall: 30%, Blackout: 0%
        /// Hero deployed reduces by HeroInfectionReduction.
        /// </summary>
        public static float EffectiveInfectionRate(in CognitiveState cs, in HeroDeploymentState hs)
        {
            // Blackout = no internet = no infection
            if (cs.InternetMode == GlobalInternetMode.Blackout)
                return 0f;

            float rate = cs.InfectionRate;

            // Firewall reduces infection
            if (cs.InternetMode == GlobalInternetMode.Firewall)
                rate *= cs.FirewallInfectionMultiplier;

            // Hero deployed reduces further
            if (hs.HeroStatus == HeroStatus.Deployed)
                rate *= (1f - hs.HeroInfectionReduction);

            // FIX S29_CODE1:F210: Clamp to 0 — config with HeroInfectionReduction > 1.0 could make rate negative
            return math.max(0f, rate);
        }

        /// <summary>
        /// Effective recovery rate considering global mode and hero status.
        /// Open: 0% recovery, Firewall: 50%, Blackout: 100%
        /// Hero lecturing adds HeroRecoveryBonus.
        /// </summary>
        public static float EffectiveRecoveryRate(in CognitiveState cs, in HeroDeploymentState hs)
        {
            // Open = full internet = no recovery (propaganda too strong)
            if (cs.InternetMode == GlobalInternetMode.Open)
                return 0f;

            float rate = cs.RecoveryRate;

            // Firewall gives partial recovery
            if (cs.InternetMode == GlobalInternetMode.Firewall)
                rate *= cs.FirewallRecoveryMultiplier;

            // Hero lecturing boosts recovery
            if (hs.HeroStatus == HeroStatus.Lecturing)
                rate *= (1f + hs.HeroRecoveryBonus);

            return math.max(0f, rate);
        }
    }
}
