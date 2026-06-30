using System.Collections.Generic;
using CivicSurvival.Core.Interfaces.Domain.Scenario;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Domains.Engineering
{
    /// <summary>
    /// Satire message provider for Engineering domain (fires, winter outages, battery, grid, equipment).
    /// </summary>
    public class EngineeringSatireProvider : ISatireProvider
    {
        private const int PRIORITY_INFRASTRUCTURE = 6;

        public string Domain => "Engineering";

        public IReadOnlyDictionary<string, SatireConfig> GetConfigs()
        {
            return new Dictionary<string, SatireConfig>
            {
                // Engineering failures - tech workers report
                ["SATIRE_FIRE"] = new("SATIRE_FIRE", PRIORITY_INFRASTRUCTURE, "TECH_WORKER", SocialMood.Warning),
                ["SATIRE_WINTER"] = new("SATIRE_WINTER", PRIORITY_INFRASTRUCTURE, "BABCYA", SocialMood.Suffering),
                ["SATIRE_BATTERY"] = new("SATIRE_BATTERY", 5, "TECH_WORKER", SocialMood.Warning),

                // Grid stress events
                ["SATIRE_GRID_COLLAPSE"] = new("SATIRE_GRID_COLLAPSE", 3, "TECH_WORKER", SocialMood.Suffering),

                // Equipment failures
                ["SATIRE_EQUIPMENT_EXPLOSION"] = new("SATIRE_EQUIPMENT_EXPLOSION", 3, "TECH_WORKER", SocialMood.Warning)
            };
        }
    }
}
