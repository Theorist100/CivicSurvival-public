using Colossal.Collections;
using Colossal.Mathematics;
using Game;
using Game.Buildings;
using Game.Common;
using Game.Net;
using Game.Objects;
using Game.Prefabs;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Components.Domain.Cognitive;
using CivicSurvival.Core.Components.Placement;
using CivicSurvival.Core.Components.Requests;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Events;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Interfaces.Domain.Economy;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.UI;
using CivicSurvival.Core.UI.DomainState;
using CivicSurvival.Core.Utils;
#pragma warning disable CIVIC182 // Phase-neutral budget helpers live with the City budget service implementation.
using CivicSurvival.Services.City;
#pragma warning restore CIVIC182

namespace CivicSurvival.Domains.Cognitive.Placement
{
    /// <summary>
    /// Cognitive consumer of the generic Core building-placement pipeline. Places the
    /// unique Propaganda Center HQ that unlocks the counter-propaganda layer + owns the broadcast-
    /// capacity budget. Budget-only (no credit path), one HQ per city (a second placement is
    /// rejected — growth is via Center upgrades, not copies). Stateless: resolves the current
    /// World per call, safe across world reloads.
    /// </summary>
    [HandlesRequestKind(RequestKind.PropagandaCenterPlacement)]
    public sealed class PropagandaCenterPlacementHandler : IBuildingPlacementHandler
    {
        private static readonly LogContext Log = new("CenterPlacement");

        // Fallback when the balance config is not yet loaded (mirrors the contract default).
        private const int FALLBACK_CENTER_COST = 150000;

        public BuildingPlacementKind Kind => BuildingPlacementKind.PropagandaCenter;
        public RequestKind RequestKind => RequestKind.PropagandaCenterPlacement;
        public string ResultBridgeKey => RequestResultBridge.PropagandaCenterPlacement;
        public ReasonId CancelReasonId => ReasonIds.CenterCancelled;

        public bool TryPrepareActivation(World world, PrefabBase prefab, Entity prefabEntity, byte mode, out ReasonId reasonId, out string message)
        {
            var em = world.EntityManager;
            if (!em.HasComponent<PropagandaCenterPrefabData>(prefabEntity))
            {
                reasonId = ReasonIds.CenterPlacementFailed;
                message = $"PropagandaCenterPrefabData marker missing: {prefab.name}";
                return false;
            }

            // A single HQ per city — reject a second placement before activating the tool.
            if (IsCenterAlreadyBuilt(world))
            {
                reasonId = ReasonIds.CenterAlreadyBuilt;
                message = "Propaganda Center already built — grow it via upgrades, not copies";
                return false;
            }

            int cost = ResolveCost();
            if (cost > 0 && !CityBudgetService.CanAffordWithPending(world, cost))
            {
                reasonId = ReasonIds.CenterBudgetFailed;
                message = $"Insufficient funds for Propaganda Center (need ${cost:N0})";
                return false;
            }

            reasonId = ReasonId.None;
            message = string.Empty;
            return true;
        }

