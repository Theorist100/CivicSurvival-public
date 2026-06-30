using System.Collections.Generic;
using CivicSurvival.Core.Interfaces.Domain.Scenario;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Domains.Cognitive.Ops.Countermeasures
{
    /// <summary>
    /// Satire message provider for Buckwheat Protocol.
    /// Registers trigger tags for buckwheat distribution reactions.
    /// </summary>
    public class BuckwheatSatireProvider : ISatireProvider
    {
        public string Domain => "Cognitive.Buckwheat";

        // FIX W3-L13: Cache as static readonly — was allocating new Dictionary per call
#pragma warning disable CIVIC148 // Immutable readonly dict — no stale data risk
        private static readonly Dictionary<string, SatireConfig> s_Configs = new()
        {
            ["SATIRE_BUCKWHEAT_BABUSHKA"] = new("SATIRE_BUCKWHEAT_BABUSHKA", 5, "BABCYA", SocialMood.Neutral),
            ["SATIRE_BUCKWHEAT_VALERA"] = new("SATIRE_BUCKWHEAT_VALERA", 4, "VALERA", SocialMood.Suspicious),
            ["SATIRE_BUCKWHEAT_STUDENT"] = new("SATIRE_BUCKWHEAT_STUDENT", 3, "CITIZEN", SocialMood.Neutral),
            ["SATIRE_BUCKWHEAT_PROCUREMENT"] = new("SATIRE_BUCKWHEAT_PROCUREMENT", 3, "CITY_ALERT", SocialMood.Warning)
        };
#pragma warning restore CIVIC148

        public IReadOnlyDictionary<string, SatireConfig> GetConfigs() => s_Configs;
    }
}
