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
using CivicSurvival.Core.Interfaces.Domain.Economy;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.Utils;
using CivicSurvival.Core.Attributes;
#pragma warning disable CIVIC182 // Phase-neutral budget mutation helper lives with City budget service implementation.
using CivicSurvival.Services.City;
#pragma warning restore CIVIC182
using CivicSurvival.Domains.AirDefense.Logic;
using CivicSurvival.Core.Systems.Scheduling;

namespace CivicSurvival.Domains.AirDefense.Systems
{
    /// <summary>
    /// Sole owner of EmergencyResupplyRequest.
    /// S24-A2 FIX: Separated from AirDefenseActionRequest (now sole-owned by SpotterCommandIngressSystem).
    ///
    /// Pause-safe (AXIOM 14): registered in ModificationEnd and pays synchronously
    /// through BudgetTransactionResolver; ammo is applied to the installations in the
    /// same tick (same clamp semantics as AARequestProcessorSystem), so the former
    /// retained batch/line/refund machinery is not involved. The automatic
    /// trickle/auto-refill flow keeps its retained batch pipeline in GameSimulation
    /// (AAAmmoSystem → AAResupplyPipelineSystem) — this route no longer touches it.
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
        private ModificationEndBarrier m_ModificationEndBarrier = null!;
        private IShadowWalletService m_WalletService = NullShadowWalletService.Instance;
        private ComponentLookup<Deleted> m_DeletedLookup;
        private ComponentLookup<Destroyed> m_DestroyedLookup;
        private ComponentLookup<Simulate> m_SimulateLookup;
        private ComponentLookup<AirDefenseInstallation> m_AALookup;
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

            m_ModificationEndBarrier = World.GetOrCreateSystemManaged<ModificationEndBarrier>();
            m_DeletedLookup = GetComponentLookup<Deleted>(true);
            m_DestroyedLookup = GetComponentLookup<Destroyed>(true);
            m_SimulateLookup = GetComponentLookup<Simulate>(true);
            m_AALookup = GetComponentLookup<AirDefenseInstallation>(false);
            m_StorageInfoLookup = GetEntityStorageInfoLookup();

            Log.Info("Created (EmergencyResupplyRequest — sole owner, synchronous payment, pause-safe)");
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            m_StateSystem ??= FeatureRegistry.Instance.Require<AirDefenseStateSystem>();
            m_WalletService = ServiceRegistryFeatureExtensions.TryGetOrNullObject(NullShadowWalletService.Instance);
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
                    if (!ecbCreated) { ecb = m_ModificationEndBarrier.CreateCommandBuffer(); ecbCreated = true; }
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
            m_SimulateLookup.Update(this);
            m_AALookup.Update(this);
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
                if (!ecbCreated) { ecb = m_ModificationEndBarrier.CreateCommandBuffer(); ecbCreated = true; }
                if (request.ValueRO.Kind != EmergencyResupplyKind.Emergency
                    && request.ValueRO.Kind != EmergencyResupplyKind.EmergencyGuns
                    && request.ValueRO.Kind != EmergencyResupplyKind.EmergencyRockets)
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

                bool applied = ProcessEmergencyResupply(ecb, meta.ValueRO, request.ValueRO.Kind, request.ValueRO.Target, queuedBatchThisUpdate, currentWave, out var failReason);
                if (applied)
                    queuedBatchThisUpdate = true;
                if (!applied)
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

