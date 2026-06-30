using System.Collections.Generic;
using System.Collections.ObjectModel;
using CivicSurvival.Core.Interfaces.Domain.Scenario;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Domains.ThreatUI
{
    /// <summary>
    /// Satire message provider for Threats domain.
    /// Covers: Spotter events, Wave events, AA events.
    /// </summary>
    public class ThreatsSatireProvider : ISatireProvider
    {
        public string Domain => "Threats";

        private static readonly ReadOnlyDictionary<string, SatireConfig> s_Configs = new(new Dictionary<string, SatireConfig>
        {
            // ============================================
            // Spotter events (Valera the conspiracy theorist)
            // ============================================

            // New spotter spawned
            ["SATIRE_SPOTTER_SPAWN"] = new("SATIRE_SPOTTER_SPAWN", 3, "VALERA", SocialMood.Paranoid),

            // Spotter reactivated after silence
            ["SATIRE_SPOTTER_REACTIVATE"] = new("SATIRE_SPOTTER_REACTIVATE", 3, "VALERA", SocialMood.Paranoid),

            // Spotter posts about missile impact
            ["SATIRE_SPOTTER_IMPACT"] = new("SATIRE_SPOTTER_IMPACT", 3, "VALERA", SocialMood.Paranoid),

            // SBU visited spotter
            ["SATIRE_SPOTTER_SBU_VISIT"] = new("SATIRE_SPOTTER_SBU_VISIT", 3, "VALERA", SocialMood.Angry),

            // Spotter evacuated (forced relocation)
            ["SATIRE_SPOTTER_EVACUATED"] = new("SATIRE_SPOTTER_EVACUATED", 3, "VALERA", SocialMood.Suffering),

            // Evacuated spotter returns
            ["SATIRE_SPOTTER_RETURN"] = new("SATIRE_SPOTTER_RETURN", 3, "VALERA", SocialMood.Paranoid),

            // Vigilant civilians report spotter activity
            ["SATIRE_SPOTTER_CIVILIAN_REPORT"] = new("SATIRE_SPOTTER_CIVILIAN_REPORT", 3, "LOCAL_RESIDENT", SocialMood.Neutral),

            // ============================================
            // Counter-OSINT events (official responses)
            // ============================================

            // Counter-OSINT operation started
            ["SATIRE_COUNTER_OSINT_START"] = new("SATIRE_COUNTER_OSINT_START", 3, "CITY_ALERT", SocialMood.Neutral),

            // Counter-OSINT operation stopped
            ["SATIRE_COUNTER_OSINT_STOP"] = new("SATIRE_COUNTER_OSINT_STOP", 3, "CITY_ALERT", SocialMood.Neutral),

            // Counter-OSINT cancelled due to budget
            ["SATIRE_COUNTER_OSINT_CANCEL"] = new("SATIRE_COUNTER_OSINT_CANCEL", 3, "CITY_ALERT", SocialMood.Warning),

            // Internet disabled in district
            ["SATIRE_INTERNET_DISABLED"] = new("SATIRE_INTERNET_DISABLED", 3, "LOCAL_RESIDENT", SocialMood.Angry),

            // ============================================
            // Journalist coverage of SBU actions
            // ============================================

            // Mariana writes about SBU visit
            ["SATIRE_MARIANA_SBU"] = new("SATIRE_MARIANA_SBU", 3, "MARIANA", SocialMood.Neutral),

            // Mariana writes about internet shutdown
            ["SATIRE_MARIANA_INTERNET"] = new("SATIRE_MARIANA_INTERNET", 3, "MARIANA", SocialMood.Warning),

            // ============================================
            // Wave/AA events
            // ============================================

            // Grid stabilized after wave
            ["SATIRE_GRID_SURPLUS"] = new("SATIRE_GRID_SURPLUS", 3, "TECH_WORKER", SocialMood.Neutral),

            // AA resupply success
            ["SATIRE_AA_RESUPPLY"] = new("SATIRE_AA_RESUPPLY", 4, "VOLUNTEER", SocialMood.Neutral),

            // AA resupply partial
            ["SATIRE_AA_PARTIAL"] = new("SATIRE_AA_PARTIAL", 3, "VOLUNTEER", SocialMood.Warning),

            // AA out of ammo
            ["SATIRE_AA_EMPTY"] = new("SATIRE_AA_EMPTY", 3, "LOCAL_RESIDENT", SocialMood.Suffering),

            // AA emergency resupply
            ["SATIRE_AA_EMERGENCY"] = new("SATIRE_AA_EMERGENCY", 3, "CITY_ALERT", SocialMood.Warning)
        });

        public IReadOnlyDictionary<string, SatireConfig> GetConfigs() => s_Configs;
    }
}
