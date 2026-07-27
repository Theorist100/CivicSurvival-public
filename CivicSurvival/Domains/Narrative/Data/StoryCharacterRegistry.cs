using System.Collections.Generic;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Domains.Narrative.Data
{
    /// <summary>
    /// Registry of all story characters in the mod
    /// </summary>
    public static class StoryCharacterRegistry
    {
        // ===== Character Defaults =====
        private const float KOTLETA_RELATIONSHIP = 50f;
        private const float KOTLETA_COOLDOWN = 45f;
        private const float BEZKYSHENKO_RELATIONSHIP = 20f;
        private const float BEZKYSHENKO_COOLDOWN = 60f;
        private const float MARIANA_COOLDOWN = 40f;
        private const float BABCYA_COOLDOWN = 90f;
        private const float PETRENKO_COOLDOWN = 120f;
        private const float VALERA_COOLDOWN = 30f;

        public static Dictionary<string, StoryCharacter> CreateCharacters()
        {
            var characters = new Dictionary<string, StoryCharacter>();

            // ========================================
            // CORRUPT ARCHETYPE
            // ========================================

            var kotleta = new StoryCharacter("Kotleta")
            {
                NameKey = "CHAR_KOTLETA_NAME",
                RoleKey = "CHAR_KOTLETA_ROLE",
                Archetype = CharacterArchetype.Corrupt,
                Slot = BindingSlot.Restaurant,
                Relationship = KOTLETA_RELATIONSHIP,  // Starts friendly (you're both corrupt)
                ReactionCooldown = KOTLETA_COOLDOWN
            };
            kotleta.Reactions.Add(ReactionTriggers.IdleWaiting,
                "KOTLETA_IDLE_1", "KOTLETA_IDLE_2", "KOTLETA_IDLE_3");
            kotleta.Reactions.Add(ReactionTriggers.OnBind,
                "KOTLETA_BIND_1", "KOTLETA_BIND_2");
            kotleta.Reactions.Add(ReactionTriggers.OnBlackout,
                "KOTLETA_BLACKOUT_1", "KOTLETA_BLACKOUT_2", "KOTLETA_BLACKOUT_3");
            kotleta.Reactions.Add(ReactionTriggers.OnBlackoutLong,
                "KOTLETA_BLACKOUT_LONG_1");
            kotleta.Reactions.Add(ReactionTriggers.OnBlackoutExtreme,
                "KOTLETA_BLACKOUT_EXTREME_1");
            kotleta.Reactions.Add(ReactionTriggers.OnPowerRestored,
                "KOTLETA_POWER_RESTORED_1", "KOTLETA_POWER_RESTORED_2");
            kotleta.Reactions.Add(ReactionTriggers.OnShadowExport,
                "KOTLETA_EXPORT_1", "KOTLETA_EXPORT_2");
            kotleta.Reactions.Add(ReactionTriggers.OnVIPProtection,
                "KOTLETA_VIP_1", "KOTLETA_VIP_2");
            kotleta.Reactions.Add(ReactionTriggers.OnInvestigationStart,
                "KOTLETA_INVEST_1", "KOTLETA_INVEST_2");
            kotleta.Reactions.Add(ReactionTriggers.OnPoliceInvolved,
                "KOTLETA_POLICE_1", "KOTLETA_POLICE_2");
            kotleta.Reactions.Add(ReactionTriggers.OnArrest,
                "KOTLETA_ARREST_1");
            kotleta.Reactions.Add(ReactionTriggers.OnBuildingDestroyed,
                "KOTLETA_BUILDING_DESTROYED_1");
            kotleta.Reactions.Add(ReactionTriggers.Idle,
                "SATIRE_KOTLETA_1", "SATIRE_KOTLETA_2", "SATIRE_KOTLETA_3",
                "SATIRE_KOTLETA_4", "SATIRE_KOTLETA_5", "SATIRE_KOTLETA_6",
                "SATIRE_KOTLETA_7", "SATIRE_KOTLETA_8", "SATIRE_KOTLETA_9",
                "SATIRE_KOTLETA_10");
            // Milestones
            kotleta.Reactions.Add(ReactionTriggers.OnWarFatigue,
                "KOTLETA_FATIGUE_1");
            kotleta.Reactions.Add(ReactionTriggers.OnVictory,
                "KOTLETA_VICTORY_1");
            kotleta.Reactions.Add(ReactionTriggers.OnLeaving,
                "KOTLETA_LEAVING_1");
            characters.Add(kotleta.ID, kotleta);

            // ========================================
            // HONEST OFFICIAL ARCHETYPE
            // ========================================

            var bezkyshenko = new StoryCharacter("Bezkyshen'ko")
            {
                NameKey = "CHAR_BEZKYSHENKO_NAME",
                RoleKey = "CHAR_BEZKYSHENKO_ROLE",
                Archetype = CharacterArchetype.HonestOfficial,
                Slot = BindingSlot.PoliceStation,
                Relationship = -BEZKYSHENKO_RELATIONSHIP,  // Suspicious of you from start
                ReactionCooldown = BEZKYSHENKO_COOLDOWN
            };
            bezkyshenko.Reactions.Add(ReactionTriggers.IdleWaiting,
                "BEZKYSHENKO_WAIT_1", "BEZKYSHENKO_WAIT_2");
            bezkyshenko.Reactions.Add(ReactionTriggers.OnBind,
                "BEZKYSHENKO_BIND_1", "BEZKYSHENKO_BIND_2");
            bezkyshenko.Reactions.Add(ReactionTriggers.OnBlackout,
                "BEZKYSHENKO_BLACKOUT_1", "BEZKYSHENKO_BLACKOUT_2", "BEZKYSHENKO_BLACKOUT_3");
            bezkyshenko.Reactions.Add(ReactionTriggers.OnBlackoutLong,
                "BEZKYSHENKO_BLACKOUT_LONG_1", "BEZKYSHENKO_BLACKOUT_LONG_2");
            bezkyshenko.Reactions.Add(ReactionTriggers.OnBlackoutExtreme,
                "BEZKYSHENKO_BLACKOUT_EXTREME_1");
            bezkyshenko.Reactions.Add(ReactionTriggers.OnPowerRestored,
                "BEZKYSHENKO_POWER_RESTORED_1", "BEZKYSHENKO_POWER_RESTORED_2");
            bezkyshenko.Reactions.Add(ReactionTriggers.OnCorruptionHigh,
                "BEZKYSHENKO_CORRUPT_1", "BEZKYSHENKO_CORRUPT_2", "BEZKYSHENKO_CORRUPT_3");
            bezkyshenko.Reactions.Add(ReactionTriggers.OnShadowExport,
                "BEZKYSHENKO_EXPORT_1", "BEZKYSHENKO_EXPORT_2");
            bezkyshenko.Reactions.Add(ReactionTriggers.OnVIPProtection,
                "BEZKYSHENKO_VIP_1", "BEZKYSHENKO_VIP_2");
            bezkyshenko.Reactions.Add(ReactionTriggers.OnInvestigationStart,
                "BEZKYSHENKO_INVEST_1");
            bezkyshenko.Reactions.Add(ReactionTriggers.OnPoliceInvolved,
                "BEZKYSHENKO_POLICE_1", "BEZKYSHENKO_POLICE_2");
            bezkyshenko.Reactions.Add(ReactionTriggers.OnArrest,
                "BEZKYSHENKO_ARREST_1", "BEZKYSHENKO_ARREST_2");
            bezkyshenko.Reactions.Add(ReactionTriggers.OnBuildingDestroyed,
                "BEZKYSHENKO_BUILDING_DESTROYED_1");
            bezkyshenko.Reactions.Add(ReactionTriggers.Idle,
                "BEZKYSHENKO_IDLE_1", "BEZKYSHENKO_IDLE_2", "BEZKYSHENKO_IDLE_3");
            bezkyshenko.Reactions.Add(ReactionTriggers.OnLeaving,
                "BEZKYSHENKO_LEAVING_1");
            characters.Add(bezkyshenko.ID, bezkyshenko);

            // ========================================
            // JOURNALIST ARCHETYPE
            // ========================================

            var mariana = new StoryCharacter("MarianaKipish")
            {
                NameKey = "CHAR_MARIANA_NAME",
                RoleKey = "CHAR_MARIANA_ROLE",
                Archetype = CharacterArchetype.Journalist,
                Slot = BindingSlot.MediaBuilding,
                Relationship = -10f,  // Neutral-suspicious
                ReactionCooldown = MARIANA_COOLDOWN  // She's active on social media
            };
            mariana.Reactions.Add(ReactionTriggers.IdleWaiting,
                "MARIANA_WAIT_1", "MARIANA_WAIT_2");
            mariana.Reactions.Add(ReactionTriggers.OnBind,
                "MARIANA_BIND_1");
            mariana.Reactions.Add(ReactionTriggers.OnBlackout,
                "MARIANA_BLACKOUT_1", "MARIANA_BLACKOUT_2", "MARIANA_BLACKOUT_3");
            mariana.Reactions.Add(ReactionTriggers.OnBlackoutLong,
                "MARIANA_BLACKOUT_LONG_1", "MARIANA_BLACKOUT_LONG_2");
            mariana.Reactions.Add(ReactionTriggers.OnBlackoutExtreme,
                "MARIANA_BLACKOUT_EXTREME_1");
            mariana.Reactions.Add(ReactionTriggers.OnPowerRestored,
                "MARIANA_POWER_RESTORED_1", "MARIANA_POWER_RESTORED_2");
            mariana.Reactions.Add(ReactionTriggers.OnCorruptionHigh,
                "MARIANA_CORRUPT_1", "MARIANA_CORRUPT_2");
            mariana.Reactions.Add(ReactionTriggers.OnShadowExport,
                "MARIANA_EXPORT_1", "MARIANA_EXPORT_2", "MARIANA_EXPORT_3");
            mariana.Reactions.Add(ReactionTriggers.OnVIPProtection,
                "MARIANA_VIP_1", "MARIANA_VIP_2");
            mariana.Reactions.Add(ReactionTriggers.OnInvestigationStart,
                "SATIRE_INVEST_START_1", "SATIRE_INVEST_START_2", "SATIRE_INVEST_START_3",
                "SATIRE_INVEST_START_4", "SATIRE_INVEST_START_5", "SATIRE_INVEST_START_6");
            mariana.Reactions.Add(ReactionTriggers.OnInvestigationProgress,
                "SATIRE_INVEST_PROG_1", "SATIRE_INVEST_PROG_2", "SATIRE_INVEST_PROG_3",
                "SATIRE_INVEST_PROG_4", "SATIRE_INVEST_PROG_5");
            mariana.Reactions.Add(ReactionTriggers.OnArticlePublished,
                "SATIRE_ARTICLE_1", "SATIRE_ARTICLE_2", "SATIRE_ARTICLE_3",
                "SATIRE_ARTICLE_4", "SATIRE_ARTICLE_5");
            mariana.Reactions.Add(ReactionTriggers.OnPoliceInvolved,
                "MARIANA_POLICE_1", "MARIANA_POLICE_2");
            mariana.Reactions.Add(ReactionTriggers.OnArrest,
                "MARIANA_ARREST_1", "MARIANA_ARREST_2");
            mariana.Reactions.Add(ReactionTriggers.OnProtestSmall,
                "MARIANA_PROTEST_1");
            mariana.Reactions.Add(ReactionTriggers.OnProtestLarge,
                "MARIANA_PROTEST_2", "MARIANA_PROTEST_3");
            mariana.Reactions.Add(ReactionTriggers.OnBuildingDestroyed,
                "MARIANA_BUILDING_DESTROYED_1");
            mariana.Reactions.Add(ReactionTriggers.Idle,
                "MARIANA_IDLE_1", "MARIANA_IDLE_2", "MARIANA_IDLE_3");
            // Milestones
            mariana.Reactions.Add(ReactionTriggers.OnWarFatigue,
                "MARIANA_FATIGUE_1", "MARIANA_FATIGUE_2");
            mariana.Reactions.Add(ReactionTriggers.OnVictory,
                "MARIANA_VICTORY_1");
            mariana.Reactions.Add(ReactionTriggers.OnLeaving,
                "MARIANA_LEAVING_1");
            characters.Add(mariana.ID, mariana);

            // ========================================
            // CITIZEN ARCHETYPE (Babcya)
            // ========================================

            var babcya = new StoryCharacter("Babcya")
            {
                NameKey = "CHAR_BABCYA_NAME",
                RoleKey = "CHAR_BABCYA_ROLE",
                Archetype = CharacterArchetype.Citizen,
                Slot = BindingSlot.District,  // Lives in any district
                Relationship = 0f,
                ReactionCooldown = BABCYA_COOLDOWN  // Less active
            };
            babcya.Reactions.Add(ReactionTriggers.IdleWaiting,
                "BABCYA_WAIT_1", "BABCYA_WAIT_2");
            babcya.Reactions.Add(ReactionTriggers.OnBind,
                "BABCYA_BIND_1");
            babcya.Reactions.Add(ReactionTriggers.OnBlackout,
                "SATIRE_BABCYA_1", "SATIRE_BABCYA_2", "SATIRE_BABCYA_3",
                "SATIRE_BABCYA_4", "SATIRE_BABCYA_5", "SATIRE_BABCYA_6");
            babcya.Reactions.Add(ReactionTriggers.OnBlackoutLong,
                "BABCYA_BLACKOUT_LONG_1", "BABCYA_BLACKOUT_LONG_2");
            babcya.Reactions.Add(ReactionTriggers.OnBlackoutExtreme,
                "BABCYA_BLACKOUT_EXTREME_1");
            babcya.Reactions.Add(ReactionTriggers.OnPowerRestored,
                "BABCYA_POWER_RESTORED_1", "BABCYA_POWER_RESTORED_2");
            babcya.Reactions.Add(ReactionTriggers.OnBuildingDestroyed,
                "BABCYA_BUILDING_DESTROYED_1");
            babcya.Reactions.Add(ReactionTriggers.Idle,
                "BABCYA_IDLE_1", "BABCYA_IDLE_2");
            // Milestones
            babcya.Reactions.Add(ReactionTriggers.OnWarFatigue,
                "BABCYA_FATIGUE_1", "BABCYA_FATIGUE_2");
            babcya.Reactions.Add(ReactionTriggers.OnVictory,
                "BABCYA_VICTORY_1", "BABCYA_VICTORY_2");
            babcya.Reactions.Add(ReactionTriggers.OnLeaving,
                "BABCYA_LEAVING_1");
            characters.Add(babcya.ID, babcya);

            // ========================================
            // WORKER ARCHETYPE (Petrenko)
            // ========================================

            var petrenko = new StoryCharacter("Petrenko")
            {
                NameKey = "CHAR_PETRENKO_NAME",
                RoleKey = "CHAR_PETRENKO_ROLE",
                Archetype = CharacterArchetype.Worker,
                Slot = BindingSlot.PowerPlant,
                Relationship = 10f,  // Neutral, just doing his job
                ReactionCooldown = PETRENKO_COOLDOWN  // Rarely speaks
            };
            petrenko.Reactions.Add(ReactionTriggers.IdleWaiting,
                "PETRENKO_WAIT_1");
            petrenko.Reactions.Add(ReactionTriggers.OnBind,
                "PETRENKO_BIND_1");
            petrenko.Reactions.Add(ReactionTriggers.OnBlackout,
                "SATIRE_PETRENKO_1", "SATIRE_PETRENKO_2", "SATIRE_PETRENKO_3",
                "SATIRE_PETRENKO_4", "SATIRE_PETRENKO_5");
            petrenko.Reactions.Add(ReactionTriggers.OnBlackoutLong,
                "PETRENKO_BLACKOUT_LONG_1");
            petrenko.Reactions.Add(ReactionTriggers.OnBlackoutExtreme,
                "PETRENKO_BLACKOUT_EXTREME_1");
            petrenko.Reactions.Add(ReactionTriggers.OnShadowExport,
                "PETRENKO_EXPORT_1", "PETRENKO_EXPORT_2");
            petrenko.Reactions.Add(ReactionTriggers.OnPowerRestored,
                "PETRENKO_POWER_RESTORED_1", "PETRENKO_POWER_RESTORED_2");
            petrenko.Reactions.Add(ReactionTriggers.OnBuildingDestroyed,
                "PETRENKO_BUILDING_DESTROYED_1");
            petrenko.Reactions.Add(ReactionTriggers.Idle,
                "PETRENKO_IDLE_1", "PETRENKO_IDLE_2");
            // Milestones
            petrenko.Reactions.Add(ReactionTriggers.OnWarFatigue,
                "PETRENKO_FATIGUE_1");
            petrenko.Reactions.Add(ReactionTriggers.OnVictory,
                "PETRENKO_VICTORY_1");
            petrenko.Reactions.Add(ReactionTriggers.OnLeaving,
                "PETRENKO_LEAVING_1");
            characters.Add(petrenko.ID, petrenko);

            // ========================================
            // CONSPIRACY THEORIST ARCHETYPE (Valera)
            // ========================================

            var valera = new StoryCharacter("Valera")
            {
                NameKey = "CHAR_VALERA_NAME",
                RoleKey = "CHAR_VALERA_ROLE",
                Archetype = CharacterArchetype.ConspiracyTheorist,
                Slot = BindingSlot.LowDensityResidential,
                Relationship = -10f,  // Annoyed at everything from start
                ReactionCooldown = VALERA_COOLDOWN  // Posts a lot
            };

            // Idle waiting messages
            valera.Reactions.Add(ReactionTriggers.IdleWaiting,
                "VALERA_WAIT_1", "VALERA_WAIT_2");

            // Binding messages
            valera.Reactions.Add(ReactionTriggers.OnBind,
                "VALERA_BIND_1", "VALERA_BIND_2");

            // Blackout - blames export, NOT rockets
            valera.Reactions.Add(ReactionTriggers.OnBlackout,
                "VALERA_BLACKOUT_1", "VALERA_BLACKOUT_2", "VALERA_BLACKOUT_3",
                "VALERA_BLACKOUT_4", "VALERA_BLACKOUT_5");
            valera.Reactions.Add(ReactionTriggers.OnBlackoutLong,
                "VALERA_BLACKOUT_LONG_1");
            valera.Reactions.Add(ReactionTriggers.OnBlackoutExtreme,
                "VALERA_BLACKOUT_EXTREME_1");

            // Shadow export - vindication
            valera.Reactions.Add(ReactionTriggers.OnShadowExport,
                "VALERA_EXPORT_1", "VALERA_EXPORT_2", "VALERA_EXPORT_3");

            // VIP protection - elite conspiracy
            valera.Reactions.Add(ReactionTriggers.OnVIPProtection,
                "VALERA_VIP_1", "VALERA_VIP_2");

            // Power restored - suspicious of quick recovery
            valera.Reactions.Add(ReactionTriggers.OnPowerRestored,
                "VALERA_POWER_RESTORED_1", "VALERA_POWER_RESTORED_2");

            // Generator nearby - noise complaints
            valera.Reactions.Add(ReactionTriggers.OnGeneratorNearby,
                "VALERA_GENERATOR_1", "VALERA_GENERATOR_2", "VALERA_GENERATOR_3");

            // Alert phase - AA conspiracy
            valera.Reactions.Add(ReactionTriggers.OnAlert,
                "VALERA_ALERT_1", "VALERA_ALERT_2", "VALERA_ALERT_3");

            // Idle conspiracy rants
            valera.Reactions.Add(ReactionTriggers.Idle,
                "VALERA_IDLE_1", "VALERA_IDLE_2", "VALERA_IDLE_3",
                "VALERA_IDLE_4", "VALERA_IDLE_5");

            // Building destroyed
            valera.Reactions.Add(ReactionTriggers.OnBuildingDestroyed,
                "VALERA_BUILDING_DESTROYED_1");

            // Leaving the city
            valera.Reactions.Add(ReactionTriggers.OnLeaving,
                "VALERA_LEAVING_1");

            // Milestones
            valera.Reactions.Add(ReactionTriggers.OnWarFatigue,
                "VALERA_FATIGUE_1", "VALERA_FATIGUE_2");
            valera.Reactions.Add(ReactionTriggers.OnVictory,
                "VALERA_VICTORY_1");

            characters.Add(valera.ID, valera);

            foreach (var character in characters.Values)
                character.MakeReadyForFirstReaction();

            return characters;
        }
    }
}