        private bool ProcessEmergencyResupply(EntityCommandBuffer ecb, in RequestMeta meta, EmergencyResupplyKind kind, AAType target, bool queuedBatchThisUpdate, int currentWave, out string failReason)
        {
            bool gunsMode = kind == EmergencyResupplyKind.EmergencyGuns;
            bool rocketsMode = kind == EmergencyResupplyKind.EmergencyRockets;
            var cfg = BalanceConfig.Current;

            // Group labels for the log line; single-type keeps a generic label to avoid an
            // enum .ToString() (reflection alloc — CIVIC061). The type is already logged elsewhere.
            string label;
            if (gunsMode) label = "Guns";
            else if (rocketsMode) label = "Rockets";
            else label = "SingleType";

            // A missile type is eligible for the rockets batch iff it has a deficit AND is off its
            // per-wave cooldown — so the group price and refill only cover types that can restock now.
            bool RocketEligible(AAType t) => HasDeficitOfType(t) && !IsRocketOnWaveCooldown(t, currentWave);

            // Single-type pays that type's flat cost; a group sums the flat cost of every member that
            // is eligible right now (guns: any deficit; rockets: deficit AND off cooldown) — one price,
            // computed in the shared AAResupplyGroups helper that the UI gate also uses.
            int cost;
            if (gunsMode) cost = AAResupplyGroups.GunsResupplyCost(cfg, HasDeficitOfType);
            else if (rocketsMode) cost = AAResupplyGroups.RocketsResupplyCost(cfg, RocketEligible);
            else cost = AAParams.ForType(cfg, target).ResupplyCost;

            if (!CanEmergencyResupply(gunsMode, rocketsMode, RocketEligible, target, cost, currentWave, out failReason))
            {
                Log.Warn($"EmergencyResupply FAILED [{label}] - {failReason}");
                return false;
            }

            // The automatic trickle flow may have a retained batch in flight for the same
            // deficits — never double-charge the same rounds.
            if (queuedBatchThisUpdate || !m_PendingResupplyBatchQuery.IsEmpty)
            {
                failReason = ReasonIds.AirDefenseActionFailed;
                Log.Warn("EmergencyResupply deferred/failed - AA resupply batch is already pending");
                return false;
            }

            Func<AAType, bool> includeType;
            if (gunsMode) includeType = AAResupplyGroups.IsGunType;
            else if (rocketsMode) includeType = RocketEligible;
            else includeType = t => t == target;
            var lines = CollectEmergencyResupplyLines(includeType, out int totalRounds);
            if (lines.Count == 0 || totalRounds <= 0)
            {
                failReason = ReasonIds.AirDefenseActionFailed;
                Log.Warn("EmergencyResupply FAILED - no ammo deficit");
                return false;
            }

            // Synchronous payment (AXIOM 14): CanEmergencyResupply already verified
            // affordability; the resolver deducts on the main thread, so the restock
            // lands in the same tick even at selectedSpeed==0.
            var payment = BudgetTransactionResolver.Deduct(
                World,
                m_WalletService,
                cost,
                BudgetCategory.AirDefense,
                $"EmergencyAAResupply:{label}:{meta.RequestId}");
            if (!payment.Succeeded)
            {
                failReason = ReasonIds.AaInsufficientFunds;
                Log.Warn($"EmergencyResupply FAILED [{label}] - payment rejected (${cost:N0})");
                return false;
            }

            // Stamp the per-type cooldown BEFORE applying ammo: the stamp predicate reads
            // the live deficit, and the synchronous apply below erases it in this same
            // tick. Game-hour for every type (0-hour gate = no-op for guns) and the wave
            // number for Patriot's one-resupply-per-wave gate. Persisted by the owner.
            m_TimeProvider ??= GameTimeSystem.Instance;
            float chargedGameHour = m_TimeProvider != null ? m_TimeProvider.Current.TotalGameHours : 0f;
            if (gunsMode)
            {
                foreach (var gunType in AAResupplyGroups.GunTypes)
                {
                    if (HasDeficitOfType(gunType))
                        m_StateSystem.RecordResupply(gunType, chargedGameHour, currentWave);
                }
            }
            else if (rocketsMode)
            {
                foreach (var rocketType in AAResupplyGroups.RocketTypes)
                {
                    if (RocketEligible(rocketType))
                        m_StateSystem.RecordResupply(rocketType, chargedGameHour, currentWave);
                }
            }
            else
            {
                m_StateSystem.RecordResupply(target, chargedGameHour, currentWave);
            }

            // Apply ammo immediately — same clamp + UI-stats semantics as
            // AARequestProcessorSystem.ProcessResupplyRequests. The lines were collected
            // from live installations this same tick, so a dead-line refund path cannot
            // occur by construction.
            int acceptedRounds = 0;
            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                var aaEntity = new Entity { Index = line.EntityIndex, Version = line.EntityVersion };
                if (!AirDefenseLifecycle.TryGetActiveInstallation(
                        aaEntity,
                        m_AALookup,
                        m_StorageInfoLookup,
                        m_SimulateLookup,
                        m_DeletedLookup,
                        m_DestroyedLookup,
                        out var aa))
                    continue;

                int newAmmo = Math.Min(line.NewAmmo, aa.MaxAmmo);
                m_StateSystem.RecordUiStatsAmmoChanged(in aa, newAmmo);
                aa.CurrentAmmo = newAmmo;
#pragma warning disable CIVIC035 // AirDefenseLifecycle validated sidecar existence and live linked building.
                m_AALookup[aaEntity] = aa;
#pragma warning restore CIVIC035
                acceptedRounds += line.RoundsAdded;
            }

