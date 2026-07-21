using System.Diagnostics.CodeAnalysis;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Events;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Domains.Scenario.Systems
{
    /// <summary>
    /// Shows the one-time GLOBAL GRID online-consent agreement on first game load, in
    /// EVERY scenario (city / town / village / new game), decoupled from the narrative
    /// cold-open. Gated on the global, save-independent <see cref="ConsentStore"/>: once
    /// a decision is recorded (any Online toggle, on OR off), the modal never shows again.
    ///
    /// Sequencing with the cold-open is by modal priority: OnlineConsent (67) preempts
    /// Intro (65), so on a fresh Town/City the agreement is decided first and the
    /// narrative intro dequeues afterwards. The gate never touches sim speed — the intro
    /// owns the pause for Town/City; Village / new game need no pause here, and the modal
    /// itself is pause-safe through <see cref="ModalCoordinator"/>.
    /// </summary>
    [ActIndependent]
    [SuppressMessage("CivicSurvival", "CIVIC098", Justification = "ModalCoordinator.Instance is static readonly and never null.")]
    public partial class OnlineConsentGateSystem : EventDrivenSystemBase
    {
        private const string ModalId = "OnlineConsent";

        private static readonly LogContext Log = new("OnlineConsentGateSystem");

        protected override void OnCreate()
        {
            base.OnCreate();

            // ScenarioTypeDetectedEvent fires on every load for all scenario types
            // (single source of truth from ScenarioStateMachine); OnlineConnectionStateChangedEvent
            // is the authoritative post-write signal for any Online toggle.
            SubscribeRequired<ScenarioTypeDetectedEvent>(OnScenarioTypeDetected);
            SubscribeRequired<OnlineConnectionStateChangedEvent>(OnConnectionStateChanged);

            Log.Info("Created");
        }

        protected override void OnDestroy()
        {
            UnsubscribeSafe<ScenarioTypeDetectedEvent>(OnScenarioTypeDetected);
            UnsubscribeSafe<OnlineConnectionStateChangedEvent>(OnConnectionStateChanged);
            base.OnDestroy();
        }

        /// <summary>
        /// First scenario detection of the session — show the agreement once if the player
        /// has never made an Online decision (global ConsentStore file absent). Returning
        /// players who already chose (file present) skip straight to the cold-open.
        /// </summary>
        private void OnScenarioTypeDetected(ScenarioTypeDetectedEvent evt)
        {
            if (ConsentStore.Exists(ConsentKey.OnlineConnection))
                return;

            Log.Info("No Online consent recorded - showing GLOBAL GRID agreement");
#pragma warning disable CIVIC239 // TryShow result intentionally ignored — idempotent show, priority handles ordering
            ModalCoordinator.Instance.TryShow(ModalId);
#pragma warning restore CIVIC239
        }

        /// <summary>
        /// Any Online toggle (on OR off) records a decision via GlobalNewsSystem and is the
        /// player's answer to the agreement; release the modal slot so a queued Intro can
        /// take over (or, in Village / new game, return control to the game).
        /// </summary>
        private void OnConnectionStateChanged(OnlineConnectionStateChangedEvent evt)
        {
            ModalCoordinator.Instance.Dismiss(ModalId);
        }
    }
}
