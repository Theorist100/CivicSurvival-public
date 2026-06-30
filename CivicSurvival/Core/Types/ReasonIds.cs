using CivicSurvival.Core.Attributes;

namespace CivicSurvival.Core.Types
{
    /// <summary>
     /// Canonical locale ids for predicate rejections.
     /// </summary>
    public static partial class ReasonIds
    {
        [UiReasonId("Action requires war state")]
        public static readonly ReasonId PreWarLocked = ReasonId.Of("UI_ACTION_PREWAR_LOCKED");
        [UiReasonId("Scenario singleton unavailable")]
        public static readonly ReasonId ScenarioUnavailable = ReasonId.Of("UI_SCENARIO_UNAVAILABLE");
        [UiReasonId("Victory modal is not active")]
        public static readonly ReasonId VictoryNotActive = ReasonId.Of("UI_VICTORY_NOT_ACTIVE");
        [UiReasonId("Repair blocked during active wave phase")]
        public static readonly ReasonId RepairBlockedDuringWave = ReasonId.Of("UI_REPAIR_BLOCKED_DURING_WAVE");
        [UiReasonId("Backup policy requires Crisis act")]
        public static readonly ReasonId BackupPolicyLockedAct = ReasonId.Of("UI_BACKUP_POLICY_REQUIRES_CRISIS");
        [UiReasonId("Grid warfare operation window is closed")]
        public static readonly ReasonId GwWindowClosed = ReasonId.Of("UI_GW_WINDOW_CLOSED");
        [UiReasonId("Action requires Crisis act")]
        public static readonly ReasonId ActionRequiresCrisis = ReasonId.Of("UI_ACTION_REQUIRES_CRISIS");
        [UiReasonId("Action requires Exodus act")]
        public static readonly ReasonId ActionRequiresExodus = ReasonId.Of("UI_ACTION_REQUIRES_EXODUS");
        [UiReasonId("Action requires Adaptation act")]
        public static readonly ReasonId ActionRequiresAdaptation = ReasonId.Of("UI_ACTION_REQUIRES_ADAPTATION");
        [UiReasonId("Action requires Routine act")]
        public static readonly ReasonId ActionRequiresRoutine = ReasonId.Of("UI_ACTION_REQUIRES_ROUTINE");

        public static ReasonId ActLockedFor(Act required)
        {
            return required switch
            {
                Act.PreWar => PreWarLocked,
                Act.Crisis => ActionRequiresCrisis,
                Act.Exodus => ActionRequiresExodus,
                Act.Adaptation => ActionRequiresAdaptation,
                Act.Routine => ActionRequiresRoutine,
                _ => throw new System.ArgumentOutOfRangeException(
                    nameof(required),
                    required,
                    "Unknown scenario act.")
            };
        }
    }
}