            RequestResultEmitter.EmitSuccess(ecb, meta, RequestKind.EmergencyResupply, SystemAPI.Time.ElapsedTime);
            EventBus?.SafePublish(new AAResupplyEvent(
                AAResupplyResult.Emergency,
                Rounds: acceptedRounds,
                Cost: cost
            ), "AirDefenseActionRequestSystem");

            Log.Info($"EmergencyResupply applied [{label}], cost: ${cost:N0}, rounds={acceptedRounds}");
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

        private bool CanEmergencyResupply(bool gunsMode, bool rocketsMode, Func<AAType, bool> rocketEligible, AAType target, int cost, int currentWave, out string failReason)
        {
            int liveInstallations = 0;
            bool hasAmmoDeficit = false;

            foreach (var aa in
                SystemAPI.Query<RefRO<AirDefenseInstallation>>()
                .WithAll<Simulate>()
                .WithNone<Deleted, Destroyed>())
            {
                var t = aa.ValueRO.Type;
                bool include;
                if (gunsMode) include = AAResupplyGroups.IsGunType(t);
                else if (rocketsMode) include = AAResupplyGroups.IsRocketType(t);
                else include = t == target;
                if (!include)
                    continue;

                if (!AirDefenseLifecycle.IsLiveLinkedBuilding(
                        aa.ValueRO.GetBuildingEntity(),
                        m_StorageInfoLookup,
                        m_DeletedLookup,
                        m_DestroyedLookup))
                    continue;

                liveInstallations++;
                // For the rockets group a deficit only counts if that type is off its wave cooldown —
                // a type that is deficit-but-gated must not keep the button enabled with a zero-cost
                // batch. Guns/single count any deficit (their cooldown is handled below/absent).
                if (aa.ValueRO.CurrentAmmo < aa.ValueRO.MaxAmmo && (!rocketsMode || rocketEligible(t)))
                    hasAmmoDeficit = true;
            }

            // Defense-in-depth: the same per-type cooldown the UI gate enforces, re-checked here so a
            // crafted/stale request cannot bypass it. Missile types are gated per-wave; the gun types
            // use the game-hour cooldown (0, so it never gates). Group restocks fold their cooldown
            // into the eligibility above (rockets) or have none (guns), so skip the single-type check.
            bool onCooldown = false;
            if (!gunsMode && !rocketsMode)
            {
                var typeParams = AAParams.ForType(BalanceConfig.Current, target);
                if (target.FiresInterceptorMissile())
                {
                    onCooldown = AirDefenseEligibility.IsResupplyWaveCooldownActive(
                        currentWave,
                        m_StateSystem.GetCreditsSnapshot().GetLastResupplyWave(target),
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

        /// <summary>True if a missile type is still inside its one-resupply-per-wave cooldown. The
        /// gun types return false (their ResupplyCooldownWaves is 0). Used to exclude a gated type
        /// from the rockets-group price and refill.</summary>
        private bool IsRocketOnWaveCooldown(AAType type, int currentWave)
        {
            var typeParams = AAParams.ForType(BalanceConfig.Current, type);
            return AirDefenseEligibility.IsResupplyWaveCooldownActive(
                currentWave,
                m_StateSystem.GetCreditsSnapshot().GetLastResupplyWave(type),
                typeParams.ResupplyCooldownWaves);
        }
    }
}
