using System.Collections.Generic;
using CivicSurvival.Core.Interfaces.Domain.Scenario;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Domains.Countermeasures
{
    /// <summary>
    /// Satire message provider for Countermeasures domain.
    /// Registers trigger tags for investigation and protest events.
    /// </summary>
    public class CountermeasuresSatireProvider : ISatireProvider
    {
        private const int INVESTIGATION_VARIANT_COUNT = 6;

        public string Domain => "Countermeasures";

        public IReadOnlyDictionary<string, SatireConfig> GetConfigs()
        {
            return new Dictionary<string, SatireConfig>
            {
                // Investigation events - Mariana investigates
                ["SATIRE_INVEST_START"] = new("SATIRE_INVEST_START", INVESTIGATION_VARIANT_COUNT, "MARIANA", SocialMood.Suspicious),
                ["SATIRE_INVEST_PROG"] = new("SATIRE_INVEST_PROG", 5, "MARIANA", SocialMood.Suspicious),
                ["SATIRE_INVEST_STOP"] = new("SATIRE_INVEST_STOP", 3, "MARIANA", SocialMood.Suspicious),
                ["SATIRE_ARTICLE"] = new("SATIRE_ARTICLE", 5, "MARIANA", SocialMood.Angry),

                // Law enforcement
                ["SATIRE_POLICE"] = new("SATIRE_POLICE", INVESTIGATION_VARIANT_COUNT, "CITY_ALERT", SocialMood.Warning),
                ["SATIRE_ARREST"] = new("SATIRE_ARREST", 5, "CITY_ALERT", SocialMood.Angry),

                // Public unrest
                ["SATIRE_PROTEST"] = new("SATIRE_PROTEST", INVESTIGATION_VARIANT_COUNT, "CITY_ALERT", SocialMood.Angry),
                ["SATIRE_SUSPICION"] = new("SATIRE_SUSPICION", 5, "CITY_ALERT", SocialMood.Suspicious)
            };
        }
    }
}
