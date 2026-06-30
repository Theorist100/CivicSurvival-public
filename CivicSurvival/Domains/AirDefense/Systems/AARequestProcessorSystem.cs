using Game;
using Game.Common;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Components.Lifecycle;
using CivicSurvival.Core.Components.Domain.AirDefense;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Components.Requests;
using CivicSurvival.Core.Components.Threats;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;
using CivicSurvival.Domains.AirDefense.Logic;
using System.Threading;
using CivicSurvival.Core.Systems.Scheduling;

namespace CivicSurvival.Domains.AirDefense.Systems
{
    /// <summary>
    /// Processes AA-related requests: Resupply and ForceCrewRelease.
    /// Single writer for AirDefenseInstallation via requests.
    ///
    /// Extracted from AirDefenseOrchestrator — zero dependency on targeting pipeline.
    /// Runs before Orchestrator to ensure request-driven AA state changes are visible
    /// to the same frame's targeting/fire control.
    /// </summary>
    [ActIndependent]
    public partial class AARequestProcessorSystem : CivicSystemBase
    {
        private static int s_EcbCommandCount;
        public static int EcbCommandCount => Volatile.Read(ref s_EcbCommandCount);
        public static void ResetCounters() => Interlocked.Exchange(ref s_EcbCommandCount, 0);
        private static void IncrementEcbCount() => Interlocked.Increment(ref s_EcbCommandCount);

        private static readonly LogContext Log = new("AARequestProcessor");

        private EntityQuery m_ResupplyRequestQuery;
        private EntityQuery m_ForceCrewReleaseQuery;

        private ComponentLookup<AirDefenseInstallation> m_AALookup;
        // S002b: filter out Deleted/Destroyed AA at apply time — pipeline pre-checks but the
        // AA can die between pipeline emit and processor consume (cross-barrier race).
        private ComponentLookup<Simulate> m_SimulateLookup;
        private ComponentLookup<Deleted> m_DeletedLookup;
        private ComponentLookup<Destroyed> m_DestroyedLookup;
        private EntityStorageInfoLookup m_StorageInfoLookup;
        private GameSimulationEndBarrier m_ECBSystem = null!;
#pragma warning disable CIVIC229 // System reference — UI stats cache is owned by AirDefenseStateSystem.
        private AirDefenseStateSystem m_StateSystem = null!;
#pragma warning restore CIVIC229

        private struct ForceReleaseEntry
        {
            public Entity Entity;
            public int NewCrewCount;
        }

        protected override void OnCreate()
        {
            base.OnCreate();

            m_ResupplyRequestQuery = GetEntityQuery(ComponentType.ReadWrite<ResupplyAARequest>());
            m_ForceCrewReleaseQuery = GetEntityQuery(ComponentType.ReadWrite<ForceCrewReleaseRequest>());

            m_AALookup = GetComponentLookup<AirDefenseInstallation>(false);
            m_SimulateLookup = GetComponentLookup<Simulate>(true);
            m_DeletedLookup = GetComponentLookup<Deleted>(true);
            m_DestroyedLookup = GetComponentLookup<Destroyed>(true);
            m_StorageInfoLookup = GetEntityStorageInfoLookup();
            m_ECBSystem = World.GetOrCreateSystemManaged<GameSimulationEndBarrier>();

            RequireAnyForUpdate(m_ResupplyRequestQuery, m_ForceCrewReleaseQuery);

            Log.Info("Created");
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            m_StateSystem ??= FeatureRegistry.Instance.Require<AirDefenseStateSystem>();
        }

