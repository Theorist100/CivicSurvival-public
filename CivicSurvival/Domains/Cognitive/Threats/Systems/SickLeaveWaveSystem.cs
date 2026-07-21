using Game.Citizens;
using Game.Common;
using Unity.Entities;
using Unity.Mathematics;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Components.Domain.Cognitive;
using CivicSurvival.Core.Components.PsyImpact;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Interfaces.Domain.Mobilization;
using CivicSurvival.Core.Logic;
using CivicSurvival.Core.Systems;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Systems.Scheduling;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Domains.Cognitive.Threats.Systems
{
    /// <summary>
    /// FakeVideo's own vanilla lever — the fake sick-leave wave ("мужчины боятся мобилизации").
    /// A landed FakeVideo raid on a draft stratum (poor/middle) sends a share of male workers
    /// from swayed households home on fake sick leave: the citizen gets a vanilla
    /// <c>HealthProblem(Sick|NoHealthcare)</c> plus the mod <see cref="FakeSickLeave"/> marker.
    ///
    /// Vanilla then does all the visible work by itself: <c>WorkerSystem.GoToWorkJob</c>
    /// excludes citizens with <c>HealthProblem</c> (they physically stay home) and
    /// <c>WorkProviderSystem</c> counts them into the workplace's <c>SickEmployees</c>
    /// efficiency factor (visible in the building tooltip). <c>NoHealthcare</c> keeps the
    /// healthcare pipeline inert: <c>HealthProblemSystem</c> raises no ambulance request, no
    /// notification icon and no hospital trip for <c>Sick|NoHealthcare</c>, and
    /// <c>DeathCheckSystem</c> adds no death risk (the fake carries no health event).
    ///
    /// One wave per attack (the <see cref="PsySickWaveEmitted"/> latch on the attack entity —
    /// a separate component, so <c>PsyOpsLaunchSystem</c> stays the sole writer of
    /// <see cref="PsyOpsAttack"/>). The wave share scales with the raid intensity, is blunted
    /// by the current hero posture (Arestovych is FakeVideo's RPS counter) and amplified while
    /// conscription is active (the fear is of the draft). The reconcile pass releases citizens
    /// when the leave expires; if a REAL health event merged onto the fake while it lived
    /// (vanilla <c>AddHealthProblemSystem.MergeProblems</c> ORs the flags, so the real problem
    /// would inherit <c>NoHealthcare</c> and never be treated), only the <c>NoHealthcare</c>
    /// bit is dropped so real treatment resumes — the real event is recognisable by a non-null
    /// <c>HealthProblem.m_Event</c> (the fake always carries <c>Entity.Null</c>).
    ///
    /// Vanilla write surface: structural add of <c>HealthProblem</c>/<see cref="FakeSickLeave"/>
    /// on citizen entities via the GameSimulation barrier — citizens are renderless (no
    /// <c>MeshBatch</c>), the render-chunk-cache class does not apply (same class as the
    /// household <c>MovingAway</c> add in ExodusSystem).
    ///
    /// Act-gated to Crisis+ for FIRING; the reconcile pass keeps ticking regardless so expired
    /// leaves are always released (mirrors the launcher's keep-ticking-while-airborne rule).
    /// </summary>
    public partial class SickLeaveWaveSystem : ThrottledSystemBase, IActGatedSystem
    {
        private static readonly LogContext Log = new("SickLeaveWaveSystem");

        protected override int UpdateInterval => Engine.Timing.UPDATE_INTERVAL_1_SECOND;

        public Act MinActiveAct => Act.Crisis;
        public ActGateState GateState => m_Gate?.State ?? ActGateState.AwaitingActState;

        // Fallbacks mirror the contract defaults (config not yet loaded).
        private const float FALLBACK_BASE_SHARE = 0.25f;
        private const float FALLBACK_MAX_SHARE = 0.5f;
        private const float FALLBACK_DURATION_HOURS = 10f;
        private const float FALLBACK_INFECTION_THRESHOLD = 0.25f;
        private const float FALLBACK_BASE_VULNERABILITY = 0.2f;
        private const float FALLBACK_CONSCRIPTION_FEAR_MULT = 1.5f;
        private const float FALLBACK_HERO_COUNTER_STRONG = 0.7f;
        private const float FALLBACK_HERO_COUNTER_FLOOR = 0.15f;
        /// <summary>Floor for the infection saturation point — a zero/degenerate configured
        /// threshold must not turn the vulnerability division into NaN/Inf.</summary>
        private const float MIN_INFECTION_SATURATION = 1e-4f;

        private ActGateController m_Gate = null!;
        private EntityQuery m_CurrentActQuery;
        private EntityQuery m_PendingWaveQuery;
        private EntityQuery m_MarkerQuery;
        private GameSimulationEndBarrier m_Barrier = null!;
        private GameTimeSystem? m_TimeProvider;
        // Cross-domain conscription read via the Core service contract (not the
        // Mobilization-owned singleton — Axiom 5 / CIVIC211). Resolved once in
        // OnStartRunning (MobilizationSystem registers it in its OnCreate, which is
        // guaranteed to precede any system's first update); Require = fail-loud (CIVIC463).
        private IMobilizationManpowerReader m_ManpowerReader = null!;

        private BufferLookup<HouseholdCitizen> m_HouseholdCitizenLookup;
        private ComponentLookup<Citizen> m_CitizenLookup;
        private ComponentLookup<Worker> m_WorkerLookup;
        private ComponentLookup<HealthProblem> m_HealthProblemLookup;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_Barrier = World.GetOrCreateSystemManaged<GameSimulationEndBarrier>();
            m_CurrentActQuery = GetEntityQuery(ComponentType.ReadOnly<CurrentActSingleton>());
            m_PendingWaveQuery = GetEntityQuery(
                ComponentType.ReadOnly<PsyOpsAttack>(),
                ComponentType.Exclude<PsySickWaveEmitted>(),
                ComponentType.Exclude<Deleted>());
            m_MarkerQuery = GetEntityQuery(
                ComponentType.ReadOnly<FakeSickLeave>(),
                ComponentType.Exclude<Deleted>());

            m_HouseholdCitizenLookup = GetBufferLookup<HouseholdCitizen>(true);
            m_CitizenLookup = GetComponentLookup<Citizen>(true);
            m_WorkerLookup = GetComponentLookup<Worker>(true);
            m_HealthProblemLookup = GetComponentLookup<HealthProblem>(true);

            m_Gate = new ActGateController(
                isOpenFor: act => act >= Act.Crisis,
                onTransition: (old, next, isInitial) =>
                {
                    if (next == ActGateState.Active)
                        Log.Info($"[SickLeave] gate opened (Crisis+) — fake sick-leave waves armed (initial={isInitial})");
                });

            m_TimeProvider = GameTimeSystem.Instance;

            Log.Info("Created (FakeVideo fake sick-leave waves; Crisis-gated firing, ungated reconcile)");
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            m_ManpowerReader = ServiceRegistry.Instance.Require<IMobilizationManpowerReader>();
        }

        protected override bool ShouldSkipUpdate()
        {
            m_Gate.ReconcileFromSingleton(m_CurrentActQuery);

            // Keep ticking while any citizen is on fake sick leave — only this system releases
            // them; skipping with a closed gate would freeze the markers (and the vanilla
            // HealthProblem with them) forever.
            if (!m_MarkerQuery.IsEmptyIgnoreFilter)
                return false;

            return m_Gate.State != ActGateState.Active;
        }

        protected override void OnThrottledUpdate()
        {
            if (!TryGetGameHours(out float nowHours))
                return;

            m_HouseholdCitizenLookup.Update(this);
            m_CitizenLookup.Update(this);
            m_WorkerLookup.Update(this);
            m_HealthProblemLookup.Update(this);

            EntityCommandBuffer ecb = default;
            bool hasEcb = false;

            ReconcileExpiry(nowHours, ref ecb, ref hasEcb);

            if (m_Gate.State == ActGateState.Active && !m_PendingWaveQuery.IsEmptyIgnoreFilter)
                FireWaves(nowHours, ref ecb, ref hasEcb);

            // The barrier replays the buffer itself; main-thread only, no producer jobs.
        }

        /// <summary>
        /// Fire the one-shot sick-leave wave for every landed, not-yet-emitted FakeVideo raid.
        /// Non-eligible attacks (wrong type/stratum) are latched too, so they never rescan.
        /// </summary>
        [CompletesDependency("FireWaves: main-thread read of HouseholdPsyState sidecars + citizen lookups to select the wave's fake-sick citizens; runs only on the rare tick a FakeVideo raid lands (one-shot per attack)")]
        private void FireWaves(float nowHours, ref EntityCommandBuffer ecb, ref bool hasEcb)
        {
            var cwCfg = BalanceConfig.Current?.Cognitive;

            // Hero posture — FakeVideo's RPS counter blunts the wave live at fire time, with the
            // same Arestovych trust-debt suppression the launcher applies to the land snapshot.
            var hs = SystemAPI.TryGetSingleton<HeroDeploymentState>(out var heroState)
                ? heroState : HeroDeploymentState.Default;
            bool heroActive = hs.HeroStatus != HeroStatus.Inactive;
            bool arestovychSuppressed = hs.Archetype == HeroArchetype.Arestovych
                && hs.DebtCollapseEndHour > 0f && nowHours < hs.DebtCollapseEndHour;
            float heroFloor = cwCfg?.HeroCounterFloor ?? FALLBACK_HERO_COUNTER_FLOOR;
            float heroStrong = StratumDefense.EffectiveHeroCounterStrong(
                cwCfg?.HeroCounterStrong ?? FALLBACK_HERO_COUNTER_STRONG, heroFloor, arestovychSuppressed);
            float heroEff = StratumDefense.HeroTypeEffectiveness(
                hs.Archetype, PsyOpsAttackType.FakeVideo, heroActive, heroStrong, heroFloor);

            // The fear is of the draft: active conscription amplifies the wave.
            bool conscription = m_ManpowerReader.IsConscriptionActive;
            float fearMult = conscription
                ? (cwCfg?.SickLeaveConscriptionFearMult ?? FALLBACK_CONSCRIPTION_FEAR_MULT) : 1f;

            float baseShare = cwCfg?.SickLeaveBaseShare ?? FALLBACK_BASE_SHARE;
            float maxShare = cwCfg?.SickLeaveMaxShare ?? FALLBACK_MAX_SHARE;
            float durationHours = cwCfg?.SickLeaveDurationHours ?? FALLBACK_DURATION_HOURS;
            float infectionThreshold = cwCfg?.SickLeaveInfectionThreshold ?? FALLBACK_INFECTION_THRESHOLD;
            float baseVulnerability = math.saturate(cwCfg?.SickLeaveBaseVulnerability ?? FALLBACK_BASE_VULNERABILITY);

            foreach (var (atkRef, attackEntity) in
                SystemAPI.Query<RefRO<PsyOpsAttack>>()
                    .WithNone<PsySickWaveEmitted>()
                    .WithEntityAccess())
            {
                ref readonly var atk = ref atkRef.ValueRO;

                // Fire only after landing; an in-flight attack waits (no latch yet).
                if (atk.Phase != PsyOpsAttackPhase.Landed)
                    continue;

                EnsureEcb(ref ecb, ref hasEcb);
                // Latch first, unconditionally: whatever this attack is, it is scanned once.
                ecb.AddComponent<PsySickWaveEmitted>(attackEntity);

                // Only FakeVideo on a draft stratum carries a wave (Wealthy harm routes to
                // Monaco/bank-run; Rumor owns the panic runs; Propaganda the dodger drain).
                if (atk.Type != PsyOpsAttackType.FakeVideo
                    || (atk.Target != SocialStratum.Poor && atk.Target != SocialStratum.Middle))
                {
                    // Say WHY there is no wave — a silent latch reads as "the feature is broken"
                    // when a dev-panel FakeVideo happens to roll a Wealthy target (Axiom 1).
                    if (atk.Type == PsyOpsAttackType.FakeVideo)
                        Log.Info($"[SickLeave] wave skipped target={atk.Target} wave={atk.Wave} (not a draft stratum)");
                    continue;
                }

                float share = math.clamp(atk.Intensity * baseShare * (1f - heroEff) * fearMult, 0f, maxShare);
                if (share <= 0f)
                {
                    Log.Info($"[SickLeave] wave blunted to zero target={atk.Target} wave={atk.Wave} heroEff={heroEff:F2}");
                    continue;
                }

                int marked = MarkStratumWorkers(atk.Target, atk.Seed, share, infectionThreshold,
                    baseVulnerability, nowHours + durationHours, ref ecb, ref hasEcb);

                Log.Info($"[SickLeave] wave fired target={atk.Target} wave={atk.Wave} intensity={atk.Intensity:F2} " +
                         $"share={share:F2} heroEff={heroEff:F2} conscription={(conscription ? 1 : 0)} marked={marked} " +
                         $"durationH={durationHours:F1}");
            }
        }

        /// <summary>
        /// Mark male workers of the target stratum with the fake sickness. Per-citizen chance =
        /// <c>share × (baseVuln + (1−baseVuln) × saturate(infection / SickLeaveInfectionThreshold))</c>:
        /// the FEAR FLOOR (<c>baseVuln</c>) makes even a loyal household panic at a shock video
        /// (fear does not require enemy sway), and the household's infection scales the rest
        /// CONTINUOUSLY up to the full share at the saturation point. Neither a hard infection
        /// gate (fired wave 1 into `marked=0` at real avg infection 0.005) nor a pure
        /// infection-proportional chance (blunted wave 1 to 0.3% — 76 picks on a 172k city)
        /// survived live measurement. Deterministic per (attack seed × citizen): a reload
        /// replays the same picks.
        /// </summary>
        private int MarkStratumWorkers(
            SocialStratum target, uint attackSeed, float share, float infectionThreshold,
            float baseVulnerability, float expiryHours, ref EntityCommandBuffer ecb, ref bool hasEcb)
        {
            int marked = 0;
            float invSaturation = 1f / math.max(infectionThreshold, MIN_INFECTION_SATURATION);

            foreach (var (psyRef, linkRef) in SystemAPI.Query<RefRO<HouseholdPsyState>, RefRO<PsyHouseholdLink>>())
            {
                ref readonly var psy = ref psyRef.ValueRO;
                if (psy.CachedStratum != target)
                    continue;
                float householdShare = share * (baseVulnerability
                    + (1f - baseVulnerability) * math.saturate(psy.InfectionLevel * invSaturation));

                Entity household = linkRef.ValueRO.GetHouseholdEntity();
                if (!m_HouseholdCitizenLookup.TryGetBuffer(household, out var members))
                    continue;

                for (int i = 0; i < members.Length; i++)
                {
                    Entity citizen = members[i].m_Citizen;
                    if (!m_CitizenLookup.TryGetComponent(citizen, out var citizenData))
                        continue;
                    // Male workers only — the draft-age fantasy; children/students/pensioners
                    // carry no Worker component.
                    if ((citizenData.m_State & CitizenFlags.Male) == CitizenFlags.None)
                        continue;
                    if (!m_WorkerLookup.HasComponent(citizen))
                        continue;
                    // Never stack onto an existing (real or fake) health problem.
                    if (m_HealthProblemLookup.HasComponent(citizen))
                        continue;

                    var rng = Unity.Mathematics.Random.CreateFromIndex(attackSeed ^ (uint)citizen.Index);
                    if (rng.NextFloat() >= householdShare)
                        continue;

                    EnsureEcb(ref ecb, ref hasEcb);
                    ecb.AddComponent(citizen, new HealthProblem(Entity.Null,
                        HealthProblemFlags.Sick | HealthProblemFlags.NoHealthcare));
                    ecb.AddComponent(citizen, new FakeSickLeave { ExpiryHours = expiryHours });
                    marked++;
                }
            }

            return marked;
        }

        /// <summary>
        /// Release every citizen whose fake sick leave expired. A pure fake (null event) loses
        /// the whole <c>HealthProblem</c>; a merged REAL problem only loses the
        /// <c>NoHealthcare</c> bit so vanilla treatment resumes; a death keeps its component
        /// untouched (deathcare owns it) — only the marker goes.
        /// </summary>
        private void ReconcileExpiry(float nowHours, ref EntityCommandBuffer ecb, ref bool hasEcb)
        {
            int released = 0;

            foreach (var (leaveRef, citizen) in
                SystemAPI.Query<RefRO<FakeSickLeave>>().WithEntityAccess())
            {
                if (nowHours < leaveRef.ValueRO.ExpiryHours)
                    continue;

                EnsureEcb(ref ecb, ref hasEcb);

                if (m_HealthProblemLookup.TryGetComponent(citizen, out var hp))
                {
                    if ((hp.m_Flags & HealthProblemFlags.Dead) != HealthProblemFlags.None)
                    {
                        // Death overtook the fake — deathcare owns the component now.
                    }
                    else if (hp.m_Event == Entity.Null)
                    {
                        ecb.RemoveComponent<HealthProblem>(citizen);
                    }
                    else
                    {
                        hp.m_Flags &= ~HealthProblemFlags.NoHealthcare;
                        ecb.SetComponent(citizen, hp);
                    }
                }

                ecb.RemoveComponent<FakeSickLeave>(citizen);
                released++;
            }

            if (released > 0)
                Log.Info($"[SickLeave] released {released} citizens back to work (leave expired)");
        }

        private bool TryGetGameHours(out float hours)
        {
            m_TimeProvider ??= GameTimeSystem.Instance;
            if (m_TimeProvider == null)
            {
                hours = 0f;
                return false;
            }
            hours = m_TimeProvider.Current.TotalGameHours;
            return true;
        }

        private void EnsureEcb(ref EntityCommandBuffer ecb, ref bool hasEcb)
        {
            if (hasEcb)
                return;
            ecb = m_Barrier.CreateCommandBuffer();
            hasEcb = true;
        }
    }
}
