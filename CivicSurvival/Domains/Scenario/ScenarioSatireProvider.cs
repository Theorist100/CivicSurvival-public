using System.Collections.Generic;
using CivicSurvival.Core.Interfaces.Domain.Scenario;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Domains.Scenario
{
    /// <summary>
    /// Satire provider for Scenario domain.
    /// Provides localized messages for:
    /// - Act transitions (Crisis, Exodus, Adaptation, Routine)
    /// - Milestones (30 days, 90 days, 180 days, Victory)
    /// - Crisis Act events (banking collapse, withdraw reserves)
    /// - Refugee system events
    /// </summary>
    public class ScenarioSatireProvider : ISatireProvider
    {
        public string Domain => "Scenario";

        private readonly Dictionary<string, SatireConfig> m_Configs = new()
        {
                // ============================================
                // Crisis Act events
                // ============================================
                ["SATIRE_SHOCK_STARTED"] = new("SATIRE_SHOCK_STARTED", 3, "LOCAL_NEWS", SocialMood.Suffering),
                ["SATIRE_SHOCK_BANKING"] = new("SATIRE_SHOCK_BANKING", 3, "PRIVATBANK", SocialMood.Warning),
                ["SATIRE_SHOCK_WITHDRAW"] = new("SATIRE_SHOCK_WITHDRAW", 3, "OPPOSITION", SocialMood.Suspicious),

                // ============================================
                // Act transitions
                // ============================================
                ["SATIRE_ACT_SHOCK"] = new("SATIRE_ACT_SHOCK", 3, "LOCAL_NEWS", SocialMood.Suffering), // "Shock" = design name for Act.Crisis
                ["SATIRE_ACT_EXODUS"] = new("SATIRE_ACT_EXODUS", 3, "LOCAL_RESIDENT", SocialMood.Suffering),
                ["SATIRE_ACT_ADAPTATION"] = new("SATIRE_ACT_ADAPTATION", 3, "LOCAL_ADMIN", SocialMood.Neutral),
                ["SATIRE_ACT_ROUTINE"] = new("SATIRE_ACT_ROUTINE", 3, "POWER_ENGINEER", SocialMood.Neutral),

                // ============================================
                // Milestones
                // ============================================
                ["SATIRE_MILESTONE_30"] = new("SATIRE_MILESTONE_30", 3, "LOCAL_NEWS", SocialMood.Neutral),
                ["SATIRE_MILESTONE_90"] = new("SATIRE_MILESTONE_90", 3, "HISTORIAN", SocialMood.Neutral),
                ["SATIRE_MILESTONE_180"] = new("SATIRE_MILESTONE_180", 3, "UN_OBSERVER", SocialMood.Suffering),
                ["SATIRE_MILESTONE_VICTORY"] = new("SATIRE_MILESTONE_VICTORY", 3, "ZELENSKYY", SocialMood.Neutral),

                // ============================================
                // Refugee system
                // ============================================
                ["SATIRE_REFUGEE_ARRIVED"] = new("SATIRE_REFUGEE_ARRIVED", 5, "REFUGEE", SocialMood.Suffering),
                ["SATIRE_REFUGEE_PARK_BUILT"] = new("SATIRE_REFUGEE_PARK_BUILT", 3, "VOLUNTEER", SocialMood.Neutral),
                ["SATIRE_REFUGEE_NO_PARK"] = new("SATIRE_REFUGEE_NO_PARK", 3, "REFUGEE", SocialMood.Suffering),
                ["SATIRE_REFUGEE_COMPLETE"] = new("SATIRE_REFUGEE_COMPLETE", 3, "CITY_MAYOR", SocialMood.Neutral),
                ["SATIRE_REFUGEE_UN_NEWS"] = new("SATIRE_REFUGEE_UN_NEWS", 3, "UN_REFUGEE", SocialMood.Warning),
                ["SATIRE_REFUGEE_COLLAPSE"] = new("SATIRE_REFUGEE_COLLAPSE", 3, "WATER_COMPANY", SocialMood.Warning),
                ["SATIRE_REFUGEE_BUS"] = new("SATIRE_REFUGEE_BUS", 3, "VOLUNTEER_BUS", SocialMood.Neutral),
                ["SATIRE_REFUGEE_INTEGRATED"] = new("SATIRE_REFUGEE_INTEGRATED", 3, "LOCAL_RESIDENT", SocialMood.Neutral),

                // ============================================
                // Ominous signs (pre-war buildup)
                // ============================================
                ["SATIRE_OMINOUS_TENSIONS"] = new("SATIRE_OMINOUS_TENSIONS", 3, "BBC_WORLD", SocialMood.Warning),
                ["SATIRE_OMINOUS_WAR_1"] = new("SATIRE_OMINOUS_WAR_1", 3, "NEXTA", SocialMood.Warning),
                ["SATIRE_OMINOUS_WAR_2"] = new("SATIRE_OMINOUS_WAR_2", 3, "UN_OFFICIAL", SocialMood.Suffering),
                ["SATIRE_OMINOUS_WAR_3"] = new("SATIRE_OMINOUS_WAR_3", 3, "LOCAL_MAYOR", SocialMood.Warning),
                ["SATIRE_OMINOUS_EMERGENCY"] = new("SATIRE_OMINOUS_EMERGENCY", 3, "EMERGENCY_SERVICES", SocialMood.Warning),
                ["SATIRE_OMINOUS_SIGN"] = new("SATIRE_OMINOUS_SIGN", 5, "LOCAL_RESIDENT", SocialMood.Warning),

                // ============================================
                // Chirper storm (intro sequence - 10 posts)
                // ============================================
                ["SATIRE_STORM_VALERA_1"] = new("SATIRE_STORM_VALERA_1", 3, "VALERA", SocialMood.Paranoid),
                ["SATIRE_STORM_MAYOR"] = new("SATIRE_STORM_MAYOR", 3, "CITY_MAYOR", SocialMood.Warning),
                ["SATIRE_STORM_GRID"] = new("SATIRE_STORM_GRID", 3, "POWER_ENGINEER", SocialMood.Warning),
                ["SATIRE_STORM_RESIDENT"] = new("SATIRE_STORM_RESIDENT", 3, "LOCAL_RESIDENT", SocialMood.Suffering),
                ["SATIRE_STORM_NEXTA"] = new("SATIRE_STORM_NEXTA", 3, "NEXTA", SocialMood.Warning),
                ["SATIRE_STORM_HOSPITAL"] = new("SATIRE_STORM_HOSPITAL", 3, "HOSPITAL_WORKER", SocialMood.Neutral),
                ["SATIRE_STORM_IT"] = new("SATIRE_STORM_IT", 3, "IT_WORKER", SocialMood.Neutral),
                ["SATIRE_STORM_VALERA_2"] = new("SATIRE_STORM_VALERA_2", 3, "VALERA", SocialMood.Warning),
                ["SATIRE_STORM_BABCYA"] = new("SATIRE_STORM_BABCYA", 3, "BABCYA", SocialMood.Suffering),
                ["SATIRE_STORM_PETRENKO"] = new("SATIRE_STORM_PETRENKO", 3, "PETRENKO", SocialMood.Neutral),
        };

        public IReadOnlyDictionary<string, SatireConfig> GetConfigs() => m_Configs;
    }
}