        protected override void OnUpdateImpl()
        {
            bool hasResupplyRequests = !m_ResupplyRequestQuery.IsEmpty;
            bool hasForceCrewReleaseRequests = !m_ForceCrewReleaseQuery.IsEmpty;
            if (!hasResupplyRequests && !hasForceCrewReleaseRequests)
                return;

            // RW lookups only when we actually write
            m_AALookup.Update(this);
            m_SimulateLookup.Update(this);
            m_DeletedLookup.Update(this);
            m_DestroyedLookup.Update(this);
            m_StorageInfoLookup.Update(this);

#pragma warning disable CIVIC145 // Early return above guarantees at least one branch writes DestroyEntity
            var ecb = m_ECBSystem.CreateCommandBuffer();
#pragma warning restore CIVIC145

            ProcessResupplyRequests(ecb);

            using var releaseEntries = new NativeList<ForceReleaseEntry>(8, Allocator.Temp);
            CollectForceCrewReleaseRequests(ecb, releaseEntries);
            ApplyForceCrewReleaseRequests(releaseEntries);

            m_ECBSystem.AddJobHandleForProducer(Dependency);
        }

        private void ProcessResupplyRequests(EntityCommandBuffer ecb)
        {
            if (m_ResupplyRequestQuery.IsEmpty) return;

            foreach (var (requestRef, entity) in
                SystemAPI.Query<RefRW<ResupplyAARequest>>()
                .WithEntityAccess())
            {
                var request = requestRef.ValueRO;

                // Terminal guard (AAPlacementIntent doctrine): destroy below is a
                // deferred ECB command played back after ALL sim ticks. At 2x-3x the
                // request is still alive on later ticks of the same frame — skip it
                // once decided so the dead-AA refund branch cannot double-emit funds.
                if (request.Applied) continue;

                var aaEntity = new Entity { Index = request.AAEntityIndex, Version = request.AAEntityVersion };

                if (AirDefenseLifecycle.TryGetActiveInstallation(
                        aaEntity,
                        m_AALookup,
                        m_StorageInfoLookup,
                        m_SimulateLookup,
                        m_DeletedLookup,
                        m_DestroyedLookup,
                        out var aa))
                {
                    // Clamp to MaxAmmo — partial resupply could exceed max
                    int newAmmo = math.min(request.NewAmmo, aa.MaxAmmo);
                    m_StateSystem.RecordUiStatsAmmoChanged(in aa, newAmmo);
                    aa.CurrentAmmo = newAmmo;
#pragma warning disable CIVIC035 // AirDefenseLifecycle validated sidecar existence and live linked building.
                    m_AALookup[aaEntity] = aa;
#pragma warning restore CIVIC035
                }
                else if (request.RoundsAdded > 0 && request.RefundCost > 0)
                {
                    // S16b-8 FIX: AA destroyed between resupply payment and processing — refund
                    // Skip refund entirely when CostPerRound=0 (free resupply scenario)
                    // R4-S9-05 FIX: Use stored CostPerRound from purchase time (not current config)
                    long refund = request.RefundCost;
                    QueueRetainedRefundIfMissing(
                        ecb,
                        refund,
                        $"AAResupplyRefund:RequestDead:{request.AAEntityIndex}:{request.AAEntityVersion}:{request.NewAmmo}:{request.RoundsAdded}:{refund}");
                    Log.Warn($"Resupply refund: AA {request.AAEntityIndex} destroyed, refunding ${refund:N0} ({request.RoundsAdded} rounds)");
                }

                requestRef.ValueRW.Applied = true;
                ecb.DestroyEntity(entity);
                IncrementEcbCount();
            }
        }

        private void CollectForceCrewReleaseRequests(EntityCommandBuffer ecb, NativeList<ForceReleaseEntry> releaseEntries)
        {
            // S1-4 FIX: Process ForceCrewReleaseRequest (from MobilizationSystem.ForceReleaseExcess)
            // When population exodus shrinks manpower below used, MobilizationSystem trims allocations
            // and creates these requests so we zero CrewAssigned on affected AA entities.
            // W5#6: Entity-keyed map validates both Index+Version (prevents recycled entity misapply).
            if (m_ForceCrewReleaseQuery.IsEmpty) return;

            foreach (var (requestRef, entity) in
                SystemAPI.Query<RefRW<ForceCrewReleaseRequest>>()
                .WithEntityAccess())
            {
                var req = requestRef.ValueRO;
                if (req.Applied) continue;

                requestRef.ValueRW.Applied = true;
                var aaEntity = new Entity { Index = req.AAEntityIndex, Version = req.AAEntityVersion };
                AddOrReplaceReleaseEntry(releaseEntries, aaEntity, req.NewCrewCount);
                ecb.DestroyEntity(entity);
                IncrementEcbCount();
            }
        }

