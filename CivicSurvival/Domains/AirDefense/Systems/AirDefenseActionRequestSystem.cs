using System;
using System.Collections.Generic;
using Game;
using Game.Common;
using Game.Simulation;
using Unity.Entities;
using CivicSurvival.Core.Components.Lifecycle;
using CivicSurvival.Core.Components.Domain.AirDefense;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Components.Threats;
using CivicSurvival.Core.Components.Requests;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Events;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Systems;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.UI;
using CivicSurvival.Core.UI.DomainState;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.Utils;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Domains.AirDefense.Logic;
using CivicSurvival.Core.Systems.Scheduling;

namespace CivicSurvival.Domains.AirDefense.Systems
{
    /// <summary>
    /// Sole owner of EmergencyResupplyRequest.
    /// S24-A2 FIX: Separated from AirDefenseActionRequest (now sole-owned by SpotterCommandIngressSystem).
    ///
    /// Uses RequireForUpdate - zero overhead when no requests pending.
    /// </summary>
    [ActIndependent]
    [HandlesRequestKind(RequestKind.EmergencyResupply)]
    [TransientConsumerReconcile(typeof(EmergencyResupplyRequest), ReconcileMode.ExplicitlyLossyAndSafe,
        Justification = "Transient UI command is consumed only into retained AAResupplyBatchIntent/BudgetDeductResult state; pre-consume load drops an unapplied action without ammo mutation.")]
    public partial class AirDefenseActionRequestSystem : CivicSystemBase
    {
        private static readonly LogContext Log = new("AirDefenseActionRequestSystem");

        private EntityQuery m_RequestQuery;
        private EntityQuery m_PendingResupplyBatchQuery;
        private GameSimulationEndBarrier m_GameSimulationEndBarrier = null!;
        private ComponentLookup<Deleted> m_DeletedLookup;
        private ComponentLookup<Destroyed> m_DestroyedLookup;
        private EntityStorageInfoLookup m_StorageInfoLookup;
        private readonly List<EmergencyResupplyLine> m_EmergencyResupplyLines = new(8);
#pragma warning disable CIVIC229 // System reference — per-type resupply cooldown state is owned by AirDefenseStateSystem.
        private AirDefenseStateSystem m_StateSystem = null!;
#pragma warning restore CIVIC229
        private GameTimeSystem? m_TimeProvider;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_RequestQuery = GetEntityQuery(
                ComponentType.ReadWrite<EmergencyResupplyRequest>()
            );
            m_PendingResupplyBatchQuery = GetEntityQuery(ComponentType.ReadOnly<AAResupplyBatchIntent>());

            RequireForUpdate(m_RequestQuery);

            m_GameSimulationEndBarrier = World.GetOrCreateSystemManaged<GameSimulationEndBarrier>();
            m_DeletedLookup = GetComponentLookup<Deleted>(true);
            m_DestroyedLookup = GetComponentLookup<Destroyed>(true);
            m_StorageInfoLookup = GetEntityStorageInfoLookup();

            Log.Info("Created (EmergencyResupplyRequest — sole owner)");
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            m_StateSystem ??= FeatureRegistry.Instance.Require<AirDefenseStateSystem>();
        }

