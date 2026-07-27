using System.Collections.Generic;
using CivicSurvival.Core.Interfaces.Domain.Scenario;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Domains.Blackout
{
    /// <summary>
    /// Satire message provider for Blackout domain.
    /// Registers trigger tags with full config (author + message).
    /// </summary>
    public class BlackoutSatireProvider : ISatireProvider
    {
        private const int BLACKOUT_VARIANT_COUNT = 7;

        public string Domain => "Blackout";

        public IReadOnlyDictionary<string, SatireConfig> GetConfigs()
        {
            return new Dictionary<string, SatireConfig>
            {
                // Blackout events - citizen suffering
                ["SATIRE_BLACKOUT"] = new("SATIRE_BLACKOUT", BLACKOUT_VARIANT_COUNT, "BABCYA", SocialMood.Suffering),
                ["SATIRE_RESTORED"] = new("SATIRE_RESTORED", 4, "LOCAL_RESIDENT", SocialMood.Neutral)
            };
        }
    }
}
