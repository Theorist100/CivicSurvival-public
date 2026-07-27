using System;
using Game.Common;
using Game.Prefabs;
using Game.Tools;
using Unity.Entities;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Components.Lifecycle;
using CivicSurvival.Core.Components.Placement;
using CivicSurvival.Core.Components.Requests;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.UI;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Core.Systems.Placement
{
    /// <summary>
    /// Detects newly placed mod objects (StaticObjectPrefab props, not vanilla buildings)
    /// matching a pending placement, and emits a <see cref="BuildingPlacementIntent"/>.
    /// The prefab-match + intent lifecycle is generic; per-kind resolution (stats, credit,
    /// affordability, building-specific payload) is delegated to the registered handler.
    ///
    /// DOES NOT create the final mod entity — that is the commit system + handler's job.
    ///
    /// Pattern (singleton-gated):
    /// 1. UI activates the placement tool → creates BuildingPlacementPending singleton.
    /// 2. RequireForUpdate(BuildingPlacementPending) — the system wakes only during placement.
    /// 3. Matches a Created entity by PrefabRef → handler resolves → creates the intent entity.
    /// </summary>
    [ActIndependent]
    public partial class BuildingPlacementDetectorSystem : CivicSystemBase
    {
        private static readonly LogContext Log = new("BuildingPlacementDetector");

        private EntityQuery m_PendingQuery;
        private ModificationEndBarrier m_Barrier = null!;
        private ToolSystem m_ToolSystem = null!;
        private DefaultToolSystem m_DefaultToolSystem = null!;

        // Multi-tick guard: ModificationEndBarrier plays back after all simulation ticks, not
        // between them. At 2x-3x speed the detector runs multiple ticks before the deferred
        // DestroyEntity takes effect — system-level fields survive across ticks (RefRW can go stale).
#pragma warning disable CIVIC241, CIVIC312 // Ephemeral multi-tick guard — intentionally resets on load (no pending entity survives load)
        [System.NonSerialized] private int m_LastProcessedPendingIndex;
        [EntityOnceGuard("Paired with m_LastProcessedPendingIndex; together form the (Index, Version) identity of the last processed pending placement so the same entity is not re-detected on subsequent ticks.")]
        [System.NonSerialized] private int m_LastProcessedPendingVersion;

        // Monotonic per-placement correlation id (always non-zero). NonSerialized is safe because
        // AllocatePlacementId rescans surviving intents before issuing a new id.
        [System.NonSerialized] private int m_NextPlacementId = 1;
#pragma warning restore CIVIC241, CIVIC312

        protected override void OnCreate()
        {
            base.OnCreate();

            m_Barrier = World.GetOrCreateSystemManaged<ModificationEndBarrier>();
            m_ToolSystem = World.GetOrCreateSystemManaged<ToolSystem>();
            m_DefaultToolSystem = World.GetOrCreateSystemManaged<DefaultToolSystem>();

            m_PendingQuery = GetEntityQuery(ComponentType.ReadOnly<BuildingPlacementPending>());

            // Runs while placement is pending. Cancellation and load cleanup are owned by
            // BuildingPlacementLifecycleSystem; this system only converts Created -> Intent.
            RequireForUpdate(m_PendingQuery);

            Log.Info("Created (singleton-gated, intent-only)");
        }

        protected override void OnUpdateImpl()
        {
            if (!m_PendingQuery.TryGetSingletonEntity<BuildingPlacementPending>(out var pendingEntity))
                return;

            if (pendingEntity.Index == m_LastProcessedPendingIndex &&
                pendingEntity.Version == m_LastProcessedPendingVersion)
                return;

            if (!SystemAPI.TryGetSingleton<BuildingPlacementPending>(out var pending))
                return;

            // Reconstruct prefab Entity from stored Index/Version (Axiom 11).
            Entity pendingPrefab = new Entity { Index = pending.PrefabIndex, Version = pending.PrefabVersion };

            Entity matchedEntity = Entity.Null;
            foreach (var (prefabRef, entity) in
                SystemAPI.Query<RefRO<PrefabRef>>()
                .WithAll<Created>()
                .WithNone<Temp, Deleted>()
                .WithEntityAccess())
            {
                if (prefabRef.ValueRO.m_Prefab == pendingPrefab)
                {
                    matchedEntity = entity;
                    break;
                }
            }

            if (matchedEntity == Entity.Null)
                return;

            // Mark processed IMMEDIATELY — covers ALL paths below (happy + fail).
            m_LastProcessedPendingIndex = pendingEntity.Index;
            m_LastProcessedPendingVersion = pendingEntity.Version;

            var ecb = m_Barrier.CreateCommandBuffer();

#pragma warning disable CIVIC022 // Failure paths fire once per rejected placement, not hot path
            if (!BuildingPlacementHandlerRegistry.TryGet(pending.Kind, out var handler))
            {
                Log.Error($"No placement handler registered for kind {pending.Kind}; aborting placement");
                if (m_DefaultToolSystem != null)
                    m_ToolSystem.activeTool = m_DefaultToolSystem;
                ecb.DestroyEntity(pendingEntity);
                m_Barrier.AddJobHandleForProducer(Dependency);
                return;
            }

            // Speculatively create the intent entity so the handler can stamp its building-specific
            // payload onto it; undo (same ECB) if the handler aborts.
            var intentEntity = ecb.CreateEntity();
            var ctx = new BuildingPlacementResolveContext(pendingPrefab, matchedEntity, pending.Mode, pending.RequestId);

            if (!handler.TryResolvePlacement(World, in ctx, ecb, intentEntity, out var resolution, out var abort))
            {
                ecb.DestroyEntity(intentEntity);
                AbortPlacement(ecb, handler, pending, pendingEntity, matchedEntity, abort);
                return;
            }
#pragma warning restore CIVIC022

            int placementId = AllocatePlacementId();
            ecb.AddComponent(intentEntity, new BuildingPlacementIntent
            {
                Building = BuildingRef.FromEntity(matchedEntity),
                Kind = pending.Kind,
                Mode = pending.Mode,
                Cost = resolution.Cost,
                RequiresBudget = resolution.RequiresBudget,
                ReservedCreditKind = resolution.ReservedCreditKind,
                PayingPurse = (byte)resolution.PayingPurse,
                PlacementId = placementId,
                RequestId = pending.RequestId
            });

            Log.Info($"Intent created: kind={pending.Kind}, building={matchedEntity.Index}, credit={resolution.ReservedCreditKind}, requiresBudget={resolution.RequiresBudget}, purse={resolution.PayingPurse}");

            ecb.DestroyEntity(pendingEntity);

            // Single-placement: deactivate the tool after each successful intent creation.
            if (m_DefaultToolSystem != null)
                m_ToolSystem.activeTool = m_DefaultToolSystem;

            m_Barrier.AddJobHandleForProducer(Dependency);
        }

        private int AllocatePlacementId()
        {
            int maxExisting = 0;
            foreach (var intent in SystemAPI.Query<RefRO<BuildingPlacementIntent>>())
                maxExisting = Math.Max(maxExisting, intent.ValueRO.PlacementId);

            if (maxExisting == int.MaxValue)
            {
                Log.Error("Building placement id space exhausted; using saturated id");
                return int.MaxValue;
            }

            if (m_NextPlacementId <= maxExisting)
                m_NextPlacementId = maxExisting + 1;

            if (m_NextPlacementId <= 0)
                m_NextPlacementId = 1;

            if (m_NextPlacementId == int.MaxValue)
            {
                Log.Error("Building placement id space exhausted; using final id");
                return m_NextPlacementId;
            }

            return m_NextPlacementId++;
        }

        /// <summary>
        /// Unified abort path for failed placements. Always destroys the pending singleton,
        /// optionally marks the ghost object Deleted, and ALWAYS deactivates the prefab tool —
        /// otherwise subsequent clicks create untracked objects (no pending → detector skips them).
        /// </summary>
        private void AbortPlacement(
            EntityCommandBuffer ecb,
            IBuildingPlacementHandler handler,
            BuildingPlacementPending pending,
            Entity pendingEntity,
            Entity matchedEntity,
            in BuildingPlacementAbort abort)
        {
            Log.Warn(abort.Reason);

            if (abort.DeleteGhost)
                ecb.AddComponent<Deleted>(matchedEntity);

            EmitPlacementFailed(ecb, handler, pending.RequestId, abort.ReasonId);
            ecb.DestroyEntity(pendingEntity);

            if (m_DefaultToolSystem != null)
                m_ToolSystem.activeTool = m_DefaultToolSystem;

            m_Barrier.AddJobHandleForProducer(Dependency);
        }

        private static void EmitPlacementFailed(EntityCommandBuffer ecb, IBuildingPlacementHandler handler, int requestId, ReasonId reasonId)
        {
            if (requestId == 0)
                return;

            var resultEntity = RequestResultEmitter.Emit(
                ecb,
                requestId,
                handler.RequestKind,
                RequestStatus.Failed,
                reasonId,
                UnityEngine.Time.realtimeSinceStartupAsDouble);
            ecb.AddComponent<Reported>(resultEntity);
            RequestResultBridge.PublishTerminalForBegun(
                handler.ResultBridgeKey,
                requestId,
                RequestStatus.Failed,
                reasonId.ToString());
        }
    }
}
