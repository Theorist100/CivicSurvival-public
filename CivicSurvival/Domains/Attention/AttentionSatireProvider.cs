using System.Collections.Generic;
using CivicSurvival.Core.Interfaces.Domain.Scenario;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Domains.Attention
{
    /// <summary>
    /// Satire message provider for Attention domain.
    /// Covers: World Shock events, Exodus events, International media reactions.
    /// </summary>
    public class AttentionSatireProvider : ISatireProvider
    {
        public string Domain => "Attention";

        public IReadOnlyDictionary<string, SatireConfig> GetConfigs()
        {
            return new Dictionary<string, SatireConfig>
            {
                // ============================================
                // World Shock - International Media
                // ============================================

                // GlobalShock tier - breaking news
                ["SATIRE_SHOCK_GLOBAL_BREAKING"] = new("SATIRE_SHOCK_GLOBAL_BREAKING", 3, "NEXTA", SocialMood.Warning),

                // GlobalShock tier - UN response
                ["SATIRE_SHOCK_GLOBAL_UN"] = new("SATIRE_SHOCK_GLOBAL_UN", 3, "UN_AID", SocialMood.Neutral),

                // Headlines tier - media coverage
                ["SATIRE_SHOCK_HEADLINES"] = new("SATIRE_SHOCK_HEADLINES", 3, "NEXTA", SocialMood.Warning),

                // DeepConcern tier - stabilizing
                ["SATIRE_SHOCK_STABILIZING"] = new("SATIRE_SHOCK_STABILIZING", 3, "UN_AID", SocialMood.Neutral),

                // Critical infrastructure hit (hospital, school, water)
                ["SATIRE_SHOCK_CRITICAL_INFRA"] = new("SATIRE_SHOCK_CRITICAL_INFRA", 5, "NEXTA", SocialMood.Suffering),

                // ============================================
                // Exodus - Citizens leaving
                // ============================================

                // Mass exodus during GlobalShock (param: {0} = families count)
                ["SATIRE_EXODUS_MASS"] = new("SATIRE_EXODUS_MASS", 3, "LOCAL_RESIDENT", SocialMood.Suffering),

                // Moderate exodus during Headlines
                ["SATIRE_EXODUS_MODERATE"] = new("SATIRE_EXODUS_MODERATE", 3, "LOCAL_RESIDENT", SocialMood.Suffering),

                // Brain drain - tech workers leaving
                ["SATIRE_EXODUS_BRAINDRAIN"] = new("SATIRE_EXODUS_BRAINDRAIN", 3, "TECH_WORKER", SocialMood.Neutral)
            };
        }
    }
}
