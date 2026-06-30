using System;
using Game;
using Game.Common;
using Game.Objects;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using CivicSurvival.Core.Components.Domain.AirDefense;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Components.Lifecycle;
using CivicSurvival.Core.Components.Requests;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Utils;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.UI;
using CivicSurvival.Core.UI.DomainState;

namespace CivicSurvival.Domains.AirDefense.Systems
{
    /// <summary>
    /// Detects newly placed AA objects (StaticObjectPrefab props — AA_40mm_Bofors /
    /// MIM104_SAM, not vanilla buildings) and emits AAPlacementIntent entities.
    ///
    /// DOES NOT create AirDefenseInstallation — that is AAPlacementCommitSystem's sole responsibility.
    ///
    /// Pattern (singleton-gated):
    /// 1. UI activates AA placement tool → creates AAPlacementPending singleton
    /// 2. Dual gate: RequireForUpdate(AAPlacementPending) AND RequireForUpdate(Created)
    /// 3. System wakes only during placement — matches Created entity by PrefabRef
    /// 4. Resolves prefab → creates intent entity → pause-safe payment/commit systems finish it.
    /// </summary>
    [ActIndependent]
    public partial class AAInstallationDetectorSystem : CivicSystemBase
    {
        private static readonly LogContext Log = new("AAInstallationDetectorSystem");

        private EntityQuery m_PendingQuery;
        private ComponentLookup<AirDefensePrefabData> m_PrefabDataLookup;
        private ComponentLookup<Transform> m_TransformLookup;
        private IRenderWriteBarrier m_RenderWriteBarrier = null!;
        private ModificationEndBarrier m_Barrier = null!;
        private ToolSystem m_ToolSystem = null!;
        private DefaultToolSystem m_DefaultToolSystem = null!;
#pragma warning disable CIVIC229 // System reference — not state, no reset needed
        private AirDefenseStateSystem m_StateSystem = null!;
#pragma warning restore CIVIC229
        [System.NonSerialized] private CivicDependencyWire m_DependencyWire = null!;

        /// <summary>
        /// Multi-tick guard: tracks which pending entity was already processed.
        /// RefRW writes can become stale between simulation ticks (chunk invalidation),
        /// but C# instance fields on the system are 100% reliable across ticks.
        /// </summary>
#pragma warning disable CIVIC241, CIVIC312 // Ephemeral multi-tick guard — intentionally resets on load (no pending entity survives load)
        [System.NonSerialized] private int m_LastProcessedPendingIndex;
        [EntityOnceGuard("Paired with m_LastProcessedPendingIndex; together form the (Index, Version) identity of the last processed pending AA installation so the same entity is not re-detected on subsequent ticks.")]
        [System.NonSerialized] private int m_LastProcessedPendingVersion;

        // Monotonic per-placement correlation id (always non-zero). NonSerialized is
        // safe because AllocatePlacementId rescans surviving intents before issuing a new id.
        [System.NonSerialized] private int m_NextPlacementId = 1;
#pragma warning restore CIVIC241, CIVIC312

        protected override void OnCreate()
        {
            base.OnCreate();

            m_Barrier = World.GetOrCreateSystemManaged<ModificationEndBarrier>();
            m_ToolSystem = World.GetOrCreateSystemManaged<ToolSystem>();
            m_DefaultToolSystem = World.GetOrCreateSystemManaged<DefaultToolSystem>();
            m_PrefabDataLookup = GetComponentLookup<AirDefensePrefabData>(true);
            m_TransformLookup = GetComponentLookup<Transform>(true);

            m_PendingQuery = GetEntityQuery(ComponentType.ReadOnly<AAPlacementPending>());
            m_DependencyWire = new CivicDependencyWire(nameof(AAInstallationDetectorSystem));

            // Runs while placement is pending. Cancellation and load cleanup are owned
            // by AAPlacementLifecycleSystem; this system only converts Created -> Intent.
            RequireForUpdate(m_PendingQuery);

            Log.Info("Created (singleton-gated, intent-only)");
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            m_RenderWriteBarrier ??= ServiceRegistry.Instance.Require<IRenderWriteBarrier>();
            m_StateSystem ??= m_DependencyWire.RequireWired(() => FeatureRegistry.Instance.Require<AirDefenseStateSystem>());
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
        }

