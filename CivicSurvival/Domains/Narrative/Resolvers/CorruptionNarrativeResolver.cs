using System;
using CivicSurvival.Core.Events;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Interfaces.Domain.Scenario;
using CivicSurvival.Core.Systems;
using CivicSurvival.Core.Types;
using CivicSurvival.Domains.Narrative.Infrastructure;
using CivicSurvival.Localization;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.Utils;
using CivicSurvival.Core.Serialization;

namespace CivicSurvival.Domains.Narrative.Resolvers
{
    /// <summary>
    /// Resolves corruption and countermeasures events into notification DTOs.
    /// Handles: suspicion, export, investigation, police, arrests, protests, VIP.
    /// Uses batching for protest events.
    /// </summary>
    public sealed class CorruptionNarrativeResolver : INarrativeResolver
    {
        public string Domain => "Corruption";

        private static readonly LogContext Log = new("CorruptionNarrativeResolver");

        private readonly NotificationState m_Sink;
        private IEventBus? m_EventBus;

        // State tracking
        private int m_PreviousExportedMW;
        private bool m_InvestigationActive;
        private bool m_PoliceActive;

        // R4-S4-05rev: Suppress first transition after load (state resets to defaults).
        // Time-based: deterministic duration regardless of resolver update cadence.
        private const float SUPPRESS_DURATION_HOURS = 0.01f; // ~36 game-seconds
        private float m_SuppressUntilTime;
        private bool m_IsSuppressing;

        // Batch state for protests
        private readonly BatchAggregator<(int Participants, string Location)> m_PendingProtests = new(
            BatchIdentityPolicy.NoDedup<(int Participants, string Location)>());

        public CorruptionNarrativeResolver(NotificationState sink)
        {
            m_Sink = sink;
        }

        /// <summary>
        /// R4-S4-05rev: Suppress first transition notifications after load.
        /// m_InvestigationActive/m_PoliceActive reset to false → re-fire events
        /// would show duplicate "started" notifications the player already saw.
        /// </summary>
        public void NotifyDeserialized()
        {
            m_SuppressUntilTime = -1f; // resolved on first Update
            m_IsSuppressing = true;
        }

        public NarrativeCorruptionResolverPersistState CapturePersistState()
        {
            return new NarrativeCorruptionResolverPersistState(
                m_PreviousExportedMW,
                m_InvestigationActive,
                m_PoliceActive);
        }

        public void RestorePersistState(in NarrativeCorruptionResolverPersistState state)
        {
            m_PreviousExportedMW = state.PreviousExportedMw;
            m_InvestigationActive = state.InvestigationActive;
            m_PoliceActive = state.PoliceActive;
            m_SuppressUntilTime = 0f;
            m_IsSuppressing = false;
        }

        public void Subscribe()
        {
            m_EventBus = ServiceRegistry.Instance.Require<IEventBus>();

            // HYBRID events (have logic subscribers elsewhere)
            m_EventBus.Subscribe<ExportDeficitEvent>(OnExportDeficit);
            m_EventBus.Subscribe<InvestigationStartedEvent>(OnInvestigationStarted);

            // Consolidated corruption narrative event
            m_EventBus.Subscribe<CorruptionNarrativeEvent>(OnCorruptionNarrative);
        }

        public void Unsubscribe()
        {
            if (m_EventBus == null)
            {
                Log.Warn("EventBus not cached during Unsubscribe");
                return;
            }

            m_EventBus.Unsubscribe<ExportDeficitEvent>(OnExportDeficit);
            m_EventBus.Unsubscribe<InvestigationStartedEvent>(OnInvestigationStarted);
            m_EventBus.Unsubscribe<CorruptionNarrativeEvent>(OnCorruptionNarrative);
            m_EventBus = null;
        }

        /// <summary>
        /// Self-resolving suppress check — works from both Update() and event handlers.
        /// Not gated by resolver cadence: resolves the suppress window through TryGetGameHours.
        /// </summary>
        private bool IsSuppressing()
        {
            if (!m_IsSuppressing) return false;
            if (!GameTimeSystem.TryGetGameHours(out var gameHours))
            {
                Log.Warn("GameTimeSystem unavailable while resolving narrative suppress window");
                return true;
            }
            if (m_SuppressUntilTime < 0)
                m_SuppressUntilTime = gameHours + SUPPRESS_DURATION_HOURS;
            if (gameHours >= m_SuppressUntilTime)
            {
                m_IsSuppressing = false;
                return false;
            }
            return true;
        }

        public void Update(float currentTime)
        {
            IsSuppressing(); // advance state even if no events fire this tick

            try
            {
                FlushPendingProtests();
            }
            catch (Exception ex)
            {
                Log.Error($"Error flushing protests: {ex}");
                m_PendingProtests.Clear();
            }
        }