        public bool TryResolvePlacement(
            World world,
            in BuildingPlacementResolveContext ctx,
            EntityCommandBuffer ecb,
            Entity intentEntity,
            out BuildingPlacementResolution resolution,
            out BuildingPlacementAbort abort)
        {
            resolution = default;
            abort = default;

            var em = world.EntityManager;
            Entity prefabEntity = ctx.PrefabEntity;
            Entity matchedEntity = ctx.MatchedBuilding;

            if (!em.HasComponent<PropagandaCenterPrefabData>(prefabEntity))
            {
                abort = new BuildingPlacementAbort(ReasonIds.CenterPlacementFailed,
                    $"Matched entity {matchedEntity.Index} but prefab {prefabEntity.Index} has no PropagandaCenterPrefabData", deleteGhost: true);
                return false;
            }

            if (!em.HasComponent<Transform>(matchedEntity))
            {
                abort = new BuildingPlacementAbort(ReasonIds.CenterBuildingLost,
                    $"Propaganda Center {matchedEntity.Index} has no Transform — destroying ghost building", deleteGhost: true);
                return false;
            }

            // A concurrent second placement can slip past TryPrepareActivation (the first has not
            // committed yet). Re-check at resolve so only one HQ ever exists.
            if (IsCenterAlreadyBuilt(world))
            {
                abort = new BuildingPlacementAbort(ReasonIds.CenterAlreadyBuilt,
                    "Propaganda Center already built — second placement rejected", deleteGhost: true);
                return false;
            }

            int cost = ResolveCost();
            bool requiresBudget = cost > 0;
            if (requiresBudget && !CityBudgetService.CanAffordWithPending(world, cost))
            {
                abort = new BuildingPlacementAbort(ReasonIds.CenterBudgetFailed,
                    $"Propaganda Center: insufficient funds (need ${cost:N0}), destroying ghost building", deleteGhost: true);
                return false;
            }

            // The Center is a runtime-marked static prop, so it bypasses the vanilla building
            // placement rotation guarantees — square the final transform up against the road
            // regardless of how the preview left it.
            AlignToNearestRoad(world, em, ecb, matchedEntity);

            Log.Info($"Propaganda Center placement detected: entity={matchedEntity.Index}, cost={cost}");
            resolution = new BuildingPlacementResolution(cost, requiresBudget, (byte)0, PlacementPurse.CityBudget);
            return true;
        }

        // Vanilla NetSide reach margin (metres) added on top of the lot half-extent — the same
        // "+16" ObjectToolSystem's snap uses for both its search bounds and its distance cutoff.
        private const float SNAP_REACH_MARGIN = 16f;
        // Zone grid cell size (metres) — the 8m step the cross-axis snap rounds to.
        private const float ZONE_CELL_SIZE = 8f;
        // quaternion dot ≈ 1 → already aligned (covers the preview-snap-worked case).
        private const float ALIGNED_DOT_EPSILON = 0.9999f;