        protected override void OnUpdateImpl()
        {
            EntityCommandBuffer ecb = default;
            bool ecbCreated = false;

            // R3-D-3: Defense-in-depth act guard. Emergency resupply is war-time only.
#pragma warning disable CIVIC070 // Act guard — CurrentActSingleton changes at act transitions only
            if (!SystemAPI.TryGetSingleton<CurrentActSingleton>(out var actSingleton) || actSingleton.CurrentAct < Act.Crisis)
            {
                foreach (var (_, meta, entity) in
                    SystemAPI.Query<RefRO<EmergencyResupplyRequest>, RefRO<RequestMeta>>()
                    .WithEntityAccess())
                {
                    if (!ecbCreated) { ecb = m_GameSimulationEndBarrier.CreateCommandBuffer(); ecbCreated = true; }
                    RequestResultEmitter.Emit(
                        ecb,
                        meta.ValueRO,
                        RequestKind.EmergencyResupply,
                        RequestStatus.Failed,
                        ReasonIds.AirDefensePreCrisis,
                        SystemAPI.Time.ElapsedTime);
                    ecb.DestroyEntity(entity);
                }
                return;
            }
#pragma warning restore CIVIC070

            m_DeletedLookup.Update(this);
            m_DestroyedLookup.Update(this);
            m_StorageInfoLookup.Update(this);

            // Current wave number drives the Patriot one-resupply-per-wave gate. 0 (no wave active)
            // when the singleton is absent — never blocks (lastResupplyWave stays the sentinel).
#pragma warning disable CIVIC070 // WaveStateSingleton read for the once-per-wave gate: a stale/absent wave number cannot mis-gate — IsResupplyWaveCooldownActive treats currentWave <= 0 (incl. this singleton-absent fallback) as "no active wave, not on cooldown", and RecordResupply never stamps a non-positive wave
            int currentWave = SystemAPI.TryGetSingleton<WaveStateSingleton>(out var waveState)
                ? waveState.WaveNumber
                : 0;
#pragma warning restore CIVIC070

            bool queuedBatchThisUpdate = false;
            foreach (var (request, meta, entity) in
                SystemAPI.Query<RefRO<EmergencyResupplyRequest>, RefRO<RequestMeta>>()
                .WithEntityAccess())
            {
                if (!ecbCreated) { ecb = m_GameSimulationEndBarrier.CreateCommandBuffer(); ecbCreated = true; }
                if (request.ValueRO.Kind != EmergencyResupplyKind.Emergency
                    && request.ValueRO.Kind != EmergencyResupplyKind.EmergencyGuns)
                {
                    RequestResultEmitter.Emit(
                        ecb,
                        meta.ValueRO,
                        RequestKind.EmergencyResupply,
                        RequestStatus.Failed,
                        ReasonIds.AirDefenseUnknownResupply,
                        SystemAPI.Time.ElapsedTime);
                    ecb.DestroyEntity(entity);
                    continue;
                }

                bool queued = ProcessEmergencyResupply(ecb, meta.ValueRO.RequestId, request.ValueRO.Kind, request.ValueRO.Target, queuedBatchThisUpdate, currentWave, out var failReason);
                if (queued)
                    queuedBatchThisUpdate = true;
                if (!queued)
                {
                    var resultReason = string.IsNullOrEmpty(failReason)
                        ? ReasonIds.AirDefenseActionFailed
                        : ReasonId.FromRuntime(failReason);
                    RequestResultEmitter.Emit(
                        ecb,
                        meta.ValueRO,
                        RequestKind.EmergencyResupply,
                        RequestStatus.Failed,
                        resultReason,
                        SystemAPI.Time.ElapsedTime);
                    RequestResultBridge.PublishTerminalForBegun(
                        RequestResultBridge.EmergencyResupply,
                        meta.ValueRO.RequestId,
                        RequestStatus.Failed,
                        resultReason.ToString());
                }

                ecb.DestroyEntity(entity);
            }
        }