        public void FlushAll()
        {
            var protests = m_PendingProtests.ForceFlush();
            if (protests.Count > 0)
            {
                EmitProtestNotifications(protests);
            }
        }

        /// <summary>
        /// S19-H4 FIX: Reset all internal state on New Game.
        /// Without this, m_InvestigationActive/m_PoliceActive survive New Game
        /// and first investigation/police events are silently swallowed.
        /// </summary>
        public void Reset()
        {
            m_PreviousExportedMW = 0;
            m_InvestigationActive = false;
            m_PoliceActive = false;
            m_SuppressUntilTime = 0;
            m_IsSuppressing = false;
            m_PendingProtests.Clear();
        }

        // ============================================================================
        // HYBRID EVENT HANDLERS
        // ============================================================================

        private void OnExportDeficit(ExportDeficitEvent evt)
        {
            if (System.Math.Abs(evt.ExportedMW - m_PreviousExportedMW) < 10) return;
            m_PreviousExportedMW = evt.ExportedMW;

            // R4-S4-05rev: After load, update tracking state only — suppress replay notification.
            // State update (above) runs unconditionally so post-suppress events see correct baseline.
            if (IsSuppressing()) return;

            NarrativeEmitter.EmitSocial(
                m_EventBus,
                "KOTLETA",
                SatireRegistry.GetMessage("SATIRE_KOTLETA"),
                SocialMood.Smug
            );

            NarrativeEmitter.EmitSocial(
                m_EventBus,
                "BABCYA",
                SatireRegistry.GetMessage("SATIRE_EXPORT", evt.ExportedMW),
                SocialMood.Suffering
            );
        }

        private void OnInvestigationStarted(InvestigationStartedEvent evt)
        {
            if (m_InvestigationActive) return;
            m_InvestigationActive = true;

            // NEWS: Official investigation announcement (@NEXTA_Live → Herald)
            NarrativeEmitter.EmitNews(
                m_EventBus,
                "NEXTA",
                LocalizationManager.GetRandom("NEWS_INVESTIGATION_START", evt.JournalistName, evt.FineAmount),
                string.Empty,
                SocialMood.Suspicious
            );

            // CHIRP: Citizen excitement
            NarrativeEmitter.EmitSocial(
                m_EventBus,
                "LOCAL_RESIDENT",
                LocalizationManager.GetRandom("CHIRP_INVESTIGATION", evt.JournalistName),
                SocialMood.Neutral
            );

            NarrativeEmitter.EmitSocial(
                m_EventBus,
                "MARIANA",
                SatireRegistry.GetMessage("SATIRE_INVEST_START", evt.JournalistName),
                SocialMood.Suspicious
            );
        }

        // ============================================================================
        // CONSOLIDATED CORRUPTION NARRATIVE HANDLER
        // ============================================================================

        private void OnCorruptionNarrative(CorruptionNarrativeEvent evt)
        {
            switch (evt.Type)
            {
                case CorruptionNarrativeEventType.SuspicionRising:
                    HandleSuspicionRising();
                    break;
                case CorruptionNarrativeEventType.InvestigationProgress:
                    HandleInvestigationProgress(evt.Percent);
                    break;
                case CorruptionNarrativeEventType.InvestigationStopped:
                    m_InvestigationActive = false;
                    break;
                case CorruptionNarrativeEventType.PoliceInvestigation:
                    HandlePoliceInvestigation();
                    break;
                case CorruptionNarrativeEventType.PoliceInvestigationEnded:
                    m_PoliceActive = false;
                    break;
                case CorruptionNarrativeEventType.ArticlePublished:
                    HandleArticlePublished(evt.ChargesCount);
                    break;
                case CorruptionNarrativeEventType.Arrest:
                    HandleArrest(evt.ChargesCount, evt.StolenAmount);
                    break;
                case CorruptionNarrativeEventType.ProtestStarted:
                    HandleProtestStarted(evt.Participants, evt.Location ?? string.Empty);
                    break;
                case CorruptionNarrativeEventType.VIPProtected:
                    HandleVIPProtected(evt.Location ?? string.Empty);
                    break;
                case CorruptionNarrativeEventType.VIPBypass:
                    HandleVIPBypass();
                    break;
                case CorruptionNarrativeEventType.VIPOverridden:
                    HandleVIPOverridden(evt.Location ?? string.Empty);
                    break;
                default:
                    Log.Warn($"Unhandled {nameof(CorruptionNarrativeEventType)}: {evt.Type}");
                    break;
            }
        }