        protected override void OnUpdateImpl()
        {
            // Both gates passed: singleton exists AND Created entities exist
            if (!m_PendingQuery.TryGetSingletonEntity<AAPlacementPending>(out var pendingEntity))
                return;

            // Multi-tick guard: ModificationEndBarrier plays back after all simulation ticks,
            // not between them. At 2x-3x speed, detector runs multiple ticks before the
            // deferred DestroyEntity takes effect — creating duplicate intents for same AA object.
            // System-level fields survive across ticks (RefRW can become stale after chunk moves).
            if (pendingEntity.Index == m_LastProcessedPendingIndex &&
                pendingEntity.Version == m_LastProcessedPendingVersion)
                return;

            if (!SystemAPI.TryGetSingleton<AAPlacementPending>(out var pending))
                return;

            // Reconstruct prefab Entity from stored Index/Version (Axiom 11)
            Entity pendingPrefab = new Entity { Index = pending.PrefabIndex, Version = pending.PrefabVersion };

            m_PrefabDataLookup.Update(this);
            m_TransformLookup.Update(this);

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
            // System-level fields are guaranteed visible in subsequent ticks.
            m_LastProcessedPendingIndex = pendingEntity.Index;
            m_LastProcessedPendingVersion = pendingEntity.Version;

            // Match found — create intent (single ECB for all paths)
            var ecb = m_Barrier.CreateCommandBuffer();

#pragma warning disable CIVIC022 // Failure paths fire once per rejected placement, not hot path
            if (!m_PrefabDataLookup.TryGetComponent(pendingPrefab, out var prefabData))
            {
                AbortPlacement(ecb, pending, pendingEntity, matchedEntity, deleteGhost: true,
                    reason: $"Matched entity {matchedEntity.Index} but prefab {pendingPrefab.Index} has no AirDefensePrefabData");
                return;
            }

            Log.Info($"AA placement detected: entity={matchedEntity.Index}, type={prefabData.Type}");

            // STEP 1: Check Transform FIRST (before any credit reservation — no rollback needed)
            var renderTicket = m_RenderWriteBarrier.Consume(GetType(), RenderWriteComponentMask.BuildingTransform);
            if (!TryGetBuildingTransform(renderTicket, matchedEntity, out var buildingTransform))
            {
                AbortPlacement(ecb, pending, pendingEntity, matchedEntity, deleteGhost: true,
                    reason: $"AA building {matchedEntity.Index} has no Transform — destroying ghost building");
                return;
            }
#pragma warning restore CIVIC022
            float3 position = buildingTransform.m_Position;

            // STEP 2: Reserve credit — READ-ONLY availability check. The detector
            // never decrements; it records ReservedCreditKind on the intent and the
            // pause-safe payment system calls the credit owner to stamp the outcome.
            // Availability subtracts outstanding unresolved claims, so back-to-back
            // placements before resolution cannot over-allocate.
            // The UI payload carries player intent, so paid and credit-backed cards never
            // collapse into the same prefab-only request.
            bool usedHeritage = false;
            if (pending.Mode == AAPlacementMode.Heritage)
            {
                usedHeritage = m_StateSystem.IsHeritageCreditAvailable();
                if (!usedHeritage)
                {
                    AbortPlacement(ecb, pending, pendingEntity, matchedEntity, deleteGhost: true,
                        reason: "AA installation aborted: heritage prefab requires an available heritage credit");
                    return;
                }
            }

            bool usedDonorCredit = false;
            if (pending.Mode == AAPlacementMode.DonorCredit)
            {
                if (prefabData.Type != AAType.PatriotSAM)
                {
                    AbortPlacement(ecb, pending, pendingEntity, matchedEntity, deleteGhost: true,
                        reason: "AA installation aborted: donor credit intent requires a Patriot prefab");
                    return;
                }

                usedDonorCredit = m_StateSystem.IsDonorPatriotCreditAvailable();
                if (!usedDonorCredit)
                {
                    AbortPlacement(ecb, pending, pendingEntity, matchedEntity, deleteGhost: true,
                        reason: "AA installation aborted: donor Patriot credit is no longer available");
                    return;
                }
            }

            // STEP 3: Check affordability (if not free). Payment happens later in the
            // pause-safe AAPlacementPaymentSystem; this is only an early UX guard.
            bool requiresBudget = !usedHeritage && !usedDonorCredit && prefabData.Price > 0;
            if (requiresBudget)
            {
                if (!AirDefenseEligibility.CanPayAirDefenseBudget(prefabData.Price, World, out _))
                {
                    AbortPlacementInsufficientFunds(ecb, pending, pendingEntity, matchedEntity, prefabData.Price);
                    return;
                }
            }

            // STEP 4: Resolve intent payload (heritage overrides stats)
            var cfg = BalanceConfig.Current;
            var heritageP = AAParams.ForType(cfg, AAType.HeritageBofors);

            // City-scaled magazine: stamp once at placement off the city SIZE (built nameplate via
            // the snapshot, NOT live production — same "city size" the wave count uses, so the
            // magazine↔wave ratio stays coherent; a struck city does not get a shrunk magazine).
            // The wave already scales with the city; a flat magazine drifts, so scale it the same way.
#pragma warning disable CIVIC070 // nameplate changes only on build/destroy, a 1-frame lag cannot meaningfully shift a coarse, once-at-placement magazine scale
            int cityMW = SystemAPI.TryGetSingleton<PowerGridSingleton>(out var powerGrid)
                ? WaveContextGatherer.ResolveCitySizeMW(powerGrid.Production)
                : WaveContextGatherer.MIN_PRODUCTION_MW;
#pragma warning restore CIVIC070
            int scaledMaxAmmo = AAAmmoScaling.ScaleMaxAmmo(
                cfg, usedHeritage ? heritageP.MaxAmmo : prefabData.MaxAmmo, cityMW);

            var creditKind = AAPlacementCreditKind.None;
            if (usedHeritage) creditKind = AAPlacementCreditKind.Heritage;
            else if (usedDonorCredit) creditKind = AAPlacementCreditKind.DonorPatriot;

            int placementId = AllocatePlacementId();

            var intent = new AAPlacementIntent
            {
                Building = BuildingRef.FromEntity(matchedEntity),
                PlacementId = placementId,
                ResolvedType = usedHeritage ? AAType.HeritageBofors : prefabData.Type,
                Range = usedHeritage ? heritageP.Range : prefabData.Range,
                InterceptChanceShahed = usedHeritage ? heritageP.InterceptChanceShahed : prefabData.InterceptChanceShahed,
                InterceptChanceBallistic = usedHeritage ? heritageP.InterceptChanceBallistic : prefabData.InterceptChanceBallistic,
                MaxAmmo = scaledMaxAmmo,
                CooldownDuration = usedHeritage ? heritageP.CooldownDuration : prefabData.CooldownDuration,
                CrewRequired = usedHeritage ? heritageP.CrewRequired : prefabData.CrewRequired,
                Cost = requiresBudget ? prefabData.Price : 0,
                RequiresBudget = requiresBudget,
                ReservedCreditKind = creditKind,
                RequestId = pending.RequestId
            };

            // STEP 5: Create intent entity
            var intentEntity = ecb.CreateEntity();
            ecb.AddComponent(intentEntity, intent);
            Log.Info($"Intent created: building={matchedEntity.Index}, type={intent.ResolvedType}, credit={creditKind}, requiresBudget={requiresBudget}, pos={position}");

            // STEP 6: Deferred destroy cleans up the entity after barrier plays back.
            // (m_LastProcessedPending* guard set above — prevents re-entry on all paths.)
            ecb.DestroyEntity(pendingEntity);

            // Single-placement: deactivate tool after each successful intent creation
            if (m_DefaultToolSystem != null)
                m_ToolSystem.activeTool = m_DefaultToolSystem;

            m_Barrier.AddJobHandleForProducer(Dependency);
        }

