using System.Collections.Generic;
using CivicSurvival.Core.Interfaces.Domain.Scenario;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Domains.Corruption
{
    /// <summary>
    /// Satire message provider for Corruption domain.
    /// Registers trigger tags with full config (author + message).
    /// </summary>
    public class CorruptionSatireProvider : ISatireProvider
    {
        private const int VIP_SATIRE_VARIANTS = 7;
        private const int ELITE_SATIRE_VARIANTS = 3;
        private const int VIP_BYPASS_SATIRE_VARIANTS = 3;
        private const int EXPORT_SATIRE_VARIANTS = 7;
        private const int DISASTER_SATIRE_VARIANTS = 5;
        private const int SHADY_DISASTER_SATIRE_VARIANTS = 4;
        private const int IMPORT_SATIRE_VARIANTS = 4;
        private const int PROCUREMENT_CORRUPT_SATIRE_VARIANTS = 2;
        private const int PROCUREMENT_HONEST_SATIRE_VARIANTS = 2;
        private const int COUNTERFEIT_FIRE_SATIRE_VARIANTS = 3;
        private const int SHADOW_DISCOVERED_SATIRE_VARIANTS = 3;
        private const int SHADOW_SANCTIONS_LIFTED_SATIRE_VARIANTS = 2;
        private const int KOTLETA_LAWYER_SATIRE_VARIANTS = 1;
        private const int KOTLETA_FAKE_NEWS_SATIRE_VARIANTS = 1;

        public string Domain => "Corruption";

        public IReadOnlyDictionary<string, SatireConfig> GetConfigs()
        {
            return new Dictionary<string, SatireConfig>
            {
                // VIP protection - city alert notices
                ["SATIRE_VIP"] = new("SATIRE_VIP", VIP_SATIRE_VARIANTS, "CITY_ALERT", SocialMood.Suspicious),
                ["SATIRE_ELITE"] = new("SATIRE_ELITE", ELITE_SATIRE_VARIANTS, "KOTLETA", SocialMood.Smug),
                ["SATIRE_VIP_BYPASS"] = new("SATIRE_VIP_BYPASS", VIP_BYPASS_SATIRE_VARIANTS, "CITY_ALERT", SocialMood.Suspicious), // FIX W2-M5

                // Export corruption - Babcya suffers, Kotleta gloats
                ["SATIRE_EXPORT"] = new("SATIRE_EXPORT", EXPORT_SATIRE_VARIANTS, "BABCYA", SocialMood.Suffering),

                // Engineering failures
                ["SATIRE_DISASTER"] = new("SATIRE_DISASTER", DISASTER_SATIRE_VARIANTS, "TECH_WORKER", SocialMood.Warning),
                ["SATIRE_SHADY_DISASTER"] = new("SATIRE_SHADY_DISASTER", SHADY_DISASTER_SATIRE_VARIANTS, "TECH_WORKER", SocialMood.Suspicious),
                ["SATIRE_IMPORT"] = new("SATIRE_IMPORT", IMPORT_SATIRE_VARIANTS, "TECH_WORKER", SocialMood.Warning),

                // Shadow Procurement - corruption scheme
                ["SATIRE_PROCUREMENT_CORRUPT"] = new("SATIRE_PROCUREMENT_CORRUPT", PROCUREMENT_CORRUPT_SATIRE_VARIANTS, "KOTLETA", SocialMood.Smug),
                ["SATIRE_PROCUREMENT_HONEST"] = new("SATIRE_PROCUREMENT_HONEST", PROCUREMENT_HONEST_SATIRE_VARIANTS, "CITY_ALERT", SocialMood.Neutral),
                ["SATIRE_KOTLETA_LAWYER"] = new("SATIRE_KOTLETA_LAWYER", KOTLETA_LAWYER_SATIRE_VARIANTS, "KOTLETA", SocialMood.Smug),
                ["SATIRE_KOTLETA_FAKE_NEWS"] = new("SATIRE_KOTLETA_FAKE_NEWS", KOTLETA_FAKE_NEWS_SATIRE_VARIANTS, "KOTLETA", SocialMood.Smug),

                // Counterfeit battery fires - consequences of corruption
                ["SATIRE_COUNTERFEIT_FIRE"] = new("SATIRE_COUNTERFEIT_FIRE", COUNTERFEIT_FIRE_SATIRE_VARIANTS, "LOCAL_RESIDENT", SocialMood.Angry),

                // Shadow Import discovery - scandal
                ["SATIRE_SHADOW_DISCOVERED"] = new("SATIRE_SHADOW_DISCOVERED", SHADOW_DISCOVERED_SATIRE_VARIANTS, "MARIANA", SocialMood.Suspicious),
                ["SATIRE_SHADOW_SANCTIONS_LIFTED"] = new("SATIRE_SHADOW_SANCTIONS_LIFTED", SHADOW_SANCTIONS_LIFTED_SATIRE_VARIANTS, "CITY_ALERT", SocialMood.Neutral)
            };
        }
    }
}