        private void HandleSuspicionRising()
        {
            // @CityAlert is an official-feed handle → Herald (preserves demux routing).
            NarrativeEmitter.EmitNews(
                m_EventBus,
                "CITY_ALERT",
                SatireRegistry.GetMessage("SATIRE_SUSPICION"),
                string.Empty,
                SocialMood.Suspicious
            );
        }

        private void HandleInvestigationProgress(int evidencePercent)
        {
            if (IsSuppressing()) return;

            // NEWS: Official progress update (@NEXTA_Live → Herald)
            NarrativeEmitter.EmitNews(
                m_EventBus,
                "NEXTA",
                LocalizationManager.GetRandom("NEWS_INVESTIGATION_PROGRESS", evidencePercent),
                string.Empty,
                SocialMood.Suspicious
            );

            // CHIRP: Journalist's investigation update
            NarrativeEmitter.EmitSocial(
                m_EventBus,
                "MARIANA",
                SatireRegistry.GetMessage("SATIRE_INVEST_PROG", evidencePercent),
                SocialMood.Suspicious
            );
        }

        private void HandlePoliceInvestigation()
        {
            if (m_PoliceActive) return;
            m_PoliceActive = true;

            NarrativeEmitter.Alert(
                m_Sink,
                "police_investigation",
                "NOTIFY_TITLE_LEGAL",
                SatireRegistry.GetMessage("SATIRE_POLICE", "Detective Shevchenko"),
                NotificationStatus.Warning
            );

            NarrativeEmitter.EmitSocial(
                m_EventBus,
                "KOTLETA",
                SatireRegistry.GetMessage("SATIRE_KOTLETA_LAWYER"),
                SocialMood.Angry
            );
        }

        private void HandleArticlePublished(int corruptionExposed)
        {
            m_InvestigationActive = false;
            m_PoliceActive = false;

            NarrativeEmitter.Alert(
                m_Sink,
                "article_published",
                "NOTIFY_TITLE_BREAKING",
                SatireRegistry.GetMessage("SATIRE_ARTICLE", corruptionExposed),
                NotificationStatus.Warning
            );

            NarrativeEmitter.EmitSocial(
                m_EventBus,
                "KOTLETA",
                SatireRegistry.GetMessage("SATIRE_KOTLETA_FAKE_NEWS"),
                SocialMood.Angry
            );
        }

        private void HandleArrest(int chargesCount, long stolenAmount)
        {
            m_InvestigationActive = false;
            m_PoliceActive = false;

            // System alert
            NarrativeEmitter.Alert(
                m_Sink,
                "arrest",
                "NOTIFY_TITLE_ARREST",
                SatireRegistry.GetMessage("SATIRE_ARREST", chargesCount, stolenAmount),
                NotificationStatus.Info
            );

            // NEWS: Official arrest announcement (@NEXTA_Live → Herald)
            NarrativeEmitter.EmitNews(
                m_EventBus,
                "NEXTA",
                LocalizationManager.GetRandom("NEWS_ARREST", chargesCount, stolenAmount),
                string.Empty,
                SocialMood.Neutral
            );

            // CHIRP: Citizen celebration
            NarrativeEmitter.EmitSocial(
                m_EventBus,
                "LOCAL_RESIDENT",
                LocalizationManager.GetRandom("CHIRP_ARREST_CELEBRATION"),
                SocialMood.Neutral
            );

            // CHIRP: Babcya's reaction
            NarrativeEmitter.EmitSocial(
                m_EventBus,
                "BABCYA",
                SatireRegistry.GetMessage("SATIRE_BABCYA"),
                SocialMood.Angry
            );
        }

        private void HandleProtestStarted(int participants, string? location)
        {
#pragma warning disable CIVIC230 // NoDedup policy intentionally preserves distinct same-size/same-location incidents
            bool forceFlush = m_PendingProtests.Add((participants, location ?? string.Empty));
#pragma warning restore CIVIC230

            if (forceFlush)
            {
                var protests = m_PendingProtests.ForceFlush();
                EmitProtestNotifications(protests);
            }
        }

        private void HandleVIPProtected(string? districtName)
        {
            NarrativeEmitter.EmitSocial(
                m_EventBus,
                "KOTLETA",
                SatireRegistry.GetMessage("SATIRE_KOTLETA_VIP"),
                SocialMood.Smug
            );

            // @CityAlert is an official-feed handle → Herald (preserves demux routing).
            NarrativeEmitter.EmitNews(
                m_EventBus,
                "CITY_ALERT",
                SatireRegistry.GetMessage("SATIRE_VIP", districtName ?? "Unknown"),
                string.Empty,
                SocialMood.Suspicious
            );
        }