        private int AllocatePlacementId()
        {
            int maxExisting = 0;

            foreach (var intent in SystemAPI.Query<RefRO<AAPlacementIntent>>())
                maxExisting = Math.Max(maxExisting, intent.ValueRO.PlacementId);

            if (maxExisting == int.MaxValue)
            {
                Log.Error("AA placement id space exhausted; using saturated id");
                return int.MaxValue;
            }

            if (m_NextPlacementId <= maxExisting)
                m_NextPlacementId = maxExisting + 1;

            if (m_NextPlacementId <= 0)
                m_NextPlacementId = 1;

            if (m_NextPlacementId == int.MaxValue)
            {
                Log.Error("AA placement id space exhausted; using final id");
                return m_NextPlacementId;
            }

            return m_NextPlacementId++;
        }

        private bool TryGetBuildingTransform(RenderWriteTicket renderTicket, Entity entity, out Transform transform)
        {
            EnsureRenderTicket(renderTicket, RenderWriteComponentMask.BuildingTransform);
            return m_TransformLookup.TryGetComponent(entity, out transform);
        }

        private static void EnsureRenderTicket(RenderWriteTicket renderTicket, RenderWriteComponentMask requiredMask)
        {
            if (!renderTicket.Covers(requiredMask))
                throw new InvalidOperationException($"Render write ticket does not cover {requiredMask}");
        }

