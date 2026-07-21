using System.Collections.Generic;
using CivicSurvival.Core.Interfaces.Domain.Scenario;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Domains.Narrative
{
    /// <summary>
    /// Satire message provider for Narrative domain.
    /// Registers trigger tags for characters and donor events.
    /// </summary>
    public class NarrativeSatireProvider : ISatireProvider
    {
        private const int BABCYA_VARIANT_COUNT = 6;

        public string Domain => "Narrative";

        public IReadOnlyDictionary<string, SatireConfig> GetConfigs()
        {
            return new Dictionary<string, SatireConfig>
            {
                // ============================================
                // Character persona messages (for direct use)
                // ============================================
                ["SATIRE_KOTLETA"] = new("SATIRE_KOTLETA", 10, "KOTLETA", SocialMood.Smug),
                ["SATIRE_KOTLETA_VIP"] = new("SATIRE_KOTLETA_VIP", 3, "KOTLETA", SocialMood.Smug),
                ["SATIRE_BABCYA"] = new("SATIRE_BABCYA", BABCYA_VARIANT_COUNT, "BABCYA", SocialMood.Suffering),
                ["SATIRE_PETRENKO"] = new("SATIRE_PETRENKO", 5, "PETRENKO", SocialMood.Warning),
                ["SATIRE_VALERA"] = new("SATIRE_VALERA", 5, "VALERA", SocialMood.Suspicious),
                ["SATIRE_IT"] = new("SATIRE_IT", 5, "IT_WORKER", SocialMood.Warning),
                ["SATIRE_VOLUNTEER"] = new("SATIRE_VOLUNTEER", 5, "VOLUNTEER", SocialMood.Neutral),
                ["SATIRE_TAXI"] = new("SATIRE_TAXI", 5, "TAXI", SocialMood.Suffering),

                // ============================================
                // Threat events - social reactions
                // ============================================
                ["SATIRE_THREAT_IMPACT"] = new("SATIRE_THREAT_IMPACT", 5, "CITIZEN", SocialMood.Suffering),
                ["SATIRE_THREAT_DEBRIS"] = new("SATIRE_THREAT_DEBRIS", 4, "TECH_WORKER", SocialMood.Warning),
                ["SATIRE_THREAT_SUCCESS"] = new("SATIRE_THREAT_SUCCESS", 4, "TECH_WORKER", SocialMood.Neutral),
                // AA satire entries moved to ThreatsSatireProvider (canonical location)

                // ============================================
                // Donor events - international aid reactions
                // ============================================
                ["SATIRE_DONOR_FUNDS"] = new("SATIRE_DONOR_FUNDS", 5, "BABCYA", SocialMood.Neutral),
                ["SATIRE_DONOR_GENERATORS"] = new("SATIRE_DONOR_GENERATORS", 5, "BABCYA", SocialMood.Neutral),
                ["SATIRE_DONOR_PATRIOT"] = new("SATIRE_DONOR_PATRIOT", 5, "CITY_ALERT", SocialMood.Neutral),
                ["SATIRE_DONOR_PATRIOT_EXPIRED"] = new("SATIRE_DONOR_PATRIOT_EXPIRED", 5, "CITY_ALERT", SocialMood.Warning),
                ["SATIRE_DONOR_CALLED"] = new("SATIRE_DONOR_CALLED", 3, "MARIANA", SocialMood.Suspicious),
                ["SATIRE_DONOR_REFUSED"] = new("SATIRE_DONOR_REFUSED", 5, "MARIANA", SocialMood.Suspicious),
                ["SATIRE_DONOR_SUSPICION"] = new("SATIRE_DONOR_SUSPICION", 5, "MARIANA", SocialMood.Suspicious),
                ["SATIRE_SANCTIONS_APPLIED"] = new("SATIRE_SANCTIONS_APPLIED", 3, "CITY_ALERT", SocialMood.Warning)
            };
        }
    }
}