        /// <summary>
        /// Square the just-placed Center up against the road the way a vanilla building would:
        /// find the best zone block with the SAME metric vanilla's <c>ZoneBlockIterator</c> uses
        /// (distance to the block's FRONT line — not to its centre, which mis-picks the block at
        /// junctions and on two-sided roads) and adopt the block's snap — position pulled to the
        /// roadside kerb, cross-axis rounded to the 8m cell grid, rotation
        /// <c>ToolUtils.CalculateRotation(block.m_Direction)</c>. Byte-for-byte the arithmetic of
        /// <c>ObjectToolSystem.ZoneBlockIterator.Iterate</c>, replayed at commit. The preview snap
        /// is not guaranteed for a runtime-marked static prop (NetSide sits in the tool's
        /// toggleable snap mask), so this commit-side alignment is the invariant: however the
        /// ghost sat, the built Center lands on the kerb facing the road. No block within reach →
        /// the player's placement stands.
        /// </summary>
        [CompletesDependency("AlignToNearestRoad: main-thread read of the zone-block quad tree at Center placement commit — one search per placement (a once-per-city event), mirroring the vanilla NetSide snap lookup")]
        private static void AlignToNearestRoad(World world, EntityManager em, EntityCommandBuffer ecb, Entity building)
        {
            // Building Transform is a render component: the read goes through the render-write
            // barrier ticket (BuildingTransform has zero out-of-band producers today, so the
            // Consume is free — the ticket is the routing contract, not a wait).
            var renderBarrier = ServiceRegistry.Instance.Require<IRenderWriteBarrier>();
            var ticket = renderBarrier.Consume(typeof(PropagandaCenterPlacementHandler), RenderWriteComponentMask.BuildingTransform);
            if (!BuildingDamageHelper.TryGetBuildingTransform(em, building, ticket, out var transform))
                return;

            // Lot size from the building's own prefab (the runtime-installed BuildingData) —
            // the same value the vanilla snap feeds its iterator.
            if (!em.HasComponent<PrefabRef>(building))
                return;
            Entity prefab = em.GetComponentData<PrefabRef>(building).m_Prefab;
            if (!em.HasComponent<BuildingData>(prefab))
                return;
            int2 lotSize = em.GetComponentData<BuildingData>(prefab).m_LotSize;

            var searchSystem = world.GetOrCreateSystemManaged<Game.Zones.SearchSystem>();
            var tree = searchSystem.GetSearchTree(readOnly: true, out var deps);
            deps.Complete();

            float reach = (float)lotSize.y * 4f + SNAP_REACH_MARGIN;
            var iterator = new NearestZoneBlockIterator
            {
                m_EntityManager = em,
                m_HitPosition = transform.m_Position,
                m_Direction = math.forward(transform.m_Rotation).xz,
                m_LotSize = lotSize,
                m_Bounds = new Bounds2(transform.m_Position.xz - reach, transform.m_Position.xz + reach),
                // Vanilla's starting threshold: cmin(lot)*4 + 16 — beyond it the block is "not at this road".
                m_BestDistance = (float)math.cmin(lotSize) * 4f + SNAP_REACH_MARGIN
            };
            tree.Iterate(ref iterator);

            if (!iterator.m_Found)
            {
                Log.Info($"Center {building.Index}: no zone block within reach — keeping player placement");
                return;
            }

            quaternion aligned = ToolUtils.CalculateRotation(iterator.m_BestDirection);
            bool rotationDone = math.abs(math.dot(transform.m_Rotation.value, aligned.value)) >= ALIGNED_DOT_EPSILON;
            bool positionDone = math.distancesq(transform.m_Position.xz, iterator.m_BestPosition.xz) < 0.01f;
            if (rotationDone && positionDone)
                return; // preview snap already landed it on the kerb

            transform.m_Rotation = aligned;
            transform.m_Position.xz = iterator.m_BestPosition.xz;
            ecb.SetComponent(building, transform);
            // Updated re-runs the vanilla object update chain (culling, search trees, attachments);
            // BatchesUpdated additionally forces the render batch to rebuild THIS frame — without it
            // the rotated Transform is live in data but the mesh keeps its old orientation until some
            // later batch refresh (the exact pair Move It stamps after every move/rotate).
            ecb.AddComponent<Updated>(building);
            ecb.AddComponent<BatchesUpdated>(building);
            Log.Info($"Center {building.Index}: snapped to kerb (block dir=({iterator.m_BestDirection.x:F2}, {iterator.m_BestDirection.y:F2}))");
        }

        /// <summary>
        /// Best zone block by the VANILLA metric — the commit-side twin of
        /// <c>ObjectToolSystem.ZoneBlockIterator.Iterate</c> (same front-line distance test, same
        /// kerb-position arithmetic, minus the preview-only owner filter and snap priority).
        /// Main-thread only: reads Block data through the EntityManager under the completed
        /// search-tree dependency above.
        /// </summary>
        private struct NearestZoneBlockIterator : INativeQuadTreeIterator<Entity, Bounds2>
        {
            public EntityManager m_EntityManager;
            public float3 m_HitPosition;
            public float2 m_Direction;
            public int2 m_LotSize;
            public Bounds2 m_Bounds;
            public float m_BestDistance;
            public float3 m_BestPosition;
            public float2 m_BestDirection;
            public bool m_Found;

            public bool Intersect(Bounds2 bounds) => MathUtils.Intersect(bounds, m_Bounds);

