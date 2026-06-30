using System;
using Game.Simulation;
using CivicSurvival.Core.Components.Requests;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.UI.DomainState;
using Unity.Collections;
using Unity.Entities;

namespace CivicSurvival.Core.UI
{
    /// <summary>
    /// Typed result of a UI trigger handler. Handlers describe the outcome;
    /// TriggerDispatch owns all request-result publication.
    /// </summary>
    public readonly struct TriggerOutcome
    {
        internal enum OutcomeKind
        {
            Unknown = 0,
            Reject,
            RejectToastOnly,
            HandOff,
            SyncSuccess,
            Supersede,
            PendingCallback
        }

        internal readonly OutcomeKind Tag;
        internal readonly FixedString64Bytes ReasonIdFs;
        internal readonly string CanonicalEcho;
        internal readonly Entity HandOffEntity;
        internal readonly EntityCommandBuffer HandOffEcb;
        internal readonly EntityManager HandOffEntityManager;
        internal readonly bool HandOffUsesEntityManager;
        internal readonly double CreatedTime;
        internal readonly uint CreatedFrame;
        internal readonly string DiscriminatorKind;
        internal readonly string DiscriminatorValue;
        internal readonly Action<RequestToken>? SupersedeCallback;
        internal readonly Func<RequestToken, bool>? PendingCallback;

        private TriggerOutcome(
            OutcomeKind tag,
            FixedString64Bytes reasonId,
            string canonicalEcho,
            Entity handOffEntity,
            EntityCommandBuffer handOffEcb,
            EntityManager handOffEntityManager,
            bool handOffUsesEntityManager,
            double createdTime,
            uint createdFrame,
            string discriminatorKind,
            string discriminatorValue,
            Action<RequestToken>? supersedeCallback,
            Func<RequestToken, bool>? pendingCallback)
        {
            Tag = tag;
            ReasonIdFs = reasonId;
            CanonicalEcho = canonicalEcho ?? string.Empty;
            HandOffEntity = handOffEntity;
            HandOffEcb = handOffEcb;
            HandOffEntityManager = handOffEntityManager;
            HandOffUsesEntityManager = handOffUsesEntityManager;
            CreatedTime = createdTime;
            CreatedFrame = createdFrame;
            DiscriminatorKind = string.IsNullOrEmpty(discriminatorKind) ? "none" : discriminatorKind;
            DiscriminatorValue = discriminatorValue ?? string.Empty;
            SupersedeCallback = supersedeCallback;
            PendingCallback = pendingCallback;
        }

        public static TriggerOutcome Reject(ReasonId reasonId) =>
            new TriggerOutcome(
                OutcomeKind.Reject,
                reasonId.ToFixedString(),
                string.Empty,
                Entity.Null,
                default,
                default,
                false,
                0d,
                0u,
                "none",
                string.Empty,
                null,
                null);

        public static TriggerOutcome RejectRuntime(string reasonId) =>
            Reject(ReasonId.FromRuntime(reasonId));

        public static TriggerOutcome RejectToastOnly(ReasonId reasonId) =>
            new TriggerOutcome(
                OutcomeKind.RejectToastOnly,
                reasonId.ToFixedString(),
                string.Empty,
                Entity.Null,
                default,
                default,
                false,
                0d,
                0u,
                "none",
                string.Empty,
                null,
                null);

        public static TriggerOutcome RejectToastOnlyRuntime(string reasonId) =>
            RejectToastOnly(ReasonId.FromRuntime(reasonId));

        public static TriggerOutcome HandOffToEcs(
            EntityCommandBuffer ecb,
            Entity entity,
            double createdTime,
            uint createdFrame,
            string discriminatorKind = "none",
            string discriminatorValue = "") =>
            new TriggerOutcome(
                OutcomeKind.HandOff,
                default,
                string.Empty,
                entity,
                ecb,
                default,
                false,
                createdTime,
                createdFrame,
                discriminatorKind,
                discriminatorValue,
                null,
                null);

        public static TriggerOutcome HandOffToEcs(
            EntityManager entityManager,
            Entity entity,
            double createdTime,
            uint createdFrame,
            string discriminatorKind = "none",
            string discriminatorValue = "") =>
            new TriggerOutcome(
                OutcomeKind.HandOff,
                default,
                string.Empty,
                entity,
                default,
                entityManager,
                true,
                createdTime,
                createdFrame,
                discriminatorKind,
                discriminatorValue,
                null,
                null);

        public static uint CurrentSimulationFrame(World world)
        {
            var simulationSystem = world?.GetExistingSystemManaged<SimulationSystem>();
            return simulationSystem != null ? simulationSystem.frameIndex : 0u;
        }

        public static bool IsSimulationPaused(World world)
        {
            var simulationSystem = world?.GetExistingSystemManaged<SimulationSystem>();
            return simulationSystem != null && simulationSystem.selectedSpeed <= 0f;
        }

        public static TriggerOutcome SyncSuccess(
            string canonicalEcho = "",
            string discriminatorKind = "none",
            string discriminatorValue = "") =>
            new TriggerOutcome(
                OutcomeKind.SyncSuccess,
                default,
                canonicalEcho ?? string.Empty,
                Entity.Null,
                default,
                default,
                false,
                0d,
                0u,
                discriminatorKind,
                discriminatorValue,
                null,
                null);

        public static TriggerOutcome Supersede(ReasonId priorReasonId, Action<RequestToken> attachNewPending) =>
            new TriggerOutcome(
                OutcomeKind.Supersede,
                priorReasonId.ToFixedString(),
                string.Empty,
                Entity.Null,
                default,
                default,
                false,
                0d,
                0u,
                "none",
                string.Empty,
                attachNewPending,
                null);

        public static TriggerOutcome SupersedeRuntime(string priorReasonId, Action<RequestToken> attachNewPending) =>
            Supersede(ReasonId.FromRuntime(priorReasonId), attachNewPending);

        public static TriggerOutcome Pending(Func<RequestToken, bool> enqueueWithRequestToken, ReasonId failedReasonId) =>
            new TriggerOutcome(
                OutcomeKind.PendingCallback,
                failedReasonId.ToFixedString(),
                string.Empty,
                Entity.Null,
                default,
                default,
                false,
                0d,
                0u,
                "none",
                string.Empty,
                null,
                enqueueWithRequestToken);

        public static TriggerOutcome PendingRuntime(Func<RequestToken, bool> enqueueWithRequestToken, string failedReasonId) =>
            Pending(enqueueWithRequestToken, ReasonId.FromRuntime(failedReasonId));
    }

