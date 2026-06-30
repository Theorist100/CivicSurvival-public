using Colossal.Logging;
using CivicSurvival.Core.Events;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;
using CivicSurvival.Domains.Narrative.Data;

namespace CivicSurvival.Domains.Narrative.Services
{
    /// <summary>
    /// Handles sending character messages to the notification system.
    /// Extracted from NarrativeSystem for better separation of concerns.
    /// </summary>
    public static class CharacterMessageSender
    {
        private static readonly LogContext Log = new("CharacterMessageSender");

        /// <summary>
        /// Send character message to NotificationSystem social feed.
        /// </summary>
        public static bool Send(StoryCharacter character, string localizationKey)
        {
            // Get localized message
            string message = Localization.LocalizationManager.Get(localizationKey);
            string authorName = Localization.LocalizationManager.Get(character.NameKey);

            // Determine mood based on archetype
            SocialMood mood = GetMoodForArchetype(character.Archetype);

            // Angry state overrides mood
            if (character.State == CharacterState.Angry)
            {
                mood = SocialMood.Angry;
            }

            // BUG-F4 FIX: Resolve fresh each call — static cache survived save reload,
            // sending messages to a stale bus. Send() is rare (~1/min), no perf concern.
            if (!ServiceRegistry.IsInitialized)
            {
                Log.Warn("EventBus unavailable: ServiceRegistry is not initialized");
                return false;
            }
            var bus = ServiceRegistry.TryGet<IEventBus>();
            if (bus == null)
            {
                Log.Warn("EventBus unavailable: service is not registered");
                return false;
            }

            // Citizen story character → CHIPPER. authorName is a display name (not an official
            // handle), so this always belonged on the social feed; publish straight onto
            // SocialPostEvent (content+author-window dedup lives in SocialFeedService).
            bus.SafePublish(new SocialPostEvent(authorName, message, mood), "CharacterMessageSender");

            if (Log.IsDebugEnabled) Log.Debug($"CHIRP [{character.ID}]: {message}");
            return true;
        }

        /// <summary>
        /// Map character archetype to social mood.
        /// </summary>
        public static SocialMood GetMoodForArchetype(CharacterArchetype archetype)
        {
            return archetype switch
            {
                CharacterArchetype.None => SocialMood.Neutral,
                CharacterArchetype.Corrupt => SocialMood.Smug,
                CharacterArchetype.HonestOfficial => SocialMood.Warning,
                CharacterArchetype.Journalist => SocialMood.Suspicious,
                CharacterArchetype.Citizen => SocialMood.Suffering,
                CharacterArchetype.Worker => SocialMood.Warning,
                CharacterArchetype.ConspiracyTheorist => SocialMood.Paranoid,
                // FIX S19_RAG1:F138: Graceful fallback instead of crash on new archetype
                _ => SocialMood.Neutral
            };
        }
    }
}
