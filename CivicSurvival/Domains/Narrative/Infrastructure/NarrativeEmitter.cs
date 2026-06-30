using System;
using CivicSurvival.Core.Events;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Systems;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;
using CivicSurvival.Localization;
using CivicSurvival.Core.Services;

namespace CivicSurvival.Domains.Narrative.Infrastructure
{
    /// <summary>
    /// Helper for emitting common narrative notification patterns.
    /// Reduces boilerplate in resolvers: Alert + News + Chirps.
    /// </summary>
    public static class NarrativeEmitter
    {
        // FIX S19_RAG3:97 + S19_RAG4:128: Append game-time seconds to all IDs
        // so repeated events of the same type are not suppressed by cooldown dedup.
        /// <summary>
        /// Append game-time seconds to notification ID for cooldown dedup uniqueness.
        /// </summary>
        public static string TimedId(string id) => Core.Utils.NotificationIdHelper.TimedId(id);

        /// <summary>
        /// Emit a system alert notification (vanilla toast style).
        /// </summary>
        public static void Alert(
            NotificationState sink,
            string id,
            string titleKey,
            string message,
            NotificationStatus status = NotificationStatus.Info)
        {
            sink.Push(new NarrativeToastDto(
                NotificationType.SystemAlert,
                TimedId(id),
                LocalizationManager.Get(titleKey),
                message,
                Status: status
            ));
        }

        /// <summary>
        /// Emit an official news post (Herald) on the typed NEWS channel.
        ///
        /// The channel is chosen EXPLICITLY here (the caller named EmitNews), not guessed
        /// downstream from the author handle. The post is published straight onto
        /// <see cref="NewsPostEvent"/> via <see cref="IEventBus"/> — bypassing the
        /// NotificationState sink and NotificationSystem's IsOfficialAuthor demux. Its id is
        /// a content-stable <see cref="NotificationIdHelper.ContentId"/>, so NewsFeedService's
        /// m_SeenIds dedup actually collapses identical repeats (the Herald-duplicate bug).
        /// </summary>
        /// <param name="bus">Event bus to publish on (resolver-held).</param>
        /// <param name="sourceKey">Stable channel key for the content id (author handle).</param>
        /// <param name="source">Display name shown in Herald (e.g. "Ministry of Energy").</param>
        /// <param name="title">Headline text (already localized).</param>
        /// <param name="body">Body text (already localized); empty for narrative news.</param>
        /// <param name="mood">Post mood.</param>
        public static void EmitNews(
            IEventBus? bus,
            string sourceKey,
            string source,
            string title,
            string body,
            SocialMood mood = SocialMood.Neutral)
        {
            if (string.IsNullOrEmpty(title))
            {
                Mod.Log.Warn($"Skipping news post with empty title from source: {source}");
                return;
            }

            string id = NotificationIdHelper.ContentId(
                sourceKey ?? string.Empty,
                title,
                body ?? string.Empty,
                Engine.Narrative.NEWS_CONTENT_BUCKET_SECONDS);

            bus.SafePublish(new NewsPostEvent(
                id,
                source ?? string.Empty,
                title,
                body ?? string.Empty,
                mood,
                DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                "official"), "NarrativeEmitter");
        }

        /// <summary>
        /// Emit an official news post (Herald) addressed by persona group. Resolves the
        /// persona to its handle (channel chosen EXPLICITLY by the caller picking EmitNews),
        /// derives the Herald source display from <see cref="NewsAuthorRegistry"/>, and
        /// publishes a content-stable <see cref="NewsPostEvent"/> straight onto the bus —
        /// bypassing the toast sink and the NotificationSystem author demux.
        /// </summary>
        /// <param name="bus">Event bus to publish on (resolver-held).</param>
        /// <param name="personaGroup">Persona group key (resolved to an official handle).</param>
        /// <param name="title">Headline text (already localized).</param>
        /// <param name="body">Body text (already localized); empty for narrative news.</param>
        /// <param name="mood">Post mood.</param>
        public static void EmitNews(
            IEventBus? bus,
            string personaGroup,
            string title,
            string body,
            SocialMood mood = SocialMood.Neutral)
        {
            if (!PersonaRegistry.TryResolve(personaGroup, out var persona))
            {
                Mod.Log.Warn($"Skipping news post for unknown persona group: {personaGroup}");
                return;
            }

            EmitNews(
                bus,
                persona.Handle,
                NewsAuthorRegistry.GetDisplayName(persona.Handle),
                title,
                body,
                mood);
        }

        /// <summary>
        /// Emit a citizen social post (CHIPPER) addressed by persona group. Resolves the
        /// persona to its handle and publishes a <see cref="SocialPostEvent"/> straight onto
        /// the bus — the same direct channel IPSOBotMessageSystem/ScenarioStateMachine use,
        /// bypassing the toast sink and the NotificationSystem author demux. SocialFeedService
        /// dedups by author+content+window, so no per-post id is needed.
        /// </summary>
        /// <param name="bus">Event bus to publish on (resolver-held).</param>
        /// <param name="personaGroup">Persona group key (resolved to a citizen handle).</param>
        /// <param name="message">Post text (already localized).</param>
        /// <param name="mood">Post mood.</param>
        public static void EmitSocial(
            IEventBus? bus,
            string personaGroup,
            string message,
            SocialMood mood = SocialMood.Neutral)
        {
            if (!PersonaRegistry.TryResolve(personaGroup, out var persona))
            {
                Mod.Log.Warn($"Skipping social post for unknown persona group: {personaGroup}");
                return;
            }

            bus.SafePublish(new SocialPostEvent(persona.Handle, message, mood), "NarrativeEmitter");
        }
    }
}