        /// <summary>
        /// S002: Unified abort path for failed placements.
        /// Always destroys the pending singleton, optionally marks the ghost AA object Deleted,
        /// and ALWAYS deactivates the prefab tool — otherwise subsequent clicks create
        /// untracked AA objects (no AAPlacementPending → detector skips them).
        /// </summary>
        private void AbortPlacement(EntityCommandBuffer ecb, AAPlacementPending pending, Entity pendingEntity, Entity matchedEntity, bool deleteGhost, string reason)
        {
            Log.Warn(reason);

            if (deleteGhost)
                ecb.AddComponent<Deleted>(matchedEntity);

            EmitPlacementFailed(ecb, pending.RequestId, MapDetectorRejectReason(reason));
            ecb.DestroyEntity(pendingEntity);

            if (m_DefaultToolSystem != null)
                m_ToolSystem.activeTool = m_DefaultToolSystem;

            m_Barrier.AddJobHandleForProducer(Dependency);
        }

        private void AbortPlacementInsufficientFunds(EntityCommandBuffer ecb, AAPlacementPending pending, Entity pendingEntity, Entity matchedEntity, int price)
        {
            AbortPlacement(
                ecb,
                pending,
                pendingEntity,
                matchedEntity,
                deleteGhost: true,
                reason: $"AA installation: insufficient funds (need ${price:N0}), destroying ghost building");
        }

        private static void EmitPlacementFailed(EntityCommandBuffer ecb, int requestId, ReasonId reasonId)
        {
            if (requestId == 0)
                return;

            var resultEntity = RequestResultEmitter.Emit(
                ecb,
                requestId,
                RequestKind.AirDefensePlacement,
                RequestStatus.Failed,
                reasonId,
                UnityEngine.Time.realtimeSinceStartupAsDouble);
            ecb.AddComponent<Reported>(resultEntity);
            RequestResultBridge.PublishTerminalForBegun(
                RequestResultBridge.AirDefensePlacement,
                requestId,
                RequestStatus.Failed,
                reasonId.ToString());
        }

        private static ReasonId MapDetectorRejectReason(string raw)
        {
            if (raw.Contains("insufficient funds", StringComparison.OrdinalIgnoreCase))
                return ReasonIds.AaBudgetFailed;
            if (raw.Contains("credit", StringComparison.OrdinalIgnoreCase))
                return ReasonIds.AaPlacementFailed;
            if (raw.Contains("Transform", StringComparison.OrdinalIgnoreCase))
                return ReasonIds.AaBuildingLost;
            return ReasonIds.AaPlacementFailed;
        }
    }
}