            public void Iterate(Bounds2 bounds, Entity blockEntity)
            {
                if (!MathUtils.Intersect(bounds, m_Bounds))
                    return;

                var block = m_EntityManager.GetComponentData<Game.Zones.Block>(blockEntity);

                // Distance from the building to the block's FRONT line (kerb edge), with the
                // probe segment widened along the building's forward axis for deep lots —
                // vanilla ZoneBlockIterator.Iterate verbatim.
                Quad2 corners = Game.Zones.ZoneUtils.CalculateCorners(block);
                var frontLine = new Line2.Segment(corners.a, corners.b);
                var probe = new Line2.Segment(m_HitPosition.xz, m_HitPosition.xz);
                float2 widen = m_Direction * (math.max(0f, m_LotSize.y - m_LotSize.x) * 4f);
                probe.a -= widen;
                probe.b += widen;
                float distance = MathUtils.Distance(frontLine, probe, out float2 t);
                // Distance is non-negative; exactly 0 = the segments intersect, and vanilla
                // breaks the tie by how centred the hit is on the block front.
                if (distance <= 0f)
                    distance -= 0.5f - math.abs(t.y - 0.5f);
                if (distance >= m_BestDistance)
                    return;

                m_BestDistance = distance;
                float2 fromBlock = m_HitPosition.xz - block.m_Position.xz;
                float2 left = MathUtils.Left(block.m_Direction);
                float blockDepth = (float)block.m_Size.y * 4f;
                float lotDepth = (float)m_LotSize.y * 4f;
                float along = math.dot(block.m_Direction, fromBlock);
                float across = math.dot(left, fromBlock);
                float parityOffset = math.select(0f, 0.5f, ((block.m_Size.x ^ m_LotSize.x) & 1) != 0);
                across -= (math.round(across / ZONE_CELL_SIZE - parityOffset) + parityOffset) * ZONE_CELL_SIZE;

                m_BestPosition = m_HitPosition;
                m_BestPosition.xz += block.m_Direction * (blockDepth - lotDepth - along);
                m_BestPosition.xz -= left * across;
                m_BestDirection = block.m_Direction;
                m_Found = true;
            }
        }

        // No credit path — this is never invoked (ReservedCreditKind stays 0), but the interface
        // requires it. Resolve to a benign success so no code path stalls if it is ever reached.
        public void ResolveCredit(World world, ref BuildingPlacementIntent intent)
        {
            intent.CreditResolved = true;
            intent.CreditSucceeded = true;
        }

        public void ResolveBudget(World world, ref BuildingPlacementIntent intent)
        {
            var wallet = ServiceRegistryFeatureExtensions.TryGetOrNullObject(NullShadowWalletService.Instance);
            var result = BudgetTransactionResolver.Deduct(
                world,
                wallet,
                intent.Cost,
                BudgetCategory.CognitiveOps,
                "CenterInstall");

            intent.BudgetResolved = true;
            intent.BudgetSucceeded = result.Succeeded;

            if (result.Succeeded)
                Log.Info($"Center budget resolved (success) for building {intent.Building.Index}:{intent.Building.Version}");
            else
                Log.Warn($"Center budget resolved (failed) for building {intent.Building.Index}:{intent.Building.Version}");
        }

        public void Commit(World world, EntityCommandBuffer ecb, Entity intentEntity, in BuildingPlacementIntent intent)
        {
            int paidBudget = intent.RequiresBudget ? intent.Cost : 0;

            var centerEntity = ecb.CreateEntity();
            ecb.AddComponent(centerEntity, new PropagandaCenterInstallation
            {
                Building = intent.Building,
                UpgradeTier = 0,
                PaidBudget = paidBudget
            });
            ecb.AddComponent<Simulate>(centerEntity);

            // SafePublish is null-tolerant by contract (extension on IEventBus?) — it owns the
            // "bus not registered yet" logging, so no null-conditional needed here.
            ServiceRegistry.TryGet<IEventBus>().SafePublish(
                new PropagandaCenterPlacedEvent(intent.Cost), nameof(PropagandaCenterPlacementHandler));
            Log.Info($"Propaganda Center confirmed: building={intent.Building.Index}, cost={intent.Cost} — cognitive-defence layer unlocked");
        }

        // Budget-only placement: no credit to return.
        public bool RefundCredit(World world, byte reservedCreditKind, int placementId) => false;

