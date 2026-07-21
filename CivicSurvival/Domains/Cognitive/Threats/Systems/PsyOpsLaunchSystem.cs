using System;
using System.Collections.Generic;
using Game.Common;
using Unity.Entities;
using Unity.Mathematics;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Components.Domain.Cognitive;
using CivicSurvival.Core.Components.Requests;
using CivicSurvival.Core.Components.Threats;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.Events;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Interfaces.Domain.Cognitive;
using CivicSurvival.Core.Interfaces.Domain.Debug;
using CivicSurvival.Core.Logic;
using CivicSurvival.Core.Systems;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Systems.Scheduling;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;
using CivicSurvival.Domains.Cognitive.Core.Systems;
using CivicSurvival.Domains.Cognitive.Ops.Countermeasures;
using CivicSurvival.Domains.Cognitive.Ops.Systems;

namespace CivicSurvival.Domains.Cognitive.Threats.Systems
{
    /// <summary>
    /// Discrete PSYOPS attack launcher. Spawns individual mental-attack objects
    /// (<see cref="PsyOpsAttack"/>) on top of the continuous reactive IPSO field
    /// (<see cref="IPSOCampaignSystem"/>, Tier 1) — the launcher never writes IPSO state.
    ///
    /// Cadence: an enemy mental "raid" is composed on entry to the PsyAttack wave phase — mid-wave,
    /// right after the kinetic strike (coordinated combined assault), not at wave-end. It is driven
    /// by <see cref="ThreatNarrativeEvent"/> (WavePhaseChanged → PsyAttack, read via EventBus and
    /// applied in the owning update, not the callback) and gated to escalating waves via the
    /// WaveExecutor's own <see cref="PsyOpsArming.WillFire"/> phase check. It reads <see cref="WaveStateSingleton"/>
    /// (Core) for the wave number/phase and the per-stratum read model
    /// (<see cref="CognitiveStatsState"/>) for infection-weighted targeting — it never
    /// imports the Scenario wave scheduler (Axiom 5) and never scans households (perf).
    ///
    /// Lifecycle (each attack, in game-time TotalGameHours): launched → in-flight (interception
    /// window open) → land (flagged + logged here) → expire (entity destroyed). A LANDED attack
    /// is a state, not an event: MentalHealthResolverSystem reads the live <see cref="PsyOpsAttack"/>
    /// entities each fire and integrates their pressure over its own dt (PsyOpsRaidPressure), so
    /// this launcher never pushes damage anywhere — it only advances the lifecycle. In-flight
    /// attacks persist as their own entities (vanilla serializes <see cref="PsyOpsAttack"/>); the
    /// launcher persists only its dispatcher RNG (partial — see PsyOpsLaunchSystem.Serialization.cs).
    ///
    /// Act-gated to Crisis+ (like <see cref="IPSOCampaignSystem"/>) — incoming enemy operations
    /// begin with the war. (The verified precedent for incoming enemy operations is
    /// act-gated, which is balance-correct.)
    /// </summary>
    public partial class PsyOpsLaunchSystem : ThrottledSystemBase, IActGatedSystem
#if DEBUG
        , IPsyOpsDebugMutator
#endif
    {
        private static readonly LogContext Log = new("PsyOpsLaunchSystem");

        protected override int UpdateInterval => Engine.Timing.UPDATE_INTERVAL_1_SECOND;

        public Act MinActiveAct => Act.Crisis;
        public ActGateState GateState => m_Gate?.State ?? ActGateState.AwaitingActState;

        // ── Cadence ──────────────────────────────────────────────────────────
        /// <summary>Raids start once physical waves reach this number. Single source of truth in
        /// Core (<see cref="PsyOpsArming.MIN_RAID_WAVE"/>) — the same value gates the PsyAttack phase
        /// in WaveExecutor, so the phase and the raid stay in lock-step.</summary>
        private const int MIN_RAID_WAVE = PsyOpsArming.MIN_RAID_WAVE;
        /// <summary>Raid size grows by one per this many waves past <see cref="MIN_RAID_WAVE"/>.</summary>
        private const int RAID_GROWTH_DIVISOR = 2;
        /// <summary>Upper bound on typed contacts in a single raid.</summary>
        private const int MAX_RAID_SIZE = 4;

        // ── Intensity / timing / per-type profiles & per-stratum vuln ────────
        // All wave-progression, per-type base intensities, landed durations, the rumor spread
        // window, the FakeVideo media-trust hit and the per-stratum vulnerability multipliers live
        // in balance.contract.yaml (Cognitive.*). The wave-progression curve mirrors
        // IPSOCampaignSystem (additive per-wave on a per-type base) — WaveScalingService
        // exposes threat counts/durations, not a 0-1 intensity scalar, so it is not used here.
        // Fallbacks (config not yet loaded) mirror the contract defaults.
        private const float FALLBACK_INTENSITY_PER_WAVE = 0.05f;
        private const float FALLBACK_MAX_INTENSITY = 0.80f;
        private const float FALLBACK_IN_FLIGHT_HOURS = 6f;
        private const float FALLBACK_PROPAGANDA_BASE = 0.18f;
        private const float FALLBACK_PROPAGANDA_DURATION = 4f;
        private const float FALLBACK_RUMOR_BASE = 0.30f;
        private const float FALLBACK_RUMOR_DURATION = 18f;
        private const float FALLBACK_RUMOR_SPREAD_WINDOW = 6f;
        /// <summary>City telecom factor above which a rumor rides the network (below = firebreak). Mirrors the contract default.</summary>
        private const float FALLBACK_TELECOM_RUMOR_SPREAD_THRESHOLD = 0.25f;
        private const float FALLBACK_FAKEVIDEO_BASE = 0.80f;
        private const float FALLBACK_FAKEVIDEO_DECAY = 3f;
        private const float FALLBACK_RAID_SECONDARY_INTENSITY_MULT = 0.5f;
        /// <summary>Float tolerance for comparing per-type coverage values when picking the least-covered (gap) type.</summary>
        private const float COVERAGE_GAP_EPSILON = 1e-4f;
        // Hero-RPS counter magnitudes for the land snapshot — mirror the balance contract defaults
        // (Cognitive.HeroCounterStrong / HeroCounterFloor; same fallbacks as CognitiveUISystem),
        // used only before the config loads.
        private const float FALLBACK_HERO_COUNTER_STRONG = 0.7f;
        private const float FALLBACK_HERO_COUNTER_FLOOR = 0.15f;
        // Per-stratum vuln + FakeVideo trust-hit are read only on the landed path, which is gated on a
        // non-null config, so they need no fallback constant.

        // ── Targeting weights ────────────────────────────────────────────────
        /// <summary>Base weight every populated stratum gets, so low-infection strata are still occasionally picked (anti-memorize).</summary>
        private const int TARGET_BASE_WEIGHT = 10;
        /// <summary>Infection (0-1) is scaled by this into an integer weight bias toward the most-infected seam.</summary>
        private const int TARGET_INFECTION_WEIGHT_SCALE = 100;

        private EntityQuery m_CurrentActQuery;
        // Live in-flight / landed attacks. Lifecycle advancement (and entity destruction) is
        // owned solely by this system, so the update must not be skipped while any attack is
        // still airborne — otherwise a gate close below Crisis would freeze and leak them.
        private EntityQuery m_LiveAttackQuery;
        private GameSimulationEndBarrier m_Barrier = null!;

        // Same-domain (Cognitive) owner of TelemarathonRuntimeState.Trust — FakeVideo routes its
        // media-trust hit through this system's one-writer method (CIVIC175), never writing Trust here.
        private TelemarathonSystem? m_Telemarathon;

        // Same-domain owner of HeroDeploymentState — a landed FakeVideo routes the Arestovych
        // trust-debt collapse through this system's one-writer method (CIVIC175).
        private HeroDeploymentSystem? m_Hero;

        // Same-domain (Cognitive) buckwheat aid — rumor-run mitigation. Real food aid
        // dampens the fake-shortage Food run (Гречка → бедные). Read-only; never written here.
        private BuckwheatSystem? m_Buckwheat;

        // Rumor resource-run: this system is the SINGLE writer of the Core Food-run
        // lever (CIVIC175). Rising/falling-edge latch so the run is logged on transition, not
        // every 1s publish. Transient — recomputed from the live rumor attacks after load.
        [System.NonSerialized] private bool m_FoodRunActive;

        // Propaganda draft-fear: same single-writer contract for the Core draft-fear lever
        // (CIVIC175); edge latch mirrors m_FoodRunActive. Transient — recomputed from the live
        // Propaganda attacks after load.
        [System.NonSerialized] private bool m_DraftFearActive;

        // Wealthy bank-run cadence. Interval-gated capital-flight drain keyed off wealthy
        // disloyalty. The interval anchor is persisted (absolute TotalGameHours) and restored via
        // ApplySavedState, so the cadence survives reload — re-anchoring it to the load moment would
        // let a player save-scum the drain by reloading before each deadline. [NonSerialized] because
        // the persist round-trip goes through PsyOpsLaunchCodec, not vanilla field serialization.
        [System.NonSerialized] private float m_LastBankRunHour;
        [System.NonSerialized] private bool m_BankRunInitialized;

        // Per-stratum defence posture — read for raid gap-targeting (dominant = the
        // type the active hero covers least). Null-object → empty list → random dominant.
        private ICognitiveCoverageReader? m_CoverageReader;
#pragma warning disable CIVIC324 // Ephemeral act-gate controller; rebuilt by OnCreate / reset paths.
        [System.NonSerialized] private ActGateController m_Gate = null!;
#pragma warning restore CIVIC324

        private GameTimeSystem? m_TimeProvider;

        // Persisted dispatcher RNG (seeds each attack; state round-trips via PsyOpsLaunchCodec).
        private SerializableRandom m_Random;

        // Enemy chatter that hits the social feed when a raid lands — the info-war vector's
        // visible tell, in the feed's own voice (mirrors IPSOBotMessageSystem's bot pool).
        // Rumor → panic-buying whispers, FakeVideo → viral vbros, Propaganda → defeatist
        // broadcast. One post per landed raid, picked from the matching pool.
        private readonly struct PsyPost
        {
            public readonly string Author;
            public readonly string Message;
            public readonly SocialMood Mood;
            public PsyPost(string author, string message, SocialMood mood)
            {
                Author = author;
                Message = message;
                Mood = mood;
            }
        }

        private static readonly PsyPost[] s_RumorPosts =
        {
            new PsyPost("@LocalInsider", "Shops are running out of bread — stock up before it's gone!", SocialMood.Paranoid),
            new PsyPost("@RealNews24", "SUPPLY SHOCK: stores empty by morning. Hoard while you still can!", SocialMood.Paranoid),
            new PsyPost("@NarodnaGazeta", "Deliveries halted citywide. Shelves stripped bare as panic spreads.", SocialMood.Warning),
            new PsyPost("@TruthPatriot_City", "They let the food run out and stay silent. Grab what you can!", SocialMood.Angry),
        };

        private static readonly PsyPost[] s_FakeVideoPosts =
        {
            new PsyPost("@RealNews24", "SHOCKING VIDEO: leaked footage exposes what they're hiding!", SocialMood.Paranoid),
            new PsyPost("@RealNews24", "This clip changes everything — watch it before they delete it!", SocialMood.Paranoid),
            new PsyPost("@LocalInsider", "A video is going around... you won't believe what officials did.", SocialMood.Suspicious),
            new PsyPost("@NarodnaGazeta", "Viral 'footage' proves the defense is staged. Millions already watching.", SocialMood.Warning),
        };

        private static readonly PsyPost[] s_PropagandaPosts =
        {
            new PsyPost("@FreedomVoice_City", "How much more can we take? Lay down arms while we're still alive.", SocialMood.Suffering),
            new PsyPost("@TruthPatriot_City", "The broadcasts lie. There is no victory here, only more graves.", SocialMood.Angry),
            new PsyPost("@NarodnaGazeta", "State radio floods every channel: resistance is 'pointless', they repeat.", SocialMood.Warning),
            new PsyPost("@FreedomVoice_City", "Our children deserve peace, not another night of sirens.", SocialMood.Suffering),
        };

        // Publish one themed enemy post for a landed raid (event bus → SocialFeedService,
        // the same pipe IPSOBotMessageSystem uses).
        private void PublishRaidLandPost(PsyOpsAttackType type)
        {
            PsyPost[] pool;
            switch (type)
            {
                case PsyOpsAttackType.Rumor: pool = s_RumorPosts; break;
                case PsyOpsAttackType.FakeVideo: pool = s_FakeVideoPosts; break;
                case PsyOpsAttackType.Propaganda: pool = s_PropagandaPosts; break;
                default:
                    Log.Warn($"[PsyOpsRaid] no feed pool for type {type} — skipping post");
                    return;
            }
            if (pool.Length == 0)
                return;
            PsyPost post = pool[m_Random.Next(0, pool.Length)];
            EventBus?.SafePublish(new SocialPostEvent(post.Author, post.Message, post.Mood), nameof(PsyOpsLaunchSystem));
        }

        // The PsyAttack-phase narrative event fires inside WaveExecutor's update, not ours — record
        // intent only, apply in the owning update (Axiom 8: no ECB / Dependency touch in an EventBus
        // callback). Accepted edge: a save taken in the one-frame window between that event and the
        // forced compose drops m_PendingRaid (transient, reset on load) — this wave's raid is
        // silently skipped. Raids are frequent, so a single miss is unnoticeable.
        [System.NonSerialized] private bool m_PendingRaid;
        // Wave number captured from the PsyAttack-phase narrative event that queued the raid — the
        // source of truth for attribution. WaveStateSingleton may already have advanced to N+1 by compose time.
        [System.NonSerialized] private int m_PendingRaidWave;
        // Transient guard against composing two raids for the same wave; reset on load.
        [System.NonSerialized] private int m_LastRaidWave;

        // ── Harm diagnostics (Axiom 1: the log is the truth) ─────────────────────────────
        // A raid's damage is a SLOW read: the impact enters infection over the landed window and
        // the aggregator republishes on its own cadence, so the city at the instant of impact says
        // nothing. Each landed raid therefore leaves a probe that re-reads the city a few game hours
        // later; the two lines together give "before → after" for one strike. Transient: a probe lost
        // to a save/load just skips one follow-up read.
        private const float HARM_PROBE_DELAY_HOURS = 6f;

        private readonly struct HarmProbe
        {
            public readonly SocialStratum Target;
            public readonly PsyOpsAttackType Type;
            public readonly int Wave;
            public readonly float Shield;
            public readonly float DueHours;

            public HarmProbe(SocialStratum target, PsyOpsAttackType type, int wave, float shield, float dueHours)
            {
                Target = target;
                Type = type;
                Wave = wave;
                Shield = shield;
                DueHours = dueHours;
            }
        }

        [System.NonSerialized] private readonly List<HarmProbe> m_HarmProbes = new();

        protected override void OnCreate()
        {
            base.OnCreate();

            m_Barrier = World.GetOrCreateSystemManaged<GameSimulationEndBarrier>();
            m_CurrentActQuery = GetEntityQuery(ComponentType.ReadOnly<CurrentActSingleton>());
            m_LiveAttackQuery = GetEntityQuery(
                ComponentType.ReadOnly<PsyOpsAttack>(),
                ComponentType.Exclude<Deleted>());
            InitializeGate();

            m_Random = new SerializableRandom(Environment.TickCount + 0x5071);
            m_TimeProvider = GameTimeSystem.Instance;
            m_Telemarathon = World.GetExistingSystemManaged<TelemarathonSystem>();
            m_Hero = World.GetExistingSystemManaged<HeroDeploymentSystem>();

            SubscribeRequired<ThreatNarrativeEvent>(OnThreatNarrative);

            // Fresh-world load order is Deserialize → OnCreate, so apply any state Deserialize
            // staged. Instance-reuse load skips OnCreate entirely (Deserialize → OnStartRunning),
            // so OnStartRunning re-applies — both paths covered (mirrors ThreatSpawnSystem).
            ApplySavedState();

#if DEBUG
            if (ServiceRegistry.IsInitialized)
                ServiceRegistry.Instance.Register<IPsyOpsDebugMutator>(this);
#endif

            Log.Info("Created (discrete PSYOPS launcher; Crisis-gated, wave-driven raids)");
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            // Load-time re-apply: OnCreate does not re-run on instance-reuse load, so the RNG
            // state staged by Deserialize would otherwise never reach m_Random (ThreatSpawnSystem
            // precedent). ApplySavedState is idempotent — it clears m_SavedRandomState after use.
            ApplySavedState();
        }

        protected override void OnDestroy()
        {
#if DEBUG
            if (ServiceRegistry.IsInitialized)
                ServiceRegistry.Instance.Unregister<IPsyOpsDebugMutator>(this);
#endif
            UnsubscribeSafe<ThreatNarrativeEvent>(OnThreatNarrative);
            // Drop the Food run so a teardown does not leave a stale multiplier for this world.
            ResourceRunAdapter.ClearFoodRun(World);
            // Same for the draft-fear lever — a stale pressure would keep feeding the evasion level.
            DraftFearAdapter.ClearPropagandaFear(World);
            Log.Info("Destroyed");
            base.OnDestroy();
        }

        protected override bool ShouldSkipUpdate()
        {
            m_Gate.ReconcileFromSingleton(m_CurrentActQuery);

            // A queued raid must reach OnThrottledUpdate even on the wake frame.
            if (m_PendingRaid)
                return false;

#if DEBUG
            // Same for a dev-panel request — it is deliberately not act-gated (that is what it tests).
            if (m_DebugRaidQueue.Count > 0 || m_DebugLandRequested)
                return false;
#endif

            // Keep ticking while attacks are airborne even with the gate closed — only this
            // system advances and destroys them; skipping would freeze and leak the entities.
            // (Raid composition stays gate-gated inside TryComposeRaid.)
            if (!m_LiveAttackQuery.IsEmptyIgnoreFilter)
                return false;

            return m_Gate.State != ActGateState.Active;
        }

        protected override void OnThrottledUpdate()
        {
            if (!TryGetGameHours(out float nowHours))
            {
                Log.Error("[PsyOpsRaid] GameTimeSystem unavailable — skipping tick");
                return;
            }

            EntityCommandBuffer ecb = default;
            bool hasEcb = false;

#if DEBUG
            // Pull in-flight attacks down to now BEFORE the lifecycle pass, so they land in this very
            // pass instead of a tick later.
            if (m_DebugLandRequested)
                DebugPullInFlightDown(nowHours);
#endif

            // 1. Advance the lifecycle of every in-flight / landed attack (also republishes the
            //    derived Food resource-run).
            ProcessLifecycle(nowHours, ref ecb, ref hasEcb);

            // 2. Compose a raid, if one was queued by entry to the PsyAttack wave phase.
            if (m_PendingRaid)
            {
                m_PendingRaid = false;
                TryComposeRaid(nowHours, ref ecb, ref hasEcb);
            }

#if DEBUG
            // 2b. …or by the dev panel, which skips the wave cadence entirely.
            if (m_DebugRaidQueue.Count > 0)
                DebugComposeRaid(nowHours, ref ecb, ref hasEcb);
#endif

            // 3. Wealthy bank-run: interval-gated capital-flight drain on the city budget
            //    while the wealthy stratum is disloyal AND an active rumor Food-run is panicking the
            //    city (Crisis-gated inside; the Food run is republished by ProcessLifecycle above).
            ProcessWealthyBankRun(nowHours, ref ecb, ref hasEcb);

            // The barrier replays the buffer itself; this ECB is main-thread only with no jobs
            // scheduled against it, so no producer job handle has to be registered.
        }

        /// <summary>
        /// Food-run contribution of one live attack (landed Rumors only). Shared by the read-only
        /// pre-pass and the mutating pass of <see cref="ProcessLifecycle"/> so the two can never drift.
        /// </summary>
        private static float FoodRunContribution(in PsyOpsAttack atk, CognitiveConfig? cfg)
        {
            if (cfg == null || atk.Phase != PsyOpsAttackPhase.Landed || atk.Type != PsyOpsAttackType.Rumor)
                return 0f;
            return atk.Intensity * (atk.HasSpread ? cfg.ResourceRunSpreadMult : 1f);
        }

        /// <summary>
        /// Draft-fear contribution of one live attack (landed Propaganda on a draft stratum,
        /// countered live by the current hero posture). Shared by both lifecycle passes.
        /// </summary>
        private static float DraftFearContribution(in PsyOpsAttack atk, HeroArchetype archetype, bool heroActive, float heroStrong, float heroFloor)
        {
            if (atk.Phase != PsyOpsAttackPhase.Landed
                || atk.Type != PsyOpsAttackType.Propaganda
                || (atk.Target != SocialStratum.Poor && atk.Target != SocialStratum.Middle))
                return 0f;
            float draftHeroEff = StratumDefense.HeroTypeEffectiveness(
                archetype, PsyOpsAttackType.Propaganda, heroActive, heroStrong, heroFloor);
            return atk.Intensity * (1f - draftHeroEff);
        }

        private void ProcessLifecycle(float nowHours, ref EntityCommandBuffer ecb, ref bool hasEcb)
        {
            var cwCfg = BalanceConfig.Current?.Cognitive;

            // Rumor rides the telecom graph: it hops to the adjacent stratum only where
            // the city network connects (telecom factor above threshold) AND is not quarantined. A
            // coverage gap (low telecom) is a natural firebreak; a Tier-3 cutoff (FIREWALL/BLACKOUT via
            // SetInternetMode) is a deliberate quarantine that halts the spread. Both leave the attack
            // un-spread (HasSpread stays false) so it hops the moment the firebreak lifts.
            float telecomFactor = SystemAPI.TryGetSingleton<PropagandaCenterState>(out var centerState)
                ? centerState.TelecomFactor : 0f;
            var rumorInternetMode = SystemAPI.TryGetSingleton<CognitiveState>(out var cwStateForRumor)
                ? cwStateForRumor.InternetMode : GlobalInternetMode.Open;
            float rumorSpreadThreshold = cwCfg?.TelecomRumorSpreadThreshold ?? FALLBACK_TELECOM_RUMOR_SPREAD_THRESHOLD;
            bool rumorQuarantined = rumorInternetMode != GlobalInternetMode.Open; // FIREWALL/BLACKOUT
            bool rumorTelecomConnects = telecomFactor >= rumorSpreadThreshold;
            bool rumorCanSpread = !rumorQuarantined && rumorTelecomConnects;

            // Hero-posture snapshot inputs — loop-invariant, resolved once per pass. Consumed at the
            // InFlight→Landed transition to FREEZE the land verdict (shield / blunted / by-whom), so
            // a speaker switch after the strike can never rewrite the historical outcome. The
            // debt-suppression mirrors MentalHealthResolverSystem's effective strong value
            // (Arestovych collapse window → strong drops to the floor).
            var hs = SystemAPI.TryGetSingleton<HeroDeploymentState>(out var heroState)
                ? heroState : HeroDeploymentState.Default;
            bool heroActive = hs.HeroStatus != HeroStatus.Inactive;
            bool arestovychSuppressed = hs.Archetype == HeroArchetype.Arestovych
                && hs.DebtCollapseEndHour > 0f && nowHours < hs.DebtCollapseEndHour;
            float heroFloor = cwCfg?.HeroCounterFloor ?? FALLBACK_HERO_COUNTER_FLOOR;
            float heroStrong = arestovychSuppressed ? heroFloor
                : (cwCfg?.HeroCounterStrong ?? FALLBACK_HERO_COUNTER_STRONG);

            // Aggregate the Food resource-run from every landed Rumor this pass. The run
            // is DERIVED (never serialized) — recomputed each tick from the live attacks, so it
            // re-arms itself on load and fades to 1.0 as rumors expire (self-limiting).
            float foodRunAccum = 0f;

            // Aggregate the draft-fear pressure from every landed Propaganda raid on a draft
            // stratum this pass (Propaganda's own vanilla lever — Rumor owns the panic runs,
            // FakeVideo the sick-leave wave). Same derived-only contract as the Food run;
            // Mobilization integrates it into its persisted evasion level via DraftFearAdapter.
            float draftFearAccum = 0f;

            // ── Read-only pre-pass ───────────────────────────────────────────────
            // PERF-LOCK: the mutating RefRW pass below must run ONLY when a transition is due.
            // A RefRW query fences against READERS of PsyOpsAttack, and MHR (a main-thread RefRO
            // reader) carries its whole resolve chain in Dependency — an unconditional RefRW pass
            // waited out that chain every 1s tick (10-31ms stalls, PERF.log 2026-07-14). RefRO
            // fences only against writers (this system itself, main-thread) — near-zero wait.
            // The pre-pass answers "is any transition due" and computes the derived accumulators
            // via the shared contribution helpers (identical pre-transition semantics: a raid
            // landing this tick starts contributing on the NEXT pass, exactly as the mutating
            // loop behaves — its InFlight branch does not accumulate either).
            bool anyTransitionDue = false;
            foreach (var atkRef in SystemAPI.Query<RefRO<PsyOpsAttack>>())
            {
                ref readonly var atk = ref atkRef.ValueRO;
                switch (atk.Phase)
                {
                    case PsyOpsAttackPhase.InFlight:
                        if (nowHours >= atk.LandTimeHours)
                            anyTransitionDue = true;
                        break;

                    case PsyOpsAttackPhase.Landed:
                        // Mirrors the mutating branch's mutation conditions exactly: expiry, or an
                        // uncontained rumor whose spread window elapsed AND the firebreak is open
                        // (a closed firebreak mutates nothing — it must not force the RefRW pass).
                        if (nowHours >= atk.ExpiryTimeHours
                            || (atk.Type == PsyOpsAttackType.Rumor
                                && atk.SpreadDeadlineHours > 0f
                                && !atk.HasSpread
                                && nowHours >= atk.SpreadDeadlineHours
                                && rumorCanSpread))
                            anyTransitionDue = true;
                        foodRunAccum += FoodRunContribution(in atk, cwCfg);
                        draftFearAccum += DraftFearContribution(in atk, hs.Archetype, heroActive, heroStrong, heroFloor);
                        break;

                    default: // Expired (or any stale value) — the mutating pass cleans it up.
                        anyTransitionDue = true;
                        break;
                }
            }

            if (!anyTransitionDue)
            {
                PublishFoodRun(foodRunAccum, cwCfg);
                PublishDraftFear(draftFearAccum);
                DrainHarmProbes(nowHours);
                return;
            }

            // ── Mutating pass (transitions due) ─────────────────────────────────
            // Recomputes the accumulators itself — the pre-pass values are discarded so this
            // branch stays byte-identical to the original single-pass loop.
            foodRunAccum = 0f;
            draftFearAccum = 0f;

            foreach (var (atkRef, entity) in
                SystemAPI.Query<RefRW<PsyOpsAttack>>().WithEntityAccess())
            {
                ref var atk = ref atkRef.ValueRW;

                switch (atk.Phase)
                {
                    case PsyOpsAttackPhase.InFlight:
                        if (nowHours >= atk.LandTimeHours)
                        {
                            atk.Phase = PsyOpsAttackPhase.Landed;

                            // Land snapshot: freeze the verdict of THIS strike under the hero posture
                            // of the landing moment. The UI reads these fields for landed contacts
                            // instead of recomputing under the current speaker (the in-flight forecast
                            // stays live — that recompute is the actionable read).
                            float shield = StratumDefense.HeroTypeEffectiveness(hs.Archetype, atk.Type, heroActive, heroStrong, heroFloor);
                            atk.LandedHeroShield = shield;
                            atk.LandedBlunted = shield > heroFloor + COVERAGE_GAP_EPSILON;
                            atk.LandedByArchetype = heroActive ? (int)hs.Archetype : -1;

                            Log.Info($"[PsyOpsRaid] land type={atk.Type} target={atk.Target} wave={atk.Wave} intensity={atk.Intensity:F2} shield={shield:F2} blunted={(atk.LandedBlunted ? 1 : 0)} by={(atk.LandedByArchetype >= 0 ? hs.Archetype.ToString() : "-")}");

                            // Damage baseline at the moment of impact + a follow-up read a few game
                            // hours later. Without these the log records that a raid landed and NOTHING
                            // about what it did — infection, sway and trust live only on the panel, so a
                            // balance pass (or a "was the hero worth it?" comparison) had nothing to read.
                            LogRaidHarm("land", in atk, shield);
                            m_HarmProbes.Add(new HarmProbe(atk.Target, atk.Type, atk.Wave, shield, nowHours + HARM_PROBE_DELAY_HOURS));

                            // The raid's visible tell: themed enemy chatter hits the social feed on land.
                            PublishRaidLandPost(atk.Type);

                            // …and the speaker answers it, if one is on air. Blunted = his counter-type
                            // took the hit (the ONE moment his work is visible); landed clean while he
                            // speaks = the wrong voice for this carrier, said plainly instead of in a
                            // tooltip. Silent when nobody is deployed — the feed then carries only the
                            // enemy, which is exactly the point.
                            if (heroActive)
                            {
                                if (atk.LandedBlunted)
                                    HeroVoice.PostBlunted(EventBus, hs.Archetype);
                                else
                                    HeroVoice.PostWrongVoice(EventBus, hs.Archetype);
                            }

                            // FakeVideo (deepfake) additionally undermines the credibility of the
                            // player's own channels — one-shot media-trust hit at land. Routed through
                            // the Telemarathon one-writer (CIVIC175); never written here.
                            if (atk.Type == PsyOpsAttackType.FakeVideo && cwCfg != null)
                            {
                                m_Telemarathon ??= World.GetExistingSystemManaged<TelemarathonSystem>();
                                m_Telemarathon?.ApplyMediaTrustHit(-cwCfg.FakeVideoTrustHit, "fake video");

                                // A FakeVideo is Arestovych's counter-type: if he is the active speaker,
                                // the landing "exposes the lie" and may collapse his accrued trust-debt
                                // (one-writer respected — HeroDeploymentSystem owns the write, CIVIC175).
                                m_Hero ??= World.GetExistingSystemManaged<HeroDeploymentSystem>();
                                m_Hero?.ApplyArestovychMajorHit(atk.Type);
                            }
                        }
                        break;

                    case PsyOpsAttackPhase.Landed:
                        // The landed pressure itself is NOT pushed from here: MentalHealthResolverSystem
                        // reads the live landed attacks each fire and integrates their per-stratum
                        // pressure over its own dt (PsyOpsRaidPressure) — height × landed duration
                        // shapes the profile: Propaganda low (grind), FakeVideo high/short (spike),
                        // Rumor medium+spread. This branch only advances the rumor spread state.

                        // Rumor contagion-hop: after an uncontained spread window, seed the
                        // wealth-adjacent stratum — but only where the telecom graph connects and
                        // the zone is not quarantined. A coverage gap or a Tier-3 cutoff
                        // is a firebreak; HasSpread/SpreadDeadline persist so the hop fires the moment
                        // the firebreak lifts.
                        if (atk.Type == PsyOpsAttackType.Rumor
                            && atk.SpreadDeadlineHours > 0f
                            && !atk.HasSpread
                            && nowHours >= atk.SpreadDeadlineHours)
                        {
                            if (rumorCanSpread)
                            {
                                atk.SecondaryTarget = AdjacentStratum(atk.Target, atk.Seed);
                                atk.HasSpread = true;
                                Log.Info($"[PsyOpsRaid] rumor spread {atk.Target}->{atk.SecondaryTarget} wave={atk.Wave} telecom={telecomFactor:F2}");
                            }
                            else if (Log.IsDebugEnabled)
                            {
                                Log.Debug($"[PsyOpsRaid] rumor firebreak (quarantine={rumorQuarantined} telecom={telecomFactor:F2}) wave={atk.Wave}");
                            }
                        }

                        // A landed Rumor maps (v1) to a Food run; a spread rumor widens it
                        // (wider panic = a bigger run). Accumulated here, published after the loop.
                        foodRunAccum += FoodRunContribution(in atk, cwCfg);

                        // A landed Propaganda raid on a draft stratum presses the men toward
                        // evasion for its whole landed window. Countered LIVE by the current hero
                        // posture (Patriot is its RPS counter) — deploying the right speaker
                        // mid-window blunts the ongoing pressure, mirroring how the resolver
                        // integrates landed raid pressure. Wealthy targets carry no draft fear
                        // (not a draft stratum — their harm routes to Monaco/bank-run).
                        draftFearAccum += DraftFearContribution(in atk, hs.Archetype, heroActive, heroStrong, heroFloor);

                        if (nowHours >= atk.ExpiryTimeHours)
                        {
                            atk.Phase = PsyOpsAttackPhase.Expired;
                            EnsureEcb(ref ecb, ref hasEcb);
                            ecb.DestroyEntity(entity);
                            Log.Info($"[PsyOpsRaid] expire type={atk.Type} target={atk.Target} wave={atk.Wave}");
                        }
                        break;

                    default: // Expired (or any stale value) — clean up.
                        EnsureEcb(ref ecb, ref hasEcb);
                        ecb.DestroyEntity(entity);
                        break;
                }
            }

            // Publish the derived Food run for this world (single writer — CIVIC175). Republished
            // every pass so it tracks land/spread/expire; no live rumors → foodRunAccum 0 → run 1.
            PublishFoodRun(foodRunAccum, cwCfg);

            // Publish the derived draft-fear pressure (single writer — CIVIC175). Republished
            // every pass so it tracks land/expire; no live propaganda → 0 → the Mobilization-side
            // evasion level decays on its own.
            PublishDraftFear(draftFearAccum);

            DrainHarmProbes(nowHours);
        }

        /// <summary>
        /// Read the city a few game hours after a raid landed and print what the strike actually did.
        /// Pairs with the "land" line: same wave, same carrier, same shield — so a log grep gives
        /// "landed clean → this much infection" versus "blunted by the right speaker → this much".
        /// </summary>
        private void DrainHarmProbes(float nowHours)
        {
            if (m_HarmProbes.Count == 0)
                return;

            for (int i = m_HarmProbes.Count - 1; i >= 0; i--)
            {
                var probe = m_HarmProbes[i];
                if (nowHours < probe.DueHours)
                    continue;

                m_HarmProbes.RemoveAt(i);
                LogHarmSnapshot("after", probe.Type, probe.Target, probe.Wave, probe.Shield);
            }
        }

        private void LogRaidHarm(string moment, in PsyOpsAttack atk, float shield)
            => LogHarmSnapshot(moment, atk.Type, atk.Target, atk.Wave, shield);

        /// <summary>One "state of the city" line, keyed to the raid that caused it.</summary>
        private void LogHarmSnapshot(string moment, PsyOpsAttackType type, SocialStratum target, int wave, float shield)
        {
            if (!SystemAPI.TryGetSingleton<CognitiveStatsState>(out var stats))
                return;

            float targetInfection;
            switch (target)
            {
                case SocialStratum.Poor: targetInfection = stats.PoorAvgInfection; break;
                case SocialStratum.Middle: targetInfection = stats.MiddleAvgInfection; break;
                case SocialStratum.Wealthy: targetInfection = stats.WealthyAvgInfection; break;
                default:
                    // Unknown / unclassified stratum — the city average is the honest read here,
                    // rather than pretending a per-stratum number exists.
                    targetInfection = stats.AvgInfectionLevel;
                    break;
            }

            // The marathon is the standing defence this line is meant to explain; if its system is
            // missing the harm read is meaningless, so re-resolve and say so rather than logging a
            // confident "no trust, off air".
            m_Telemarathon ??= World.GetExistingSystemManaged<TelemarathonSystem>();
            string defence = m_Telemarathon == null
                ? "trust=? onAir=?"
                : $"trust={m_Telemarathon.CurrentTrust:F2} onAir={m_Telemarathon.IsOnAir}";

            Log.Info($"[PsyOpsHarm] {moment} wave={wave} type={type} target={target} shield={shield:F2} " +
                     $"| infected={stats.HouseholdsInfected}/{stats.TotalHouseholds} " +
                     $"targetAvgInfection={targetInfection:F3} cityAvgInfection={stats.AvgInfectionLevel:F3} " +
                     $"{defence}");
        }

        /// <summary>
        /// Recompute and publish the aggregate Food resource-run. Intensity is scaled
        /// from the summed rumor contribution and capped; active buckwheat aid (real food on
        /// shelves) dampens the fake-shortage run (Гречка → бедные). The demand-up multiplier is
        /// the visible tell; the intensity drives the economy commerce bite + UI. Derived only —
        /// nothing here touches the save (mirrors CrisisEconomicsAdapter's published float).
        /// </summary>
        private void PublishFoodRun(float accum, CognitiveConfig? cfg)
        {
            float intensity = 0f;
            bool buckwheatAid = false;
            if (cfg != null && accum > 0f)
            {
                intensity = math.clamp(accum * cfg.ResourceRunIntensity, 0f, cfg.ResourceRunMaxIntensity);

                // Mitigation: buckwheat food aid answers the fake shortage → the run shrinks.
                m_Buckwheat ??= World.GetExistingSystemManaged<BuckwheatSystem>();
                buckwheatAid = m_Buckwheat != null && m_Buckwheat.HasActiveFoodAid;
                if (buckwheatAid)
                    intensity *= math.max(0f, 1f - cfg.BuckwheatRunMitigation);
            }

            float demandMult = 1f + (cfg != null ? intensity * cfg.ResourceRunDemandScale : 0f);
            ResourceRunAdapter.PublishFoodRun(World, demandMult, intensity);

            // Rising / falling edge only (Axiom 1 greppable marker without 1s spam).
            bool active = intensity > 0f;
            if (active != m_FoodRunActive)
            {
                m_FoodRunActive = active;
                if (active)
                    Log.Info($"[PsyOpsRun] Food run ACTIVE intensity={intensity:F2} demandMult={demandMult:F2} (buckwheatAid={buckwheatAid})");
                else
                    Log.Info("[PsyOpsRun] Food run cleared (no live rumors) — demand back to vanilla");
            }
        }

        /// <summary>
        /// Publish the aggregate draft-fear pressure of the live landed Propaganda raids
        /// (hero-countered, draft strata only). Derived only — Mobilization owns the persisted
        /// evasion level it integrates from this; nothing here touches the save.
        /// </summary>
        private void PublishDraftFear(float pressure)
        {
            DraftFearAdapter.PublishPropagandaFear(World, pressure);

            // Rising / falling edge only (Axiom 1 greppable marker without 1s spam).
            bool active = pressure > 0f;
            if (active != m_DraftFearActive)
            {
                m_DraftFearActive = active;
                if (active)
                    Log.Info($"[PsyOpsFear] Draft-fear pressure ACTIVE pressure={pressure:F2} — propaganda presses the draft strata");
                else
                    Log.Info("[PsyOpsFear] Draft-fear pressure cleared (no live propaganda) — evasion level decays");
            }
        }

        /// <summary>
        /// Wealthy bank-run: while an active rumor Food-run panics the city AND the wealthy
        /// stratum is disloyal, rich households pull capital → an interval-gated drain straight on the
        /// city budget (Core BudgetEmitter, Axiom-5 clean — Cognitive never imports Finance). Uses the
        /// debt-fallback variant so it bites even a near-empty treasury (capital flight, not a gated
        /// purchase). Modest &amp; self-limiting: capped per cycle, only during a live Food run above the
        /// disloyalty threshold, scaled by it. The Food-run gate ties the flight to the rumor panic
        /// (thematic — capital flees the run, not any infection source), not to background disloyalty.
        /// </summary>
        private void ProcessWealthyBankRun(float nowHours, ref EntityCommandBuffer ecb, ref bool hasEcb)
        {
            if (m_Gate.State != ActGateState.Active)
                return;

            var cwCfg = BalanceConfig.Current?.Cognitive;
            if (cwCfg == null)
                return;

            // Anchor the interval to the first tick after the gate first activates (fresh world),
            // so the first drain lands one interval after war start rather than immediately.
            // After a load the anchor is restored from the save (ApplySavedState), so this latch is
            // already set and the persisted cadence — not the load moment — drives the next drain.
            if (!m_BankRunInitialized)
            {
                m_BankRunInitialized = true;
                m_LastBankRunHour = nowHours;
                return;
            }

            float interval = math.max(cwCfg.BankRunIntervalHours, 0.1f);
            if (nowHours - m_LastBankRunHour < interval)
                return;

            // Advance the cadence even when the run is dormant or disloyalty is below threshold —
            // otherwise the interval "banks up" and fires a burst of drains the instant either
            // condition flips.
            m_LastBankRunHour = nowHours;

            // Gate strictly on the ACTIVE rumor Food-run: wealthy capital flees during the
            // panic-buy run, not from any infection source. m_FoodRunActive is refreshed by
            // PublishFoodRun earlier this same tick (ProcessLifecycle runs before this in OnThrottledUpdate),
            // so it reflects the current pass.
            if (!m_FoodRunActive)
                return;

            float wealthyDisloyalty = SystemAPI.TryGetSingleton<CognitiveStatsState>(out var stats)
                ? stats.WealthyAvgInfection : 0f;
            float threshold = cwCfg.BankRunDisloyaltyThreshold;

            if (wealthyDisloyalty <= threshold)
                return;

            float span = math.max(1f - threshold, 0.001f);
            float disloyaltyFactor = math.clamp((wealthyDisloyalty - threshold) / span, 0f, 1f);
            long amount = (long)math.round(cwCfg.BankRunBudgetDrainPerCycle * disloyaltyFactor);
            if (amount <= 0)
                return;

            EnsureEcb(ref ecb, ref hasEcb);
            BudgetEmitter.QueueDeductWithDebtFallback(
                ecb,
                amount,
                BudgetCategory.CognitiveOps,
                BudgetPriority.Damage,
                "RumorBankRun",
                BudgetCategory.CognitiveOps);

            Log.Info($"[PsyOpsRun] wealthy bank-run drain=${amount:N0} wealthyDisloyalty={wealthyDisloyalty:F2} (threshold={threshold:F2})");
        }

        /// <summary>
        /// Wealth-adjacent stratum a rumor spreads into. Poor/Wealthy each have one neighbour (Middle);
        /// Middle is adjacent to both, so the direction is drawn deterministically from the attack's
        /// frozen seed (save-safe — the rumor resumes the same direction after reload).
        /// </summary>
        private static SocialStratum AdjacentStratum(SocialStratum stratum, uint seed) => stratum switch
        {
            SocialStratum.Poor => SocialStratum.Middle,
            SocialStratum.Wealthy => SocialStratum.Middle,
            SocialStratum.Middle => (seed & 1u) == 0u ? SocialStratum.Poor : SocialStratum.Wealthy,
            SocialStratum.Unknown => SocialStratum.Unknown, // unclassified target — no spread
            _ => throw new ArgumentOutOfRangeException(nameof(stratum), stratum, "Unknown SocialStratum — add case to PsyOpsLaunchSystem.AdjacentStratum")
        };

        private void TryComposeRaid(float nowHours, ref EntityCommandBuffer ecb, ref bool hasEcb)
        {
            if (m_Gate.State != ActGateState.Active)
                return;

            // Attribute to the wave that actually ended (carried by the event), not the live
            // WaveStateSingleton, which may have advanced to N+1 by this forced next frame.
            int waveNumber = m_PendingRaidWave;
            if (waveNumber < MIN_RAID_WAVE)
            {
                if (Log.IsDebugEnabled)
                    Log.Debug($"[PsyOpsRaid] skip wave={waveNumber} (< MIN_RAID_WAVE={MIN_RAID_WAVE})");
                return;
            }

            if (waveNumber <= m_LastRaidWave)
                return; // already raided this wave
            m_LastRaidWave = waveNumber;

            int raidSize = math.clamp(1 + (waveNumber - MIN_RAID_WAVE) / RAID_GROWTH_DIVISOR, 1, MAX_RAID_SIZE);
            ComposeRaid(nowHours, waveNumber, raidSize, forcedType: null, forcedTarget: null, ref ecb, ref hasEcb);
        }

        /// <summary>
        /// Spawn one raid's worth of typed contacts. Composition: ONE dominant contact at full
        /// intensity (the main threat to read) + secondary contacts at reduced intensity (the
        /// guaranteed off-type leak). <paramref name="forcedType"/> null = the in-play composition
        /// (dominant gap-biased toward the type the active hero counters least, secondaries random);
        /// a value pins every contact to that one carrier, so a single carrier can be read against a
        /// single hero posture.
        /// </summary>
        private void ComposeRaid(float nowHours, int waveNumber, int raidSize, PsyOpsAttackType? forcedType, SocialStratum? forcedTarget, ref EntityCommandBuffer ecb, ref bool hasEcb)
        {
            // Stats may not exist yet (pre-aggregation) — a default (all-zero) read degrades to the
            // Poor-seam fallback in PickTargetStratum, which is the intended behaviour.
            if (!SystemAPI.TryGetSingleton<CognitiveStatsState>(out var stats))
                stats = default;

            var cwCfg = BalanceConfig.Current?.Cognitive;
            float perWave = cwCfg?.PsyOpsIntensityPerWave ?? FALLBACK_INTENSITY_PER_WAVE;
            float maxIntensity = cwCfg?.PsyOpsMaxIntensity ?? FALLBACK_MAX_INTENSITY;
            float inFlight = cwCfg?.PsyOpsInFlightHours ?? FALLBACK_IN_FLIGHT_HOURS;
            float rumorSpreadWindow = cwCfg?.RumorSpreadWindowHours ?? FALLBACK_RUMOR_SPREAD_WINDOW;

            var dominantType = forcedType ?? PickDominantType();
            float secondaryMult = cwCfg?.RaidSecondaryIntensityMult ?? FALLBACK_RAID_SECONDARY_INTENSITY_MULT;

            EnsureEcb(ref ecb, ref hasEcb);

            for (int i = 0; i < raidSize; i++)
            {
                bool isDominant = i == 0;
                var type = forcedType ?? (isDominant ? dominantType : PickType());
                var target = forcedTarget ?? PickTargetStratum(stats);
                // Per-type base height scaled by the same additive wave-progression as IPSO
                // (WaveScalingService has no 0-1 scalar). Secondary contacts land at a reduced
                // height (the off-type leak). Per-stratum vulnerability is applied later at emit time,
                // where the hit stratum (primary or spread) is known.
                // Cap at min(maxIntensity, 1): the serializer's read-clamp is [0,1], so the write
                // range must match it under ANY config (a >1 cap would silently shrink on load).
                float intensity = math.clamp((PerTypeBase(type, cwCfg) + waveNumber * perWave) * (isDominant ? 1f : secondaryMult), 0f, math.min(maxIntensity, 1f));
                uint seed = NextSeed();

                float landHours = nowHours + inFlight;
                float landedDuration = LandedDurationHours(type, cwCfg);

                var atk = new PsyOpsAttack
                {
                    Type = type,
                    Target = target,
                    Intensity = intensity,
                    LaunchTimeHours = nowHours,
                    LandTimeHours = landHours,
                    InterceptWindowEndHours = landHours,
                    ExpiryTimeHours = landHours + landedDuration,
                    Phase = PsyOpsAttackPhase.InFlight,
                    Seed = seed,
                    Wave = waveNumber,
                    IsDominant = isDominant,
                    SecondaryTarget = SocialStratum.Unknown,
                    HasSpread = false,
                    // Land snapshot is empty until the attack lands. The object-initializer bypasses
                    // SetDefaults(), so the "-1 = no hero / not landed yet" sentinel must be set here
                    // explicitly — a raw 0 would read as HeroArchetype.Voice for an in-flight attack.
                    LandedHeroShield = 0f,
                    LandedBlunted = false,
                    LandedByArchetype = -1,
                    // Rumor only: absolute deadline after which an uncontained rumor spreads to the
                    // adjacent stratum. 0 for Propaganda/FakeVideo (no spread).
                    SpreadDeadlineHours = type == PsyOpsAttackType.Rumor ? landHours + rumorSpreadWindow : 0f
                };

                var e = ecb.CreateEntity();
                ecb.AddComponent(e, atk);

                EventBus?.SafePublish(new PsyOpsAttackEvent(type, target, waveNumber, intensity));
                Log.Info($"[PsyOpsRaid] launch type={type} target={target} wave={waveNumber} intensity={intensity:F2} eta={inFlight}h landed={landedDuration}h seed={seed}");
            }

            Log.Info($"[PsyOpsRaid] composed wave={waveNumber} size={raidSize} dominant={dominantType} ({(forcedType.HasValue ? "forced carrier" : "gap-biased")}; gate=Crisis+)");
        }

        // ── Targeting / composition ──────────────────────────────────────────

        private PsyOpsAttackType PickType()
        {
            // Uniform seeded pick among the three carriers. Act-driven drift toward more mental
            // carriers later is a future-phase escalation, intentionally not modelled here.
            return (PsyOpsAttackType)m_Random.Next(0, 3);
        }

        /// <summary>
        /// Pick the raid's DOMINANT type with a gap-bias: the enemy dominates with the
        /// attack type the active hero archetype counters LEAST (lowest per-type cover from the
        /// cognitive coverage projection). Ties among the least-covered types break on the dispatcher
        /// RNG. When no hero is active (all per-type covers equal) — or coverage is unavailable
        /// (null-object empty list) — there is no seam to exploit and it falls back to the uniform
        /// random pick.
        /// </summary>
        private PsyOpsAttackType PickDominantType()
        {
            m_CoverageReader ??= ServiceRegistryFeatureExtensions.TryGetOrNullObject(NullCognitiveCoverageReader.Instance);
            var list = m_CoverageReader.GetCoverage();
            if (list.Count > 0)
            {
                // Per-type covers are city-wide (identical on every stratum entry) — read entry 0.
                var c = list[0];
                float minCover = math.min(c.PropagandaCover, math.min(c.RumorCover, c.FakeVideoCover));
                float maxCover = math.max(c.PropagandaCover, math.max(c.RumorCover, c.FakeVideoCover));
                if (maxCover - minCover > COVERAGE_GAP_EPSILON)
                {
                    Span<PsyOpsAttackType> gaps = stackalloc PsyOpsAttackType[3];
                    int n = 0;
                    if (c.PropagandaCover <= minCover + COVERAGE_GAP_EPSILON) gaps[n++] = PsyOpsAttackType.Propaganda;
                    if (c.RumorCover <= minCover + COVERAGE_GAP_EPSILON) gaps[n++] = PsyOpsAttackType.Rumor;
                    if (c.FakeVideoCover <= minCover + COVERAGE_GAP_EPSILON) gaps[n++] = PsyOpsAttackType.FakeVideo;
                    return gaps[m_Random.Next(0, n)];
                }
            }
            return PickType();
        }

        /// <summary>Per-type base intensity height (contract, fallback to const). Profile shape = height × landed duration.</summary>
        private static float PerTypeBase(PsyOpsAttackType type, CognitiveConfig? cfg) => type switch
        {
            PsyOpsAttackType.Propaganda => cfg?.PropagandaIntensityBase ?? FALLBACK_PROPAGANDA_BASE,
            PsyOpsAttackType.Rumor => cfg?.RumorIntensityBase ?? FALLBACK_RUMOR_BASE,
            PsyOpsAttackType.FakeVideo => cfg?.FakeVideoIntensityBase ?? FALLBACK_FAKEVIDEO_BASE,
            _ => FALLBACK_RUMOR_BASE
        };

        /// <summary>Per-type landed window (hours): Propaganda/Rumor medium (grind / grind+spread), FakeVideo short (burst).</summary>
        private static float LandedDurationHours(PsyOpsAttackType type, CognitiveConfig? cfg) => type switch
        {
            PsyOpsAttackType.Propaganda => cfg?.PropagandaDurationHours ?? FALLBACK_PROPAGANDA_DURATION,
            PsyOpsAttackType.Rumor => cfg?.RumorDurationHours ?? FALLBACK_RUMOR_DURATION,
            PsyOpsAttackType.FakeVideo => cfg?.FakeVideoDecayHours ?? FALLBACK_FAKEVIDEO_DECAY,
            _ => FALLBACK_RUMOR_DURATION
        };

        /// <summary>
        /// Infection-weighted seeded pick among the three strata (read model). Each
        /// populated stratum gets a base weight plus a bias proportional to its average infection,
        /// so the most-infected seam is favoured without being deterministic (anti-set-and-forget).
        /// Pre-Phase-04 this is infection-weighted, not coverage-gap-weighted (no per-stratum
        /// cognitive coverage exists yet). Falls back to Poor if no stratum is classified yet.
        /// </summary>
        private SocialStratum PickTargetStratum(in CognitiveStatsState stats)
        {
            Span<int> weights = stackalloc int[3];
            weights[0] = StratumWeight(stats.PoorHouseholds, stats.PoorAvgInfection);
            weights[1] = StratumWeight(stats.MiddleHouseholds, stats.MiddleAvgInfection);
            weights[2] = StratumWeight(stats.WealthyHouseholds, stats.WealthyAvgInfection);

            int total = weights[0] + weights[1] + weights[2];
            if (total <= 0)
                return SocialStratum.Poor; // nothing classified yet — default seam

            int roll = m_Random.Next(0, total);
            int idx = DamageTargetingMath.WeightedPick(weights, total, roll);
            return idx switch
            {
                0 => SocialStratum.Poor,
                1 => SocialStratum.Middle,
                _ => SocialStratum.Wealthy
            };
        }

        private static int StratumWeight(int count, float avgInfection)
        {
            if (count <= 0)
                return 0;
            int infectionBias = (int)math.round(math.clamp(avgInfection, 0f, 1f) * TARGET_INFECTION_WEIGHT_SCALE);
            return TARGET_BASE_WEIGHT + infectionBias;
        }

        private uint NextSeed()
        {
            uint seed = (uint)m_Random.Next();
            return seed == 0 ? 1u : seed; // 0 is the inert-default sentinel in PsyOpsAttack
        }

        // ── Time ─────────────────────────────────────────────────────────────

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

        // ── Gate / events ────────────────────────────────────────────────────

        private void InitializeGate()
        {
            m_Gate = new ActGateController(
                isOpenFor: act => act >= Act.Crisis,
                onTransition: HandleGateTransition);
        }

        private void HandleGateTransition(ActGateState old, ActGateState next, bool isInitial)
        {
            if (next == ActGateState.Active)
                Log.Info($"[PsyOpsRaid] gate opened (Crisis+) — discrete PSYOPS raids armed (initial={isInitial})");
        }

        private void OnThreatNarrative(ThreatNarrativeEvent evt)
        {
            // Raids launch on entry to the PsyAttack phase — the info-war beat the wave
            // clock opens right after the kinetic strike (coordinated combined assault),
            // not at wave-end. WaveExecutor only enters PsyAttack when PsyOpsArming.WillFire
            // holds, so this fires exactly on the waves that should raid. Ignore all other
            // narrative/phase events.
            if (evt.Type != ThreatNarrativeEventType.WavePhaseChanged || evt.Phase != GamePhase.PsyAttack)
                return;

            // EventBus callback runs inside WaveExecutor's update window, not ours — only record
            // intent; OnThrottledUpdate composes the raid on the forced next frame (Axiom 8).
            m_PendingRaid = true;
            m_PendingRaidWave = evt.WaveNumber;
            ForceNextUpdate();

            if (Log.IsDebugEnabled)
                Log.Debug($"[PsyOpsRaid] PsyAttack phase entered (wave {evt.WaveNumber}) — raid queued");
        }

        private void EnsureEcb(ref EntityCommandBuffer ecb, ref bool hasEcb)
        {
            if (hasEcb)
                return;
            ecb = m_Barrier.CreateCommandBuffer();
            hasEcb = true;
        }

#if DEBUG
        // ── Dev panel (IPsyOpsDebugMutator) ──────────────────────────────────
        // In play a raid only composes when a wave enters its PsyAttack phase, so from a running city
        // the whole info-war block is unreachable on demand. These hooks record intent from the UI
        // thread; the composition itself runs in this system's own update through the SAME ComposeRaid
        // path as a real raid (Axiom 8: no ECB from a foreign update).

        private readonly struct DebugRaidRequest
        {
            public readonly int Type;
            public readonly int Stratum;
            public DebugRaidRequest(int type, int stratum) { Type = type; Stratum = stratum; }
        }

        // A QUEUE, not a single slot: the panel buttons are clicked faster than the 1s throttled
        // update drains the request, and a single field silently swallowed every click but the
        // last (observed live: three carriers clicked, one raid launched). List, not Queue<T> —
        // Queue<T> is ambiguous between System and mscorlib in this net48 reference set (CS0433).
        [System.NonSerialized] private readonly List<DebugRaidRequest> m_DebugRaidQueue = new();
        [System.NonSerialized] private bool m_DebugLandRequested;

        public void DebugLaunchRaid(int typeOverride, int stratumOverride, string source)
        {
            if (typeOverride < -1 || typeOverride > 2)
            {
                Log.Warn($"[DEBUG] {source}: unknown PSYOPS override {typeOverride} (-1 = full raid, 0-2 = single carrier)");
                return;
            }
            if (stratumOverride < 0 || stratumOverride > 3)
            {
                Log.Warn($"[DEBUG] {source}: unknown PSYOPS stratum {stratumOverride} (0 = weighted, 1-3 = Poor/Middle/Wealthy)");
                return;
            }

            m_DebugRaidQueue.Add(new DebugRaidRequest(typeOverride, stratumOverride));
            ForceNextUpdate();
            Log.Info($"[DEBUG] {source}: PSYOPS raid queued (override={typeOverride}, stratum={(stratumOverride == 0 ? "weighted" : ((SocialStratum)stratumOverride).ToString())}, pending={m_DebugRaidQueue.Count})");
        }

        public void DebugLandInFlight(string source)
        {
            m_DebugLandRequested = true;
            ForceNextUpdate();
            Log.Info($"[DEBUG] {source}: landing every in-flight PSYOPS attack now");
        }

        /// <summary>
        /// Compose outside the wave cadence and outside the act gate — the raid itself is what the panel
        /// is here to test. The gate still decides whether the mental-damage consumers are awake, so a
        /// pre-Crisis launch says so: its [PsyOpsHarm] read would be a false zero.
        /// </summary>
        private void DebugComposeRaid(float nowHours, ref EntityCommandBuffer ecb, ref bool hasEcb)
        {
            if (m_Gate.State != ActGateState.Active)
                Log.Warn("[DEBUG] PSYOPS raid launched below Crisis — the mental-damage consumers are act-gated, so harm will read as zero");

            int wave = SystemAPI.TryGetSingleton<WaveStateSingleton>(out var ws) ? ws.WaveNumber : 0;

            // Drain EVERY pending click this pass — each request composes its own raid, so a fast
            // Propaganda→Rumor→FakeVideo burst launches three contacts, not just the last one.
            for (int i = 0; i < m_DebugRaidQueue.Count; i++)
            {
                var request = m_DebugRaidQueue[i];
                bool fullRaid = request.Type < 0;
                int raidSize = fullRaid
                    ? math.clamp(1 + (wave - MIN_RAID_WAVE) / RAID_GROWTH_DIVISOR, 1, MAX_RAID_SIZE)
                    : 1;
                PsyOpsAttackType? forcedType = fullRaid ? null : (PsyOpsAttackType)request.Type;
                SocialStratum? forcedTarget = request.Stratum == 0 ? null : (SocialStratum)request.Stratum;

                ComposeRaid(nowHours, wave, raidSize, forcedType, forcedTarget, ref ecb, ref hasEcb);
            }
            m_DebugRaidQueue.Clear();
        }

        /// <summary>
        /// Bring every in-flight attack down to this instant: a raid's damage only exists after it lands,
        /// and the in-flight window is hours of game time. The landed window (and a rumor's spread
        /// deadline) is re-anchored to now so the shortened flight does not shorten the strike itself.
        /// </summary>
        private void DebugPullInFlightDown(float nowHours)
        {
            m_DebugLandRequested = false;

            var cwCfg = BalanceConfig.Current?.Cognitive;
            float rumorSpreadWindow = cwCfg?.RumorSpreadWindowHours ?? FALLBACK_RUMOR_SPREAD_WINDOW;
            int pulled = 0;

            foreach (var atkRef in SystemAPI.Query<RefRW<PsyOpsAttack>>())
            {
                ref var atk = ref atkRef.ValueRW;
                if (atk.Phase != PsyOpsAttackPhase.InFlight)
                    continue;

                atk.LandTimeHours = nowHours;
                atk.InterceptWindowEndHours = nowHours;
                atk.ExpiryTimeHours = nowHours + LandedDurationHours(atk.Type, cwCfg);
                if (atk.Type == PsyOpsAttackType.Rumor)
                    atk.SpreadDeadlineHours = nowHours + rumorSpreadWindow;
                pulled++;
            }

            Log.Info($"[DEBUG] pulled {pulled} in-flight PSYOPS attack(s) down to now — they land in this pass");
        }
#endif
    }
}