        private void QueueRetainedRefundIfMissing(EntityCommandBuffer ecb, long amount, string operationKey)
        {
            if (amount <= 0 || HasRefundRequest(operationKey))
                return;

            if (BudgetEmitter.TryQueueAddFunds(
                    ecb,
                    amount,
                    BudgetSource.ResupplyRefund,
                    BudgetIncomeKind.Refund,
                    operationKey,
                    out var refundEntity,
                    BudgetResultMode.RetainResult))
            {
                ecb.AddComponent(refundEntity, new AAResupplyRefundIntent
                {
                    Amount = amount,
                    OperationKey = new FixedString128Bytes(operationKey)
                });
                IncrementEcbCount();
            }
        }

        private bool HasRefundRequest(string operationKey)
        {
            foreach (var requestRef in SystemAPI.Query<RefRO<BudgetAddFundsRequest>>())
            {
                if (requestRef.ValueRO.OperationKey.ToString() == operationKey)
                    return true;
            }

            return false;
        }

        private void ApplyForceCrewReleaseRequests(NativeList<ForceReleaseEntry> releaseEntries)
        {
            if (releaseEntries.Length == 0) return;

            // Apply to matching AA entities — remove each matched key so unmatched = destroyed
            foreach (var (aa, aaEntity) in
                SystemAPI.Query<RefRW<AirDefenseInstallation>>()
                .WithEntityAccess())
            {
                if (!m_SimulateLookup.HasComponent(aaEntity)
                    || !m_SimulateLookup.IsComponentEnabled(aaEntity)
                    || m_DeletedLookup.HasComponent(aaEntity)
                    || m_DestroyedLookup.HasComponent(aaEntity))
                    continue;

                if (!AirDefenseLifecycle.IsLiveLinkedBuilding(
                        aa.ValueRO.GetBuildingEntity(),
                        m_StorageInfoLookup,
                        m_DeletedLookup,
                        m_DestroyedLookup))
                    continue;

                if (TryTakeReleaseEntry(releaseEntries, aaEntity, out int newCrew))
                {
                    if (aa.ValueRO.CrewAssigned != newCrew)
                    {
                        int oldCrew = aa.ValueRO.CrewAssigned;
                        aa.ValueRW.CrewAssigned = newCrew;
                        Log.Warn($"Force crew release: AA {aaEntity.Index} ({aa.ValueRO.Type}) crew {oldCrew} → {newCrew}");
                    }
                }
            }

            // Force-release requests for already-destroyed AAs — crew handled by AACrewReleaseSystem
            for (int i = 0; i < releaseEntries.Length; i++)
            {
                var entry = releaseEntries[i];
                Log.Warn($"Force crew release: AA {entry.Entity.Index} already destroyed — skipping (target crew={entry.NewCrewCount})");
            }
        }

        private static void AddOrReplaceReleaseEntry(NativeList<ForceReleaseEntry> entries, Entity entity, int newCrewCount)
        {
            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i].Entity != entity)
                    continue;

                entries[i] = new ForceReleaseEntry { Entity = entity, NewCrewCount = newCrewCount };
                return;
            }

            entries.Add(new ForceReleaseEntry { Entity = entity, NewCrewCount = newCrewCount });
        }

        private static bool TryTakeReleaseEntry(NativeList<ForceReleaseEntry> entries, Entity entity, out int newCrewCount)
        {
            for (int i = 0; i < entries.Length; i++)
            {
                var entry = entries[i];
                if (entry.Entity != entity)
                    continue;

                newCrewCount = entry.NewCrewCount;
                entries.RemoveAtSwapBack(i);
                return true;
            }

            newCrewCount = 0;
            return false;
        }
    }
}