        private bool ProcessEmergencyResupply(EntityCommandBuffer ecb, int requestId, EmergencyResupplyKind kind, AAType target, bool queuedBatchThisUpdate, int currentWave, out string failReason)
        {
            bool gunsMode = kind == EmergencyResupplyKind.EmergencyGuns;
            var cfg = BalanceConfig.Current;
            string label = gunsMode ? "Guns" : $"{target}";

            // Single-type pays that type's flat cost; the guns group sums the flat cost of every
            // gun type that currently has a deficit (full gun types cost nothing) — one price,
            // computed in the shared AAResupplyGroups helper that the UI gate also uses.
            int cost = gunsMode
                ? AAResupplyGroups.GunsResupplyCost(cfg, HasDeficitOfType)
                : AAParams.ForType(cfg, target).ResupplyCost;

            if (!CanEmergencyResupply(gunsMode, target, cost, currentWave, out failReason))
            {
                Log.Warn($"EmergencyResupply FAILED [{label}] - {failReason}");
                return false;
            }

            if (queuedBatchThisUpdate || !m_PendingResupplyBatchQuery.IsEmpty)
            {
                failReason = ReasonIds.AirDefenseActionFailed;
                Log.Warn("EmergencyResupply deferred/failed - AA resupply batch is already pending");
                return false;
            }

            Func<AAType, bool> includeType = gunsMode
                ? AAResupplyGroups.IsGunType
                : (Func<AAType, bool>)(t => t == target);
            var lines = CollectEmergencyResupplyLines(includeType, out int totalRounds);
            if (lines.Count == 0 || totalRounds <= 0)
            {
                failReason = ReasonIds.AirDefenseActionFailed;
                Log.Warn("EmergencyResupply FAILED - no ammo deficit");
                return false;
            }

            long batchId = AllocateBatchId();
            var batchEntity = ecb.CreateEntity();
            ecb.AddComponent(batchEntity, new AAResupplyBatchIntent
            {
                BatchId = batchId,
                TotalCost = cost,
                RequiresBudget = cost > 0,
                BudgetResolved = cost <= 0,
                BudgetSucceeded = cost <= 0,
                IsFullResupply = true,
                RequestedRounds = totalRounds,
                NeededRounds = totalRounds,
                IsEmergency = true,
                RequestId = requestId
            });

            if (!BudgetEmitter.TryQueueDeduct(
                    World,
                    ecb,
                    cost,
                    BudgetCategory.AirDefense,
                    BudgetPriority.PlayerAction,
                    $"EmergencyAAResupply:{label}:{batchId}",
                    out var budgetEntity,
                    BudgetResultMode.RetainResult))
            {
                ecb.SetComponent(batchEntity, new AAResupplyBatchIntent
                {
                    BatchId = batchId,
                    TotalCost = cost,
                    RequiresBudget = true,
                    BudgetResolved = true,
                    BudgetSucceeded = false,
                    IsFullResupply = true,
                    RequestedRounds = totalRounds,
                    NeededRounds = totalRounds,
                    IsEmergency = true,
                    RequestId = requestId
                });
            }
            else
            {
                ecb.AddComponent(budgetEntity, new AAResupplyBudgetLink
                {
                    BatchId = batchId
                });
            }

            long remainingCost = cost;
            int remainingRounds = totalRounds;
            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                long allocatedCost;
                if (remainingRounds <= line.RoundsAdded)
                {
                    allocatedCost = remainingCost;
                }
                else
                {
                    allocatedCost = ((long)cost * line.RoundsAdded) / totalRounds;
                    allocatedCost = Math.Min(allocatedCost, remainingCost);
                }

                remainingCost -= allocatedCost;
                remainingRounds -= line.RoundsAdded;

                var lineEntity = ecb.CreateEntity();
                ecb.AddComponent(lineEntity, new AAResupplyLineIntent
                {
                    BatchId = batchId,
                    AAEntityIndex = line.EntityIndex,
                    AAEntityVersion = line.EntityVersion,
                    NewAmmo = line.NewAmmo,
                    RoundsAdded = line.RoundsAdded,
                    CostPerRound = 0,
                    AllocatedCost = allocatedCost
                });
            }

            // Stamp the per-type cooldown the moment the batch is committed (cost is charged here
            // too): the game-hour for every type (0-hour gate = no-op for guns and now Patriot) and
            // the current wave number for Patriot's one-resupply-per-wave gate. Persisted by the owner.
            m_TimeProvider ??= GameTimeSystem.Instance;
            float queuedGameHour = m_TimeProvider != null ? m_TimeProvider.Current.TotalGameHours : 0f;
            if (gunsMode)
            {
                // Stamp every gun type that contributed (its deficit is still visible — the refill
                // ECB has not played back yet). Guns carry a zero cooldown so this never gates, but
                // it keeps the per-type last-resupply timestamp coherent for any future tuning.
                foreach (var gunType in AAResupplyGroups.GunTypes)
                {
                    if (HasDeficitOfType(gunType))
                        m_StateSystem.RecordResupply(gunType, queuedGameHour, currentWave);
                }
            }
            else
            {
                m_StateSystem.RecordResupply(target, queuedGameHour, currentWave);
            }