        public string BudgetRefundOperationKey(int placementId) => $"CenterPlacementRefund:{placementId}";

        public void QueueBudgetRefund(EntityCommandBuffer ecb, int cost, string operationKey)
        {
            _ = BudgetEmitter.TryQueueAddFunds(
                ecb,
                cost,
                BudgetSource.CenterInstallRefund,
                BudgetIncomeKind.Refund,
                operationKey,
                out _,
                BudgetResultMode.RetainResult);
        }

        // City-budget purse only: the refund always settles through the queued path.
        public bool TryRefundBudgetSync(World world, in BuildingPlacementIntent intent, out bool succeeded)
        {
            succeeded = false;
            return false;
        }

        public ReasonId MapTerminalReasonToReasonId(BuildingPlacementTerminalReason reason)
        {
            if (reason == BuildingPlacementTerminalReason.BudgetFailed)
                return ReasonIds.CenterBudgetFailed;
            if (reason == BuildingPlacementTerminalReason.BuildingMissingBeforeApply)
                return ReasonIds.CenterBuildingLost;
            if (reason == BuildingPlacementTerminalReason.DuplicateBuilding)
                return ReasonIds.CenterAlreadyBuilt;
            return ReasonIds.CenterPlacementFailed;
        }

        private static int ResolveCost()
        {
            var cog = BalanceConfig.Current?.Cognitive;
            return cog?.PropagandaCenterCost ?? FALLBACK_CENTER_COST;
        }

        // A Center exists when the derived state singleton reports IsBuilt, when a live installation
        // entity is present, or when a Center placement intent is still in flight. The third check is
        // what makes the single-HQ rule race-free: the installation created by Commit stays invisible
        // until the ModificationEnd barrier plays back, and the derived state rebuilds only in
        // GameSimulation (frozen in pause) — during that window the first two checks see nothing, so
        // a second placement could pass activation/resolve and pay a second time. The intent entity
        // covers exactly that window: it exists from the frame after detection until the same barrier
        // playback that materializes the installation, so at most one Center flow can hold the slot.
        private static bool IsCenterAlreadyBuilt(World world)
        {
            var em = world.EntityManager;
            var stateQuery = em.CreateEntityQuery(ComponentType.ReadOnly<PropagandaCenterState>());
            bool built = stateQuery.TryGetSingleton<PropagandaCenterState>(out var state) && state.IsBuilt;
            stateQuery.Dispose();
            if (built)
                return true;

            var installQuery = em.CreateEntityQuery(ComponentType.ReadOnly<PropagandaCenterInstallation>());
            bool hasInstall = !installQuery.IsEmptyIgnoreFilter;
            installQuery.Dispose();
            if (hasInstall)
                return true;

            return HasInFlightCenterIntent(em);
        }

        // An unapplied Center intent still owns the single-HQ slot — it will produce an installation
        // once payment resolves and the commit plays back (including the post-load reprocess of an
        // intent whose applied installation was lost to a pre-playback save). Terminally rejected
        // intents (ApplyResolved && !ApplySucceeded, alive only while their refund settles) never
        // will, so they release the slot immediately — otherwise a reject in pause would block
        // re-placement until the refund drains through GameSimulation after unpause.
        private static bool HasInFlightCenterIntent(EntityManager em)
        {
            var intentQuery = em.CreateEntityQuery(ComponentType.ReadOnly<BuildingPlacementIntent>());
            var intents = intentQuery.ToComponentDataArray<BuildingPlacementIntent>(Allocator.Temp);
            bool inFlight = false;
            for (int i = 0; i < intents.Length; i++)
            {
                var intent = intents[i];
                if (intent.Kind != BuildingPlacementKind.PropagandaCenter)
                    continue;
                if (intent.ApplyResolved && !intent.ApplySucceeded)
                    continue;
                inFlight = true;
                break;
            }
            intents.Dispose();
            intentQuery.Dispose();
            return inFlight;
        }
    }
}