    /// <summary>
    /// Owns the request-result lifecycle for trigger handlers.
    /// </summary>
    public static class TriggerDispatch
    {
        public static void Invoke(
            string key,
            string triggerName,
            ActionKey actionKey,
            Func<ActionContext> contextFactory,
            Func<TriggerOutcome> handler)
        {
            try
            {
                var gate = ActionGate.Resolve(actionKey, contextFactory());
                if (!gate.CanRun)
                {
                    RequestResultBridge.RejectNew(key, gate.LockedReasonId);
                    return;
                }
            }
            catch (Exception ex)
            {
                Mod.Log.Error($"[TriggerDispatch] Trigger '{triggerName}' action gate threw\n{ex}");
                RequestResultBridge.RejectNew(key, ReasonIds.InternalError);
                return;
            }

            Invoke(key, triggerName, handler);
        }

        public static void Invoke(string key, string triggerName, Func<TriggerOutcome> handler)
        {
            TriggerOutcome outcome;
            try
            {
                outcome = handler();
            }
            catch (Exception ex)
            {
                Mod.Log.Error($"[TriggerDispatch] Trigger '{triggerName}' threw\n{ex}");
                RequestResultBridge.RejectNew(key, ReasonIds.InternalError);
                return;
            }

            switch (outcome.Tag)
            {
                case TriggerOutcome.OutcomeKind.Unknown:
                    RequestResultBridge.RejectNew(key, ReasonIds.InternalError);
                    return;

                case TriggerOutcome.OutcomeKind.Reject:
                    RequestResultBridge.RejectNew(key, outcome.ReasonIdFs.ToString());
                    return;

                case TriggerOutcome.OutcomeKind.RejectToastOnly:
                    RequestResultBridge.RejectToastOnly(key, outcome.ReasonIdFs.ToString());
                    return;

                case TriggerOutcome.OutcomeKind.SyncSuccess:
                {
                    var token = RequestResultBridge.Begin(
                        key,
                        outcome.DiscriminatorKind,
                        outcome.DiscriminatorValue);
                    RequestResultBridge.Complete(token, RequestStatus.Success, canonicalEcho: outcome.CanonicalEcho);
                    return;
                }

                case TriggerOutcome.OutcomeKind.HandOff:
                {
                    var token = RequestResultBridge.Begin(
                        key,
                        outcome.DiscriminatorKind,
                        outcome.DiscriminatorValue);
                    if (outcome.HandOffUsesEntityManager)
                        RequestMetaWriter.Add(outcome.HandOffEntityManager, outcome.HandOffEntity, token, outcome.CreatedTime, outcome.CreatedFrame);
                    else
                        RequestMetaWriter.Add(outcome.HandOffEcb, outcome.HandOffEntity, token, outcome.CreatedTime, outcome.CreatedFrame);
                    return;
                }

                case TriggerOutcome.OutcomeKind.Supersede:
                {
                    RequestResultBridge.FailCurrent(key, outcome.ReasonIdFs);
                    var token = RequestResultBridge.Begin(
                        key,
                        outcome.DiscriminatorKind,
                        outcome.DiscriminatorValue);
                    try
                    {
                        outcome.SupersedeCallback?.Invoke(token);
                    }
                    catch (Exception ex)
                    {
                        RequestResultBridge.Complete(token, RequestStatus.Failed, ReasonIds.InternalError);
                        Mod.Log.Error($"[TriggerDispatch] Trigger '{triggerName}' supersede callback threw\n{ex}");
                    }
                    return;
                }

                case TriggerOutcome.OutcomeKind.PendingCallback:
                {
                    var token = RequestResultBridge.Begin(
                        key,
                        outcome.DiscriminatorKind,
                        outcome.DiscriminatorValue);
                    bool queued;
                    try
                    {
                        queued = outcome.PendingCallback != null && outcome.PendingCallback(token);
                    }
                    catch (Exception ex)
                    {
                        queued = false;
                        Mod.Log.Error($"[TriggerDispatch] Trigger '{triggerName}' pending callback threw\n{ex}");
                    }

                    if (!queued)
                        RequestResultBridge.Complete(token, RequestStatus.Failed, outcome.ReasonIdFs);
                    return;
                }

                default:
                    RequestResultBridge.RejectNew(key, ReasonIds.InternalError);
                    return;
            }
        }

        public static void ResetAll() => RequestResultBridge.Reset();
    }
}