        private void HandleVIPBypass()
        {
            // @CityAlert is an official-feed handle → Herald (preserves demux routing).
            NarrativeEmitter.EmitNews(
                m_EventBus,
                "CITY_ALERT",
                SatireRegistry.GetMessage("SATIRE_VIP_BYPASS"),
                string.Empty,
                SocialMood.Suspicious
            );
        }

        private void HandleVIPOverridden(string districtName)
        {
            // BUG-PL-020: VIP forced to shed at CRITICAL stress — oligarch is angry

            // ALERT: System notification
            NarrativeEmitter.Alert(
                m_Sink,
                "vip_override",
                "NOTIFY_TITLE_EMERGENCY",
                LocalizationManager.Get("NOTIFY_VIP_OVERRIDE_MSG", districtName),
                NotificationStatus.Warning
            );

            // CHIRP: Kotleta is FURIOUS
            NarrativeEmitter.EmitSocial(
                m_EventBus,
                "KOTLETA",
                LocalizationManager.GetRandom("KOTLETA_VIP_OVERRIDE"),
                SocialMood.Angry
            );

            // CHIRP: Mariana reports
            NarrativeEmitter.EmitSocial(
                m_EventBus,
                "MARIANA",
                LocalizationManager.GetRandom("MARIANA_VIP_OVERRIDE", districtName),
                SocialMood.Neutral
            );
        }

        // ============================================================================
        // BATCHING
        // ============================================================================

        private void FlushPendingProtests()
        {
            if (!m_PendingProtests.IsReadyToFlush()) return;

            var protests = m_PendingProtests.FlushAndGet();
            EmitProtestNotifications(protests);
        }

        private void EmitProtestNotifications(System.Collections.Generic.IReadOnlyList<(int Participants, string Location)> protests)
        {
            if (protests.Count == 0) return;

            if (protests.Count == 1)
            {
                var (participants, location) = protests[0];
                // Single protest - normal notification with details
                NarrativeEmitter.Alert(
                    m_Sink,
                    "protest_single",
                    "NOTIFY_TITLE_PROTEST",
                    LocalizationManager.Get("NOTIFY_PROTEST_MSG", participants, location),
                    NotificationStatus.Warning
                );

                // NEWS: @NEXTA_Live → Herald.
                NarrativeEmitter.EmitNews(
                    m_EventBus,
                    "NEXTA",
                    LocalizationManager.GetRandom("NEWS_PROTEST"),
                    string.Empty,
                    SocialMood.Suspicious
                );

                NarrativeEmitter.EmitSocial(
                    m_EventBus,
                    "LOCAL_RESIDENT",
                    LocalizationManager.GetRandom("CHIRP_PROTEST"),
                    SocialMood.Suspicious
                );

                NarrativeEmitter.EmitSocial(
                    m_EventBus,
                    "KOTLETA",
                    LocalizationManager.GetRandom("CHIRP_KOTLETA_PROTEST"),
                    SocialMood.Angry
                );

                // @CityAlert is an official-feed handle → Herald (preserves demux routing).
                NarrativeEmitter.EmitNews(
                    m_EventBus,
                    "CITY_ALERT",
                    SatireRegistry.GetMessage("SATIRE_PROTEST", participants, location),
                    string.Empty,
                    SocialMood.Angry
                );
            }
            else
            {
                // Multiple protests - aggregated notification
                int protestCount = protests.Count;
                int totalParticipants = 0;
                for (int i = 0; i < protests.Count; i++)
                    totalParticipants += protests[i].Participants;

                NarrativeEmitter.Alert(
                    m_Sink,
                    "protest_batch",
                    "NOTIFY_TITLE_PROTEST",
                    LocalizationManager.Get("NOTIFY_PROTEST_BATCH_MSG", protestCount, totalParticipants),
                    NotificationStatus.Warning
                );

                // NEWS: @NEXTA_Live → Herald.
                NarrativeEmitter.EmitNews(
                    m_EventBus,
                    "NEXTA",
                    LocalizationManager.Get("NEWS_PROTEST_BATCH", protestCount),
                    string.Empty,
                    SocialMood.Suspicious
                );

                NarrativeEmitter.EmitSocial(
                    m_EventBus,
                    "KOTLETA",
                    LocalizationManager.Get("KOTLETA_PROTEST_BATCH", protestCount),
                    SocialMood.Angry
                );

                // @CityAlert is an official-feed handle → Herald (preserves demux routing).
                NarrativeEmitter.EmitNews(
                    m_EventBus,
                    "CITY_ALERT",
                    SatireRegistry.GetMessage("SATIRE_PROTEST", totalParticipants, "multiple districts"),
                    string.Empty,
                    SocialMood.Angry
                );
            }
        }
    }
}