            Log.Info($"EmergencyResupply queued as retained batch {batchId} [{label}], cost: ${cost:N0}, rounds={totalRounds}");
            failReason = "";
            return true;
        }

        private List<EmergencyResupplyLine> CollectEmergencyResupplyLines(Func<AAType, bool> includeType, out int totalRounds)
        {
            var lines = m_EmergencyResupplyLines;
            lines.Clear();
            totalRounds = 0;

            foreach (var (aa, entity) in
                SystemAPI.Query<RefRO<AirDefenseInstallation>>()
                .WithAll<Simulate>()
                .WithNone<Deleted, Destroyed>()
                .WithEntityAccess())
            {
                if (!includeType(aa.ValueRO.Type))
                    continue;

                if (!AirDefenseLifecycle.IsLiveLinkedBuilding(
                        aa.ValueRO.GetBuildingEntity(),
                        m_StorageInfoLookup,
                        m_DeletedLookup,
                        m_DestroyedLookup))
                    continue;

                int rounds = Math.Max(0, aa.ValueRO.MaxAmmo - aa.ValueRO.CurrentAmmo);
                if (rounds == 0)
                    continue;

                totalRounds += rounds;
                lines.Add(new EmergencyResupplyLine(
                    entity.Index,
                    entity.Version,
                    aa.ValueRO.MaxAmmo,
                    rounds));
            }

            return lines;
        }

        private long AllocateBatchId()
        {
            long maxExisting = 0;
            foreach (var batch in SystemAPI.Query<RefRO<AAResupplyBatchIntent>>())
                maxExisting = Math.Max(maxExisting, batch.ValueRO.BatchId);

            return Math.Max(1, maxExisting + 1);
        }

        private readonly struct EmergencyResupplyLine
        {
            public readonly int EntityIndex;
            public readonly int EntityVersion;
            public readonly int NewAmmo;
            public readonly int RoundsAdded;

            public EmergencyResupplyLine(int entityIndex, int entityVersion, int newAmmo, int roundsAdded)
            {
                EntityIndex = entityIndex;
                EntityVersion = entityVersion;
                NewAmmo = newAmmo;
                RoundsAdded = roundsAdded;
            }
        }

        private bool CanEmergencyResupply(bool gunsMode, AAType target, int cost, int currentWave, out string failReason)
        {
            int liveInstallations = 0;
            bool hasAmmoDeficit = false;

            foreach (var aa in
                SystemAPI.Query<RefRO<AirDefenseInstallation>>()
                .WithAll<Simulate>()
                .WithNone<Deleted, Destroyed>())
            {
                bool include = gunsMode
                    ? AAResupplyGroups.IsGunType(aa.ValueRO.Type)
                    : aa.ValueRO.Type == target;
                if (!include)
                    continue;

                if (!AirDefenseLifecycle.IsLiveLinkedBuilding(
                        aa.ValueRO.GetBuildingEntity(),
                        m_StorageInfoLookup,
                        m_DeletedLookup,
                        m_DestroyedLookup))
                    continue;

                liveInstallations++;
                if (aa.ValueRO.CurrentAmmo < aa.ValueRO.MaxAmmo)
                    hasAmmoDeficit = true;
            }

            // Defense-in-depth: the same per-type cooldown the UI gate enforces, re-checked here so a
            // crafted/stale request cannot bypass it. Patriot is gated per-wave (one resupply per
            // wave); every other single-type path uses the game-hour cooldown (0 for the gun types,
            // so it never gates). The group restock (gunsMode) is never gated, so skip the check.
            bool onCooldown = false;
            if (!gunsMode)
            {
                var typeParams = AAParams.ForType(BalanceConfig.Current, target);
                if (target == AAType.PatriotSAM)
                {
                    onCooldown = AirDefenseEligibility.IsResupplyWaveCooldownActive(
                        currentWave,
                        m_StateSystem.GetCreditsSnapshot().LastResupplyWavePatriot,
                        typeParams.ResupplyCooldownWaves);
                }
                else
                {
                    m_TimeProvider ??= GameTimeSystem.Instance;
                    float currentGameHour = m_TimeProvider != null ? m_TimeProvider.Current.TotalGameHours : 0f;
                    float cooldownLeft = AirDefenseEligibility.ResupplyCooldownRemainingHours(
                        currentGameHour,
                        m_StateSystem.GetCreditsSnapshot().GetLastResupplyHour(target),
                        typeParams.ResupplyCooldownHours);
                    onCooldown = cooldownLeft > 0f;
                }
            }

            return AirDefenseEligibility.CanEmergencyResupply(
                liveInstallations > 0,
                hasAmmoDeficit,
                onCooldown,
                cost,
                World,
                out failReason);
        }

        /// <summary>
        /// True if at least one live installation of <paramref name="type"/> is below its magazine.
        /// Drives the guns-group cost sum (only deficit types are charged) and the per-type
        /// last-resupply stamp. Lookups are refreshed once per OnUpdateImpl before this runs.
        /// </summary>
        private bool HasDeficitOfType(AAType type)
        {
            foreach (var aa in
                SystemAPI.Query<RefRO<AirDefenseInstallation>>()
                .WithAll<Simulate>()
                .WithNone<Deleted, Destroyed>())
            {
                if (aa.ValueRO.Type != type)
                    continue;

                if (!AirDefenseLifecycle.IsLiveLinkedBuilding(
                        aa.ValueRO.GetBuildingEntity(),
                        m_StorageInfoLookup,
                        m_DeletedLookup,
                        m_DestroyedLookup))
                    continue;

                if (aa.ValueRO.CurrentAmmo < aa.ValueRO.MaxAmmo)
                    return true;
            }

            return false;
        }
    }
}
