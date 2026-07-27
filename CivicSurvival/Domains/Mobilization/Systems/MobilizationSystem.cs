using System;
using System.Collections.Generic;
using CivicSurvival.Core.Features.Wellbeing;
using Colossal.Serialization.Entities;
using Game;
using Game.Common;
using Game.Simulation;
using Unity.Entities;
using CivicSurvival.Core.Components.Lifecycle;
using CivicSurvival.Core.Components.Domain.Economy;
using CivicSurvival.Core.Components.Domain.Mobilization;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Components.Requests;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Events;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Interfaces.Domain.Debug;
using CivicSurvival.Core.Interfaces.Domain.Corruption;
using CivicSurvival.Core.Interfaces.Domain.Mobilization;
using CivicSurvival.Core.Interfaces.Domain.Population;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Services.Countermeasures;
using CivicSurvival.Core.Systems;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.UI;
using CivicSurvival.Core.UI.DomainState;
using CivicSurvival.Core.Utils;
using CivicSurvival.Core.Logic;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Components.Threats;
using CivicSurvival.Core.Systems.Scheduling;

using CivicSurvival.Core.Services;
namespace CivicSurvival.Domains.Mobilization.Systems
{
    /// <summary>
    /// Manpower management system.
    /// Optimized with Frame Throttling for population queries and heavy logic.
    /// </summary>
    [ActIndependent]
    [SingletonOwner(typeof(MobilizationStateSingleton))]
    [OwnedSingletonLifecycle(
        Persisted = true,
        EnsurePhase = SingletonLifecyclePhase.OnStartRunning | SingletonLifecyclePhase.OnLoadRestore,
        DisposePhase = SingletonLifecyclePhase.OnDestroy)]
    public partial class MobilizationSystem : CivicSystemBase, IDefaultSerializable, IResettable, IPostLoadValidation, ICivicSingletonOwner<MobilizationStateSingleton>, IMobilizationManpowerReader
#if DEBUG
        , IMobilizationDebugMutator
#endif
    {
        private static readonly LogContext Log = new("MobilizationSystem");

        private DistrictPenaltySystem? m_PenaltySystem;
        [NonSerialized]
        private CivicSingletonHandle<MobilizationStateSingleton> m_Singleton;
        private EntityQuery m_SingletonQuery;

        [Persist] private int m_UsedManpower;
        [Persist] private int m_Casualties;
        [Persist] private bool m_ConscriptionActive;
        [Persist(Default = -1)] private int m_LastWarDay = -1;

        // Draft-evasion level (0-1, "ухилянти"): integrated from the Core draft-fear lever the
        // Cognitive launcher republishes from live landed Propaganda raids. Persisted — the
        // sociological damage outlives the raid window and only decays with time.
        [Persist] private float m_DodgerLevel;
        // Integration anchor (TotalGameHours). Transient: re-anchors on the first tick after
        // load so a load never produces a catch-up jump.
        [NonSerialized] private float m_LastDodgerHour = -1f;
        // Rising/falling edge latch for the greppable log marker (Axiom 1).
        [NonSerialized] private bool m_DodgerActive;
        // Level at the last breakdown-dirtying publish. The dirty threshold MUST compare against
        // this, never against the per-frame delta: at frame cadence a single step of the
        // integration (~6e-5 at typical pressure) sits below any sane epsilon, and gating the
        // ASSIGNMENT on it silently freezes the level at zero forever (observed live:
        // [PsyOpsFear] ACTIVE for a full propaganda window, [Dodgers] never rose).
        [NonSerialized] private float m_LastPublishedDodgerLevel;
        /// <summary>Accumulated drift from the last published level that re-dirties the breakdown.</summary>
        private const float DODGER_LEVEL_EPSILON = 0.0001f;

        private const int HASH_INIT = 17;
        private const int HASH_MULTIPLIER = 31;

        /// <summary>
        /// Stable allocation identity. Type is metadata on the value, not part of
        /// identity: releases can arrive with AATypeHash=0 after the AA was already
        /// destroyed, but they still refer to the same entity allocation.
        /// </summary>
        public readonly struct AllocKey : System.IEquatable<AllocKey>
        {
            public readonly int EntityIndex;
            public readonly int EntityVersion;

            public AllocKey(int entityIndex, int entityVersion)
            {
                EntityIndex = entityIndex;
                EntityVersion = entityVersion;
            }

            public bool Equals(AllocKey other) =>
                EntityIndex == other.EntityIndex && EntityVersion == other.EntityVersion;

            public override bool Equals(object obj) => obj is AllocKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = HASH_INIT;
                    hash = hash * HASH_MULTIPLIER + EntityIndex;
                    hash = hash * HASH_MULTIPLIER + EntityVersion;
                    return hash;
                }
            }

            public override string ToString() => $"Alloc:{EntityIndex}v{EntityVersion}";

            public static bool operator ==(AllocKey left, AllocKey right) => left.Equals(right);
            public static bool operator !=(AllocKey left, AllocKey right) => !left.Equals(right);
        }

        private struct AllocEntry
        {
            public int TypeHash;
            public int Count;

            public AllocEntry(int typeHash, int count)
            {
                TypeHash = typeHash;
                Count = count;
            }
        }

        private Dictionary<AllocKey, AllocEntry> m_Allocations = new();

        private readonly List<KeyValuePair<AllocKey, AllocEntry>> m_ForceReleaseSortedScratch = new();

        private ManpowerBreakdown m_CachedBreakdown;

        /// <summary>
        /// The single owner of "how many people live in the city". Resolved in OnStartRunning and
        /// re-resolved with <c>??=</c> inside <see cref="TryRefreshPopulation"/>, because
        /// ValidateAfterLoad reaches that method through UpdateBreakdown and runs BEFORE
        /// OnStartRunning in PLVS sequencing.
        /// </summary>
        private ICityPopulationReader? m_CityPopulation;

        /// <summary>
        /// Last population the owner actually handed over. Meaningful only while
        /// <see cref="m_HasPopulationMeasure"/> is true — zero here is a REAL zero (an empty city),
        /// never a "not measured yet" marker. The old raw counter had no way to refuse, so this
        /// field carried both meanings at once and the rebuild could not tell them apart.
        /// </summary>
        private int m_CachedPopulation;

        /// <summary>
        /// True while <see cref="m_CachedPopulation"/> holds an answer the owner gave and did not
        /// later withdraw. This is what replaced the "zero means not measured" convention: the
        /// owner refuses honestly, so the refusal is stored as a refusal.
        /// </summary>
        private bool m_HasPopulationMeasure;

        /// <summary>
        /// What the last read was — see <see cref="PopulationSource"/>. Session-local on purpose:
        /// the first read after a load re-derives it from the owner, so persisting it would only
        /// let a stale cause describe a fresh measurement.
        /// </summary>
        [NonSerialized] private PopulationSource m_PopulationSource = PopulationSource.Unknown;

        /// <summary>
        /// Rebuilds held by the population gate since it last closed. Printed when the gate opens
        /// again, so the log states how long the city was unmeasurable rather than leaving the
        /// duration to be inferred from timestamps. Session-local: the count describes this run's
        /// gate episode, and carrying it across a load would attribute the previous run's hold to
        /// the first rebuild after the load.
        /// </summary>
        [NonSerialized] private int m_GateHeldRebuilds;

        /// <summary>
        /// A critical-latch seed that arrived while the gate was shut. Deferred, never dropped: the
        /// seed exists so a load of an already-critical city re-arms the latch silently, and losing
        /// it would turn the first successful rebuild after the gate into a false rising edge.
        /// </summary>
        private bool m_PendingCriticalSeed;

        /// <summary>
        /// Why the population read came back as it did. Until the owner existed, three different
        /// causes arrived as one indistinguishable zero (no facade in the registry, no host on the
        /// facade, a count that genuinely returned zero) — which is what let a mid-game zero pass
        /// for an empty city for two rounds of investigation.
        /// </summary>
        private enum PopulationSource
        {
            /// <summary>Nothing read yet this session/reset.</summary>
            Unknown = 0,
            /// <summary>Owner refused: no city entity, so the question has no answer.</summary>
            NoCityEntity = 1,
            /// <summary>Owner answered zero and there IS a city — nobody lives in it.</summary>
            EmptyCity = 2,
            /// <summary>Owner answered a positive count.</summary>
            Measured = 3
        }

        [RuntimeInputDirtyCursor("Frame throttle for the population owner read; not a snapshot producer/consumer version cursor.")]
        [NonSerialized] private uint m_LastUpdateFrame;
#pragma warning disable CIVIC324 // Debug-only morale override; intentionally session-local and cleared by ResetBootDefaultsFields.
        [NonSerialized] private bool m_DebugMoraleOverrideActive;
        [NonSerialized] private float m_DebugMoraleOverride;
#pragma warning restore CIVIC324
        [NonSerialized] private bool m_LoggedMissingPenaltySystem;
        private bool m_IsDirty;

        private const int POPULATION_REFRESH_INTERVAL = 128;
        // Use config values — ManpowerLogic.IsCritical reads BalanceConfig.Current.Mobilization
        // .CriticalThreshold (entry) and .CriticalRecoveryBand (hysteresis, exit).

        /// <summary>
        /// Latched "pool is critical" verdict. Persisted as STATE, deliberately not as the hour of
        /// the last alert.
        ///
        /// The previous form was <c>currentHour - m_LastCriticalEventHour &gt; HOURS_PER_DAY</c>
        /// with the stamp defaulting to 0.0 — but 0.0 is a legal game hour (hour 0 of day 0), not a
        /// "never fired" marker, so the first alert of any city could not fire before game hour 24.
        /// Every scenario starts on day 0, which left the whole first-day window — exactly when a
        /// player over-commits crew to guns — permanently silent. Observed live 2026-07-25: the pool
        /// stood at 2 available of 86 on day 0 hour 15 and nothing was raised, whatever the save
        /// held, because the difference could not exceed 15.4 in the first place.
        ///
        /// A latch has no epoch to get wrong. The alert is the RISING EDGE; staying critical is
        /// carried by the UI badge (mirrored into MobilizationStateSingleton.IsManpowerCritical),
        /// not re-announced on a timer.
        /// </summary>
        [Persist] private bool m_ManpowerCritical;

        /// <summary>
        /// Edge latch for the "nobody to ask about settlement" line below. The state it reports is
        /// ordinary for the first ticks of a session and a real defect if it persists, so it is
        /// worth one line per episode and worthless once per rebuild. Session-local: the next run
        /// must state its own answer rather than inherit a silence from this one.
        /// </summary>
        [NonSerialized] private bool m_LoggedSettlementUnknown;

        [Persist(Unclamped = true)] private float m_CallToArmsCooldownEndHour;

        [Persist(Unclamped = true)] private float m_ConscriptionCooldownEndHour;

        private EntityQuery m_ReleaseRequestQuery;
        private EntityQuery m_CasualtyRequestQuery;
        private EntityQuery m_CorruptionQuery;
        private EntityQuery m_DraftExemptionQuery;
        // Scenario domain, Core singleton (Axiom 5): read for one bit only — has anyone ever
        // lived here — which the manpower numbers cannot supply for themselves.
        private EntityQuery m_ScenarioQuery;
        private GameSimulationEndBarrier m_GameSimulationEndBarrier = null!;
        private ComponentLookup<MobilizationStateSingleton> m_SingletonLookup;
        private EntityQuery m_AAInstallationQuery;
        private IShadowReputationService m_ReputationService = null!;

        // ============================================================
        // IMobilizationService Implementation
        // ============================================================

        public bool IsConscriptionActive => m_ConscriptionActive;

        /// <summary>
        /// Free heads for a DISPLAY. <c>false</c> when the breakdown is frozen behind the
        /// population gate: the cached numbers then describe a city nobody has measured since,
        /// and handing them out as a count is exactly how "unknown" used to reach the screen as
        /// a confident zero.
        ///
        /// The rebuild is attempted first (unchanged from the old getter) precisely so this is
        /// not a stale-read helper — it asks, then reports whether the answer stands.
        /// </summary>
        public bool TryGetAvailableManpower(out int available)
        {
            UpdateBreakdown(false);
            if (!m_HasPopulationMeasure)
            {
                available = 0;
                return false;
            }

            available = m_CachedBreakdown.AvailableManpower;
            return true;
        }

        private static string AllocationId(AllocKey key, int typeHash) =>
            $"Alloc:{typeHash}@{key.EntityIndex}v{key.EntityVersion}";

        /// <summary>
        /// The DECISION side. <see cref="ManpowerCheck.PoolUnmeasured"/> is returned whenever the
        /// gate in <see cref="UpdateBreakdown"/> refused to rebuild: the pool is not merely small,
        /// it is unknown, and the two must never arrive as one answer. The old <c>bool</c> could
        /// not say that — a frozen breakdown answered "false" and every caller wrote "not enough
        /// people" over it.
        /// </summary>
        public ManpowerCheck CanRecruit(int amount)
        {
            UpdateBreakdown(false);
            if (!m_HasPopulationMeasure) return ManpowerCheck.PoolUnmeasured;
            // Nothing to commit is trivially satisfiable — a crewless installation is not a
            // caller error, so this is Ok rather than an InvalidAmount-style rejection.
            if (amount <= 0) return ManpowerCheck.Ok;
            return m_CachedBreakdown.AvailableManpower >= amount
                ? ManpowerCheck.Ok
                : ManpowerCheck.InsufficientManpower;
        }

        /// <summary>
        /// Commit heads. Returns the same three-state answer as <see cref="CanRecruit"/> so the
        /// caller can tell a refusal to decide from a refusal to grant: on
        /// <see cref="ManpowerCheck.PoolUnmeasured"/> the request was not judged at all and must
        /// be retried in silence, without spending a one-shot notification on it.
        /// </summary>
#pragma warning disable CIVIC231 // Public API — callers are responsible for act check
        public ManpowerCheck TryRecruit(int amount, int typeHash, int idx, int ver)
#pragma warning restore CIVIC231
        {
            var check = CanRecruit(amount);
            if (check != ManpowerCheck.Ok) return check;

            // XSTEP: crew commit-on-place routes through Core CrewMath (shared with AA release / future
            // server recompute); inline `used + amount` here would re-derive the transition off-Core.
            m_UsedManpower = CrewMath.Commit(m_UsedManpower, amount);

            var key = new AllocKey(idx, ver);
            m_Allocations.TryGetValue(key, out var entry);
            if (entry.TypeHash == 0 && typeHash != 0)
                entry.TypeHash = typeHash;
            entry.Count += amount;
            m_Allocations[key] = entry;

            m_IsDirty = true;

            int available = CrewMath.Available(m_CachedBreakdown.TotalManpower, m_UsedManpower);
            string allocationId = AllocationId(key, entry.TypeHash);
            EventBus?.SafePublish(new ManpowerRecruitedEvent(amount, allocationId, available));
            Log.Info($"Recruited {amount} for '{allocationId}' - {available} available");

            CheckCritical();
            return ManpowerCheck.Ok;
        }

        public void Release(int amount, int typeHash, int idx, int ver)
        {
            amount = System.Math.Max(0, amount);
            var key = new AllocKey(idx, ver);
            int eventTypeHash = typeHash;
            int released = 0;
            if (m_Allocations.TryGetValue(key, out var entry))
            {
                if (entry.TypeHash != 0)
                    eventTypeHash = entry.TypeHash;

                released = System.Math.Min(amount, System.Math.Max(0, entry.Count));
                int newAlloc = entry.Count - released;
                if (newAlloc <= 0) m_Allocations.Remove(key);
                else
                {
                    entry.Count = newAlloc;
                    m_Allocations[key] = entry;
                }
            }

            // XSTEP: crew release routes through Core CrewMath (same accumulator AA release decrements);
            // inline `max(0, used - released)` here would fork the committed-pool transition off-Core.
            m_UsedManpower = CrewMath.Release(m_UsedManpower, released);
            m_IsDirty = true;
            UpdateBreakdown(true);
            int availableAfter = CrewMath.Available(m_CachedBreakdown.TotalManpower, m_UsedManpower);
            EventBus?.SafePublish(new ManpowerReleasedEvent(released, AllocationId(key, eventTypeHash), availableAfter));
        }

        public bool IsCallToArmsOnCooldown
        {
            get
            {
                var tp = GameTimeSystem.Instance;
                return tp != null && tp.Current.TotalGameHours < m_CallToArmsCooldownEndHour;
            }
        }

        public bool IsConscriptionReactivationOnCooldown
        {
            get
            {
                var tp = GameTimeSystem.Instance;
                return tp != null && tp.Current.TotalGameHours < m_ConscriptionCooldownEndHour;
            }
        }

        // ============================================================
        // Lifecycle & Update
        // ============================================================

        protected override void OnCreate()
        {
            base.OnCreate();
            m_ReleaseRequestQuery = GetEntityQuery(ComponentType.ReadWrite<ManpowerReleaseRequest>());
            m_CasualtyRequestQuery = GetEntityQuery(ComponentType.ReadWrite<CasualtyReportRequest>());
            m_GameSimulationEndBarrier = World.GetOrCreateSystemManaged<GameSimulationEndBarrier>();
            m_AAInstallationQuery = GetEntityQuery(ComponentType.ReadOnly<AirDefenseInstallation>());
            m_CorruptionQuery = GetEntityQuery(ComponentType.ReadOnly<CorruptionSingleton>());
            m_DraftExemptionQuery = GetEntityQuery(ComponentType.ReadOnly<DraftExemptionSingleton>());
            m_ScenarioQuery = GetEntityQuery(ComponentType.ReadOnly<ScenarioSingleton>());
            m_SingletonLookup = GetComponentLookup<MobilizationStateSingleton>(false);
            m_SingletonQuery = GetEntityQuery(ComponentType.ReadWrite<MobilizationStateSingleton>());
            m_Singleton = CreateSingletonHandle<MobilizationStateSingleton>(m_SingletonQuery);

            SubscribeRequired<WarDayChangedEvent>(OnWarDayChanged);

            RestoreMobilizationSingleton();

            if (ServiceRegistry.IsInitialized)
            {
                ServiceRegistry.Instance.Register<IMobilizationManpowerReader>(this);
            }

#if DEBUG
            if (ServiceRegistry.IsInitialized)
            {
                ServiceRegistry.Instance.Register<IMobilizationDebugMutator>(this);
            }
#endif
            m_IsDirty = true;
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            m_ReputationService = ServiceRegistryFeatureExtensions.TryGetOrNullObject(NullShadowReputationService.Instance);
            m_PenaltySystem ??= FeatureRegistry.Instance.Query<DistrictPenaltySystem>();
            if (m_PenaltySystem == null && !m_LoggedMissingPenaltySystem)
            {
                Log.Warn("DistrictPenaltySystem unavailable; mobilization will use neutral social penalties");
                m_LoggedMissingPenaltySystem = true;
            }
            // Canonical resolution point for the population owner (CIVIC403: not OnCreate). The
            // same ??= is repeated in TryRefreshPopulation for the load path, where
            // ValidateAfterLoad reaches the rebuild before OnStartRunning has run at all.
            m_CityPopulation ??= ServiceRegistry.Instance.Require<ICityPopulationReader>();
            RestoreMobilizationSingleton();
            // SECOND ENTRANCE into the rebuild, and the one that seeds the critical latch outside
            // any post-load context. It is covered because the population gate lives INSIDE
            // UpdateBreakdown, not at the ValidateAfterLoad call site — a gate placed at the caller
            // would leave this path free to rebuild capacity, force-release crew and seed the latch
            // off an unmeasured city.
            UpdateBreakdown(true, refreshPopulation: true, seedCriticalLatch: true);
        }

        protected override void OnUpdateImpl()
        {
            IntegrateDodgerLevel();

            // Player mobilization commands are drained by MobilizationRequestIntakeSystem
            // (ModificationEnd, pause-safe per Axiom 14) — not here.
            ProcessReleaseRequests();
            ProcessCasualtyRequests();

            UpdateBreakdown(false);
        }

        /// <summary>
        /// Integrate the draft-evasion level from the Core draft-fear lever (landed enemy
        /// Propaganda pressure on the draft strata, hero-countered at the source). Gain scales
        /// with the pressure and — while conscription is active — with the fear multiplier;
        /// decay is proportional to the ACCUMULATED level (exponential relaxation), so any
        /// pressure produces an effect proportional to its strength and the level settles at
        /// the equilibrium <c>gain / DodgerDecayPerHour</c> (clamped to 1). A constant decay
        /// would instead create a dead zone where weak early-wave propaganda (pressure below
        /// decay/accum ≈ 0.33) could never lift the level off zero — observed live as
        /// <c>[PsyOpsFear] ACTIVE</c> with no <c>[Dodgers]</c> ever following.
        /// </summary>
        private void IntegrateDodgerLevel()
        {
            var tp = GameTimeSystem.Instance;
            if (tp == null)
                return;

            float now = tp.Current.TotalGameHours;
            if (m_LastDodgerHour < 0f)
            {
                m_LastDodgerHour = now;
                return;
            }

            // Negative delta (load anomaly) re-anchors; oversized delta is clamped so a stall
            // never integrates a burst of fear in one step.
            float dh = now - m_LastDodgerHour;
            m_LastDodgerHour = now;
            if (dh <= 0f)
                return;
            dh = System.Math.Min(dh, 1f);

            var mobCfg = BalanceConfig.Current.Mobilization;
            float pressure = DraftFearAdapter.GetPropagandaFear(World);
            float gain = pressure * mobCfg.DodgerAccumPerHour
                * (m_ConscriptionActive ? mobCfg.DodgerConscriptionFearMult : 1f);
            // The integration is UNCONDITIONAL — only the breakdown-dirty is thresholded, and
            // against the accumulated drift since the last publish (see the field note above).
            m_DodgerLevel = System.Math.Clamp(
                m_DodgerLevel + (gain - mobCfg.DodgerDecayPerHour * m_DodgerLevel) * dh, 0f, 1f);

            if (System.Math.Abs(m_DodgerLevel - m_LastPublishedDodgerLevel) > DODGER_LEVEL_EPSILON)
            {
                m_LastPublishedDodgerLevel = m_DodgerLevel;
                m_IsDirty = true;
            }

            bool active = m_DodgerLevel > 0.001f;
            if (active != m_DodgerActive)
            {
                m_DodgerActive = active;
                if (active)
                    Log.Info($"[Dodgers] draft evasion RISING level={m_DodgerLevel:F3} pressure={pressure:F2} conscription={m_ConscriptionActive}");
                else
                    Log.Info("[Dodgers] draft evasion drained — manpower back to full");
            }
        }

        /// <summary>
        /// W2 row 166 (SAVE_LOAD_LIFECYCLE_DOCTRINE Invariant 2). The cached
        /// singleton handle may point at an entity destroyed by load. Re-resolve
        /// from the world first, dedup, and create only if genuinely absent.
        /// Idempotent; called from lifecycle/restore before update code consumes
        /// the cached handle.
        /// </summary>
        private void RestoreMobilizationSingleton()
        {
            var previous = m_Singleton.Entity;
            var entity = EnsureSingleton(ref m_Singleton, MobilizationStateSingleton.Default);
            if (entity != previous)
                m_IsDirty = true;
        }

        public void OnLoadRestore(EntityManager entityManager)
        {
            // Merge policy: PreferRestoredPayload. Mobilization's durable payload
            // lives in persisted system fields; PLVS recreates the singleton before
            // ValidateAfterLoad rebuilds allocation-derived UsedManpower and writes
            // the authoritative breakdown.
            EnsureSingleton(ref m_Singleton, entityManager, MobilizationStateSingleton.Default);
            m_IsDirty = true;
        }

        /// <param name="seedCriticalLatch">
        /// Adopt the critical verdict without announcing it. Set by the rebuild-from-scratch sites
        /// (OnStartRunning / ValidateAfterLoad / ResetState) so a load of an already-critical city
        /// re-arms the latch instead of re-toasting on every load. Deferred, not dropped, when the
        /// population gate refuses: it is carried in <see cref="m_PendingCriticalSeed"/> until a
        /// rebuild actually completes.
        /// </param>
        private void UpdateBreakdown(bool force, bool refreshPopulation = false, bool seedCriticalLatch = false)
        {
            m_SingletonLookup.Update(this);
            uint currentFrame = (uint)UnityEngine.Time.frameCount;
            bool populationIntervalElapsed = currentFrame - m_LastUpdateFrame >= (uint)POPULATION_REFRESH_INTERVAL;

            bool shouldRefresh = force ||
                                m_IsDirty ||
                                populationIntervalElapsed ||
                                !m_HasPopulationMeasure;

            // Remembered before ANY early return — the throttle above and the gate below both.
            // A seed request that arrives while the city is unmeasurable must survive to the first
            // rebuild that succeeds; dropping it would make that rebuild announce a rising edge for
            // a city that was already critical when it was loaded, the exact re-toast the seed
            // exists to suppress. (All three seeding callers also pass force, so the throttle never
            // swallows one today — this keeps that from being load-bearing.)
            m_PendingCriticalSeed |= seedCriticalLatch;

            if (!shouldRefresh) return;

            using (PerformanceProfiler.Measure("Mobilization.UpdateBreakdown"))
            {
                if (refreshPopulation || !m_HasPopulationMeasure || populationIntervalElapsed)
                {
                    // THE GATE. On refusal nothing below runs: capacity is not rebuilt off an
                    // unmeasured city, ForceReleaseExcess cannot strip crew from the guns, and the
                    // critical latch does not move. The previous shape had no gate to bypass — the
                    // raw counter answered an unmeasurable city with a plain zero, BuildBreakdown
                    // turned that zero into TotalManpower=0 (the defence floor needs a positive base
                    // pool to engage), every committed crew became "excess", and the force-release
                    // and the critical alert both fired on a measurement that was never taken.
                    if (!TryRefreshPopulation())
                    {
                        // The gate holds the NUMBERS, but the verdict "these numbers are stale"
                        // has to travel anyway. Without this write the mirror keeps the last
                        // successful rebuild AND the trust flag that came with it, so every
                        // singleton reader — the mobilization panel, the air-defense roster, the
                        // sweep's live/archetype switch — goes on treating a frozen breakdown as
                        // a fresh measurement. That is the display-side half of the same defect
                        // the gate closes on the decision side.
                        PublishPoolMeasured(false);
                        return;
                    }
                }

                seedCriticalLatch = m_PendingCriticalSeed;
                m_PendingCriticalSeed = false;

                var corruption = m_CorruptionQuery.TryGetSingleton<CorruptionSingleton>(out var cor)
                    ? cor : CorruptionSingleton.Default;
                // Willingness to serve reads the corruption the CITY CAN SEE — every scheme it can
                // witness, not the export slider alone as it did historically. Offshore balance and
                // sold deferrals are excluded inside CalculateVisibleToCity: the first is a stock
                // nobody on the street sees (and scoring it would make the counterattack war chest
                // permanently cost crews), the second already costs heads directly below.
                float visibleCorruption = CorruptionCalculator.CalculateVisibleToCity(
                    corruption.ExportLoadPercent,
                    corruption.VIPDistrictCount,
                    corruption.VIPBypassCount,
                    corruption.EmergencyFundWithdrawn,
                    corruption.FuelSiphonPercent,
                    corruption.AccumulatedExposure,
                    corruption.ShadyContractCount);

                // Draft-Exemption scheme (Corruption domain, Core singleton — Axiom 5):
                // sold deferrals bite a reversible slice; sold disabilities are permanent heads.
                var draftExemption = m_DraftExemptionQuery.TryGetSingleton<DraftExemptionSingleton>(out var de)
                    ? de : DraftExemptionSingleton.Default;

                float happinessPenalty = GetAverageHappinessPenalty();
                var timeProvider = GameTimeSystem.Instance;
                int warDay = timeProvider?.Current.WarDay ?? m_LastWarDay;

                m_CachedBreakdown = ManpowerLogic.BuildBreakdown(
                    m_CachedPopulation, visibleCorruption, corruption.ExportLoadPercent,
                    happinessPenalty, m_DodgerLevel, warDay,
                    m_UsedManpower, m_Casualties,
                    draftExemption.RatePercent, draftExemption.DisabilityHeadcount,
                    m_ConscriptionActive
                );
                ApplyDebugMoraleOverrideIfNeeded();

                if (m_UsedManpower > m_CachedBreakdown.TotalManpower && m_CachedBreakdown.TotalManpower >= 0)
                {
                    ForceReleaseExcess(m_UsedManpower - m_CachedBreakdown.TotalManpower);
                    m_CachedBreakdown = ManpowerLogic.BuildBreakdown(
                        m_CachedPopulation, visibleCorruption, corruption.ExportLoadPercent,
                    happinessPenalty, m_DodgerLevel, warDay,
                        m_UsedManpower, m_Casualties,
                        draftExemption.RatePercent, draftExemption.DisabilityHeadcount,
                        m_ConscriptionActive);
                    ApplyDebugMoraleOverrideIfNeeded();
                }

                // Predicted force-release: how much crew DeactivateConscription would
                // pull off the guns right now. Computed from the final breakdown
                // factors (after ForceReleaseExcess re-build and the debug-morale
                // override) so the number matches the actual force-release that the
                // next deactivation would trigger.
                int predictedConscriptionRelease = 0;
                if (m_ConscriptionActive)
                {
                    int totalWithoutConscription = ManpowerLogic.EffectiveTotal(
                        m_CachedBreakdown.BasePool,
                        m_CachedBreakdown.PatriotismFactor,
                        m_CachedBreakdown.MoraleFactor,
                        m_CachedBreakdown.FatigueFactor,
                        m_CachedBreakdown.DodgerFactor,
                        m_CachedBreakdown.Casualties,
                        draftExemption.RatePercent,
                        draftExemption.DisabilityHeadcount,
                        isConscription: false);
                    predictedConscriptionRelease = ManpowerLogic.PredictedForceRelease(m_UsedManpower, totalWithoutConscription);
                }

                // Latch BEFORE the singleton write so the mirror below carries the fresh verdict,
                // and announce only AFTER it — a subscriber must never observe the alert ahead of
                // the state that describes it.
                bool criticalEdge = UpdateCriticalLatch(seedCriticalLatch);

                var singletonEntity = m_Singleton.Entity;
                if (singletonEntity != Entity.Null && m_SingletonLookup.HasComponent(singletonEntity))
                {
                    m_SingletonLookup[singletonEntity] = new MobilizationStateSingleton
                    {
                        AvailableManpower = m_CachedBreakdown.AvailableManpower,
                        UsedManpower = m_CachedBreakdown.UsedManpower,
                        TotalManpower = m_CachedBreakdown.TotalManpower,
                        BasePool = m_CachedBreakdown.BasePool,
                        Casualties = m_CachedBreakdown.Casualties,
                        Population = m_CachedBreakdown.Population,
                        PatriotismFactor = m_CachedBreakdown.PatriotismFactor,
                        VisibleCorruptionScore = m_CachedBreakdown.VisibleCorruptionScore,
                        ExportLoadPercent = m_CachedBreakdown.ExportLoadPercent,
                        IsDefenceFloorEngaged = m_CachedBreakdown.IsDefenceFloorEngaged,
                        MoraleFactor = m_CachedBreakdown.MoraleFactor,
                        FatigueFactor = m_CachedBreakdown.FatigueFactor,
                        DodgerFactor = m_CachedBreakdown.DodgerFactor,
                        DodgerCount = m_CachedBreakdown.DodgerCount,
                        DeferralManpower = m_CachedBreakdown.DeferralManpower,
                        DisabilityManpower = m_CachedBreakdown.DisabilityManpower,
                        ConscriptionBonus = m_CachedBreakdown.ConscriptionBonus,
                        IsConscriptionActive = m_CachedBreakdown.IsConscriptionActive,
                        IsWarFatigued = m_CachedBreakdown.IsWarFatigued,
                        IsManpowerCritical = m_ManpowerCritical,
                        WarDay = m_CachedBreakdown.WarDay,
                        CallToArmsCooldownEndHour = m_CallToArmsCooldownEndHour,
                        IsCallToArmsOnCooldown = IsCallToArmsOnCooldown,
                        ConscriptionCooldownEndHour = m_ConscriptionCooldownEndHour,
                        IsConscriptionReactivationOnCooldown = IsConscriptionReactivationOnCooldown,
                        PredictedConscriptionRelease = predictedConscriptionRelease,
                        SocialPenaltyProducerReady = m_PenaltySystem != null,
                        // Reached only past the gate, so this is unconditionally true here. Kept
                        // as an explicit assignment rather than a default so the field cannot be
                        // silently dropped from the write and start reading as "measured" off
                        // MobilizationStateSingleton.Default.
                        IsPoolMeasured = true
                    };
                }

                m_LastUpdateFrame = currentFrame;
                m_IsDirty = false;

                // The threshold check lives HERE, at the tail of the rebuild, not in the callers.
                // ForceReleaseExcess above is exactly the case the alert exists for — the pool
                // collapsed under crew already committed — yet it could never raise it: calling
                // CheckCritical from the release path would re-enter UpdateBreakdown and recurse
                // straight back into ForceReleaseExcess. Firing off the finished breakdown covers
                // every route into a critical pool (recruit, casualties, force release, a shrinking
                // city) with one call and no re-entry.
                //
                // AXIOM 14 — this placement is the pause-safety invariant, not a tidiness choice.
                // MobilizationSystem itself is registered in GameSimulation, which does not tick
                // while paused (decompile: SimulationSystem.OnUpdate runs the GameSimulation phase
                // inside `if (num != 0)` and num is 0 at selectedSpeed 0). The alert stays reachable
                // while paused ONLY because pause-ticking callers reach the rebuild: AACrewAssignment
                // (ModificationEnd) via TryRecruit, and AABuildingPlacementHandler via CanRecruit.
                // Crewing guns while paused is the ordinary way a player drives the pool critical.
                // Moving this call into OnUpdateImpl would kill the alert in pause — do not "tidy".
                if (criticalEdge) PublishCriticalTransition();
            }
        }

        /// <summary>
        /// Ask the population owner and record the answer. Returns false when the owner refuses —
        /// there is no city entity, so the question has no answer and the caller must not decide
        /// anything. A refusal deliberately CLEARS <see cref="m_HasPopulationMeasure"/> instead of
        /// leaving the last number standing: from that point every rebuild re-asks and stays gated
        /// until the city answers again, so the gate cannot be walked around by a call that happens
        /// to land inside the frame-throttle window.
        ///
        /// Axiom 1 — both edges are logged at the normal level, never at Debug. A silent early
        /// return here would reproduce the trap that cost the investigation two wrong verdicts:
        /// "no rebuild happened" and "the rebuild found nothing" read identically in the log.
        /// Logging is edge-triggered, so a long unmeasurable stretch costs two lines, not one per
        /// frame.
        /// </summary>
        /// <summary>
        /// Stamp the trust flag on the state mirror without touching any of the numbers.
        /// Called from the gate's refusal path, where the numbers are deliberately left alone —
        /// the readers need to know the figures they can see are frozen, not to have them zeroed
        /// (a zeroed pool is a different lie, and it is the one that lights the critical badge).
        ///
        /// AXIOM 14 — the readers are pause-ticking panels in UIUpdate and this system is not.
        /// The write is reachable while paused for the same reason the critical alert is: the
        /// pause-ticking callers (AACrewAssignment via TryRecruit, the placement handler via
        /// CanRecruit) reach the rebuild, and the flag rides the same route.
        /// </summary>
        private void PublishPoolMeasured(bool measured)
        {
            var singletonEntity = m_Singleton.Entity;
            if (singletonEntity == Entity.Null || !m_SingletonLookup.HasComponent(singletonEntity))
                return;

            var state = m_SingletonLookup[singletonEntity];
            if (state.IsPoolMeasured == measured) return;

            state.IsPoolMeasured = measured;
            m_SingletonLookup[singletonEntity] = state;
        }

        private bool TryRefreshPopulation()
        {
            // Repeated ??= (the contract's own instruction): ValidateAfterLoad reaches this method
            // through UpdateBreakdown and runs before OnStartRunning resolved anything.
            m_CityPopulation ??= ServiceRegistry.Instance.Require<ICityPopulationReader>();

            if (!m_CityPopulation.TryGetResidentCount(out int residents))
            {
                m_HasPopulationMeasure = false;
                ReportPopulationSource(PopulationSource.NoCityEntity, 0);
                if (m_GateHeldRebuilds == 0)
                {
                    Log.Warn($"[Manpower] breakdown HELD — population unmeasurable (cause=no-city-entity); capacity, force-release and critical latch frozen at total={m_CachedBreakdown.TotalManpower} used={m_UsedManpower} critical={m_ManpowerCritical}");
                }
                m_GateHeldRebuilds++;
                return false;
            }

            m_CachedPopulation = residents;
            m_HasPopulationMeasure = true;
            ReportPopulationSource(
                residents > 0 ? PopulationSource.Measured : PopulationSource.EmptyCity,
                residents);

            if (m_GateHeldRebuilds > 0)
            {
                Log.Info($"[Manpower] breakdown RESUMED — population={residents} after {m_GateHeldRebuilds} held rebuild(s)");
                m_GateHeldRebuilds = 0;
            }

            return true;
        }

        /// <summary>
        /// Print the source of the population reading whenever it CHANGES — which covers the
        /// zero-to-non-zero and non-zero-to-zero transitions the plan asks for, and additionally
        /// separates the two kinds of zero that used to be one symptom: a city with nobody in it
        /// versus no city to ask at all.
        ///
        /// This is the line that decides the city-unload hypothesis. If a mid-game zero is the
        /// unload window, it shows up here as <c>cause=no-city-entity</c> while the mod is still
        /// ticking; if it is a genuinely emptied city, it shows up as <c>cause=empty-city</c>. The
        /// question cannot be answered from the outside — telemetry carries one session id per
        /// process, so every city unload inside a run looks like mid-game.
        ///
        /// Normal log level on purpose (Axiom 1): a Debug line answers nothing in a player's log,
        /// and that is where the evidence has to come from. Cost is bounded by the edge check —
        /// a steady city prints once.
        /// </summary>
        private void ReportPopulationSource(PopulationSource source, int residents)
        {
            if (source == m_PopulationSource) return;

            var previous = m_PopulationSource;
            m_PopulationSource = source;
            Log.Info($"[PopSource] mobilization residents={residents} cause={SourceName(source)} prev={SourceName(previous)}");
        }

        /// <summary>
        /// Const strings for the log — no enum ToString on a path reachable from update. Every
        /// declared cause is named explicitly, including Unknown: the discard arm used to answer
        /// "never-read" for anything it did not recognise, which would have quietly relabelled a
        /// cause added later as "the owner was never asked" — the exact confusion this enum exists
        /// to end. The enum is private and only ever assigned the four values above, so the arm
        /// below is unreachable; if it ever fires, the cause is a new member nobody taught the log.
        /// </summary>
        private static string SourceName(PopulationSource source) => source switch
        {
            PopulationSource.Unknown => "never-read",
            PopulationSource.NoCityEntity => "no-city-entity",
            PopulationSource.EmptyCity => "empty-city",
            PopulationSource.Measured => "measured",
            _ => throw new System.ArgumentOutOfRangeException(nameof(source), source, "Unknown PopulationSource — add a case to SourceName")
        };

        public void ReportCasualties(int amount, int typeHash, int idx, int ver)
        {
            amount = System.Math.Max(0, amount);
            m_Casualties = System.Math.Max(0, m_Casualties + amount);

            var key = new AllocKey(idx, ver);
            int eventTypeHash = typeHash;
            int released = 0;
            if (m_Allocations.TryGetValue(key, out var entry))
            {
                if (entry.TypeHash != 0)
                    eventTypeHash = entry.TypeHash;

                released = System.Math.Min(amount, System.Math.Max(0, entry.Count));
                int newAlloc = entry.Count - released;
                if (newAlloc <= 0) m_Allocations.Remove(key);
                else
                {
                    entry.Count = newAlloc;
                    m_Allocations[key] = entry;
                }
            }

            // XSTEP: casualty-driven crew release routes through Core CrewMath — the committed pool
            // shrinks by the same transition as a clean release, kept single-source in Core.
            m_UsedManpower = CrewMath.Release(m_UsedManpower, released);
            m_IsDirty = true;
            Log.Warn($"CASUALTIES: {amount} ({AllocationId(key, eventTypeHash)})");
            EventBus?.SafePublish(new ManpowerCasualtiesEvent(amount, m_Casualties, $"hash={eventTypeHash}"), "MobilizationSystem");
            CheckCritical();
        }

        public bool ActivateConscription()
        {
            if (m_ConscriptionActive) return false;
            if (IsConscriptionReactivationOnCooldown) return false;
            m_ConscriptionActive = true;
            var config = BalanceConfig.Current.Mobilization;
            ApplyReputationDelta(config.ConscriptionReputation, "Conscription activated");

            var tp = GameTimeSystem.Instance;
            if (tp != null)
                m_ConscriptionCooldownEndHour = tp.Current.TotalGameHours + config.ConscriptionCooldownHours;

            m_IsDirty = true;
            Log.Info($"CONSCRIPTION ACTIVATED, reactivation cooldown until hour {m_ConscriptionCooldownEndHour:F1}");
            EventBus?.SafePublish(new ConscriptionActivatedEvent(), "MobilizationSystem");
            return true;
        }

        public void DeactivateConscription()
        {
            if (!m_ConscriptionActive) return;
            m_ConscriptionActive = false;
            m_IsDirty = true;
            EventBus?.SafePublish(new ConscriptionDeactivatedEvent(), "MobilizationSystem");
        }

        public bool CallToArms()
        {
            if (IsCallToArmsOnCooldown) return false;
            if (m_Casualties <= 0) return false;
            var config = BalanceConfig.Current.Mobilization;
            int recovered = System.Math.Min(m_Casualties, config.CallToArmsRecovery);
            m_Casualties -= recovered;

            var tp = GameTimeSystem.Instance;
            if (tp != null)
                m_CallToArmsCooldownEndHour = tp.Current.TotalGameHours + config.CallToArmsCooldownHours;

            ApplyReputationDelta(config.CallToArmsReputation, "Call to Arms");

            m_IsDirty = true;
            Log.Info($"CALL TO ARMS: {recovered} recovered, cooldown until hour {m_CallToArmsCooldownEndHour:F1}");
            EventBus?.SafePublish(new CallToArmsEvent(recovered, m_Casualties), "MobilizationSystem");
            return true;
        }

        private void ApplyReputationDelta(float delta, string reason)
            => m_ReputationService.ModifyTrust(delta, reason);

        private float GetAverageHappinessPenalty()
        {
            if (m_PenaltySystem == null)
            {
                m_PenaltySystem = ResolvePenaltySystem();
                if (m_PenaltySystem == null)
                {
                    if (!m_LoggedMissingPenaltySystem)
                    {
                        m_LoggedMissingPenaltySystem = true;
                        Log.Warn("DistrictPenaltySystem unavailable; using neutral happiness penalty");
                    }
                    return 0f;
                }
            }
            var allPenalties = m_PenaltySystem.GetAllPenalties();
            if (allPenalties == null || allPenalties.Count == 0) return m_PenaltySystem.GetTotalPositiveHappinessPenalty(0);
            float sum = 0f;
            int positiveCount = 0;
            foreach (var kvp in allPenalties)
            {
                float positivePenalty = m_PenaltySystem.GetTotalPositiveHappinessPenalty(kvp.Key);
                if (positivePenalty <= 0f)
                    continue;

                sum += positivePenalty;
                positiveCount++;
            }

            return positiveCount > 0 ? sum / positiveCount : 0f;
        }

        private DistrictPenaltySystem? ResolvePenaltySystem()
        {
            return m_PenaltySystem ?? World.GetExistingSystemManaged<DistrictPenaltySystem>();
        }

        private void ForceReleaseExcess(int deficit)
        {
            if (deficit <= 0) return;
            int released = 0;
            m_ForceReleaseSortedScratch.Clear();
            m_ForceReleaseSortedScratch.AddRange(m_Allocations);
            var sorted = m_ForceReleaseSortedScratch;
            sorted.Sort((a, b) => a.Value.Count.CompareTo(b.Value.Count));

            EntityCommandBuffer ecb = default;
            bool hasEcb = false;

            if (sorted.Count > 0)
            {
                int requestCount = 0;
                foreach (var kvp in sorted)
                {
                    if (released >= deficit) break;

                    var key = kvp.Key;
                    int remaining = deficit - released;
                    int crewToRelease = System.Math.Min(kvp.Value.Count, remaining);
                    int newCrew = kvp.Value.Count - crewToRelease;
                    released += crewToRelease;

                    if (newCrew <= 0)
                        m_Allocations.Remove(key);
                    else
                    {
                        var entry = kvp.Value;
                        entry.Count = newCrew;
                        m_Allocations[key] = entry;
                    }

                    if (!hasEcb)
                    {
                        ecb = m_GameSimulationEndBarrier.CreateCommandBuffer();
                        hasEcb = true;
                    }

                    var requestEntity = ecb.CreateEntity();
                    ecb.AddComponent(requestEntity, new ForceCrewReleaseRequest
                    {
                        AAEntityIndex = key.EntityIndex,
                        AAEntityVersion = key.EntityVersion,
                        NewCrewCount = newCrew
                    });
                    RequestMetaWriter.AddInternal(ecb, requestEntity, nameof(ForceCrewReleaseRequest), key.EntityIndex.ToString());
                    requestCount++;
                }
                Log.Warn($"FORCE RELEASE: {released} manpower freed, {requestCount} AA crew release requests sent");
            }

            // XSTEP: force-release of excess crew decrements the SAME committed accumulator via Core
            // CrewMath — the last inline `max(0, used - released)` would re-fork the transition off-Core.
            m_UsedManpower = CrewMath.Release(m_UsedManpower, released);
            EventBus?.SafePublish(new ManpowerForceReleasedEvent(released, m_CachedBreakdown.TotalManpower), "MobilizationSystem");
        }

        private void ApplyDebugMoraleOverrideIfNeeded()
        {
            if (!m_DebugMoraleOverrideActive)
                return;

            float morale = System.Math.Clamp(m_DebugMoraleOverride, 0f, 1f);
            int rawTotal = ManpowerLogic.CalculateTotalManpower(
                m_CachedBreakdown.BasePool,
                m_CachedBreakdown.PatriotismFactor,
                morale,
                m_CachedBreakdown.FatigueFactor,
                m_CachedBreakdown.DodgerFactor,
                m_CachedBreakdown.IsConscriptionActive);
            // Reuse the authoritative breakdown's exemption heads — the debug override
            // only reshapes morale, not the Draft-Exemption scheme state.
            int beforeFloor = rawTotal
                - m_CachedBreakdown.Casualties
                - m_CachedBreakdown.DeferralManpower
                - m_CachedBreakdown.DisabilityManpower;
            // Same defence floor as the authoritative breakdown: the debug override reshapes
            // morale, not the guarantee that the free heritage guns stay crewable.
            int total = ManpowerLogic.ApplyDefenceFloor(beforeFloor, m_CachedBreakdown.BasePool);
            bool floorEngaged = total > System.Math.Max(0, beforeFloor);
            int available = System.Math.Max(0, total - m_CachedBreakdown.UsedManpower);

            m_CachedBreakdown = new ManpowerBreakdown(
                m_CachedBreakdown.Population,
                m_CachedBreakdown.BasePool,
                m_CachedBreakdown.PatriotismFactor,
                morale,
                m_CachedBreakdown.FatigueFactor,
                m_CachedBreakdown.DodgerFactor,
                m_CachedBreakdown.ConscriptionBonus,
                total,
                m_CachedBreakdown.UsedManpower,
                available,
                m_CachedBreakdown.Casualties,
                m_CachedBreakdown.DodgerCount,
                m_CachedBreakdown.DeferralManpower,
                m_CachedBreakdown.DisabilityManpower,
                m_CachedBreakdown.WarDay,
                m_CachedBreakdown.IsWarFatigued,
                m_CachedBreakdown.IsConscriptionActive,
                m_CachedBreakdown.VisibleCorruptionScore,
                m_CachedBreakdown.ExportLoadPercent,
                floorEngaged);
        }

        /// <summary>
        /// Pause-safe single-action apply, called by MobilizationRequestIntakeSystem
        /// (ModificationEnd, AXIOM 14) per request. All state touched here is managed
        /// (fields, cooldowns, reputation service, EventBus) — no SystemAPI, safe to
        /// run outside this system's OnUpdate. Request iteration/destruction stays in
        /// the intake — this system is not the request consumer.
        /// </summary>
        internal bool ApplyMobilizationActionImmediate(MobilizationActionType action, out RequestKind kind, out string failReason)
        {
            failReason = "";
            kind = action == MobilizationActionType.CallToArms
                ? RequestKind.Mobilization
                : RequestKind.ConscriptionToggle;

            bool success = true;
            switch (action)
            {
                case MobilizationActionType.ActivateConscription:
                    success = MobilizationEligibility.CanToggleConscription(
                                  reactivating: true,
                                  IsConscriptionReactivationOnCooldown,
                                  out failReason)
                              && ActivateConscription();
                    break;
                case MobilizationActionType.DeactivateConscription: DeactivateConscription(); break;
                case MobilizationActionType.CallToArms:
                    success = MobilizationEligibility.CanCallToArms(
                        true,
                        m_Casualties,
                        IsCallToArmsOnCooldown,
                        out failReason)
                        && CallToArms();
                    break;
                default: success = false; break;
            }

            if (!success && string.IsNullOrEmpty(failReason))
            {
                failReason = action == MobilizationActionType.CallToArms
                    ? ReasonIds.MobCallToArmsRejected
                    : ReasonIds.MobRejected;
            }

            return success;
        }

        private void ProcessReleaseRequests()
        {
            if (m_ReleaseRequestQuery.IsEmpty) return;
            EntityCommandBuffer ecb = default;
            bool hasEcb = false;
            foreach (var (request, entity) in SystemAPI.Query<RefRW<ManpowerReleaseRequest>>().WithEntityAccess())
            {
                if (!hasEcb)
                {
                    ecb = m_GameSimulationEndBarrier.CreateCommandBuffer();
                    hasEcb = true;
                }

                if (!request.ValueRO.DomainApplied)
                {
                    Release(request.ValueRO.Count, request.ValueRO.AATypeHash, request.ValueRO.EntityIndex, request.ValueRO.EntityVersion);
                    request.ValueRW.DomainApplied = true;
                }
                ecb.DestroyEntity(entity);
            }
        }

        private void ProcessCasualtyRequests()
        {
            if (m_CasualtyRequestQuery.IsEmpty) return;
            EntityCommandBuffer ecb = default;
            bool hasEcb = false;
            foreach (var (request, entity) in SystemAPI.Query<RefRW<CasualtyReportRequest>>().WithEntityAccess())
            {
                if (!hasEcb)
                {
                    ecb = m_GameSimulationEndBarrier.CreateCommandBuffer();
                    hasEcb = true;
                }

                if (!request.ValueRO.DomainApplied)
                {
                    ReportCasualties(request.ValueRO.Count, request.ValueRO.AATypeHash, request.ValueRO.EntityIndex, request.ValueRO.EntityVersion);
                    request.ValueRW.DomainApplied = true;
                }
                ecb.DestroyEntity(entity);
            }
        }

        private void OnWarDayChanged(WarDayChangedEvent evt)
        {
            m_LastWarDay = evt.WarDay;
            m_IsDirty = true;
        }

        /// <summary>
        /// Force a rebuild after TryRecruit/ReportCasualties changed committed crew. The threshold
        /// itself is raised at the tail of <see cref="UpdateBreakdown"/>, so every path that can
        /// leave the pool critical is covered — not just the two that happen to call this.
        /// </summary>
        private void CheckCritical() => UpdateBreakdown(true);

        /// <summary>
        /// Re-evaluate the latched critical verdict against the finished breakdown. Returns true
        /// when the verdict CHANGED — an edge the caller announces after refreshing the singleton.
        /// Nothing is published from in here on purpose: a subscriber must not observe the alert
        /// before the state mirror that describes it.
        ///
        /// <paramref name="seed"/> adopts the live verdict and reports no edge. Passed by the three
        /// rebuild-from-scratch sites so a load of an already-critical city re-arms the latch rather
        /// than re-toasting on every load — the post-load suppression CognitiveStateSystem applies
        /// to its own compromise transitions. Seeding at all three sites keeps the result
        /// independent of the order vanilla happens to run them in.
        /// </summary>
        private bool UpdateCriticalLatch(bool seed)
        {
            int available = m_CachedBreakdown.AvailableManpower;
            int total = m_CachedBreakdown.TotalManpower;
            bool now = ManpowerLogic.IsCritical(available, total, m_ManpowerCritical);

            // An empty pool is at once the worst state a city can reach and the ordinary state
            // every city starts in, and the headcount alone cannot tell a city that bled out from
            // a plot of land nobody has moved into yet. So the emptiness verdict — and only it —
            // is put to the Scenario domain before it is allowed to alarm anyone; with a single
            // head in the pool the ratio and its hysteresis decide exactly as they did before.
            //
            // The question belongs here, on the verdict, not at the rebuild: a city that really
            // did die out must go on recomputing its honest zero for the panel and the roster,
            // it must merely not be alarmed about a shortage it never had.
            bool withheldUnsettled = total <= 0 && !IsSettledCityConfirmed();
            if (withheldUnsettled)
                now = false;

            // Axiom 1: without this, "no alert" is indistinguishable from "never evaluated". Costs
            // nothing in a shipped build (level check first) and answers the question outright in a
            // repro run with Debug enabled for this context.
            if (Log.IsDebugEnabled)
                Log.Debug($"[Manpower] critical predicate available={available} total={total} was={m_ManpowerCritical} now={now} seed={seed} unsettled={withheldUnsettled}");

            if (seed)
            {
                if (now != m_ManpowerCritical)
                    Log.Info($"[Manpower] critical latch seeded to {now} (available={available} total={total}) — pre-existing state, no alert raised");
                m_ManpowerCritical = now;
                return false;
            }

            if (now == m_ManpowerCritical) return false;
            m_ManpowerCritical = now;
            return true;
        }

        /// <summary>
        /// Whether the Scenario domain confirms people have ever lived in this city
        /// (<see cref="ScenarioSingleton.CityEverSettled"/> — persisted there, and reconstructed
        /// from a save's own measurements when it predates the flag).
        ///
        /// An absent singleton is not an answer and is never folded into "never settled": in the
        /// first ticks of a session the scenario has published nothing yet. Unconfirmed keeps the
        /// alert down, and says so in the log once per episode — an alarm that stays silent because
        /// nobody was asked must not read like an alarm that stayed silent because all is well
        /// (Axiom 1).
        /// </summary>
        private bool IsSettledCityConfirmed()
        {
            if (!m_ScenarioQuery.TryGetSingleton<ScenarioSingleton>(out var scenario))
            {
                if (!m_LoggedSettlementUnknown)
                {
                    m_LoggedSettlementUnknown = true;
                    Log.Warn("[Manpower] pool is empty and no ScenarioSingleton is published yet — a city that died out cannot be told from one nobody has moved into, so the critical alert is withheld until the scenario answers");
                }
                return false;
            }

            m_LoggedSettlementUnknown = false;
            return scenario.CityEverSettled;
        }

        /// <summary>
        /// Announce a latched critical transition. No time source and no rate limit — the edge IS
        /// the event, and a city that simply stays critical is carried by the UI badge instead of
        /// being re-announced. The previous body also read GameTimeSystem and returned silently
        /// when it was not yet active, a second unlogged drop route stacked on the epoch bug.
        /// </summary>
        private void PublishCriticalTransition()
        {
            int available = m_CachedBreakdown.AvailableManpower;
            int total = m_CachedBreakdown.TotalManpower;
            // total <= 0 is the WORST case, not a case to skip. It used to be dropped here purely to
            // dodge the payload division, which made a fully collapsed pool the one state that never
            // reported — while the UI badge lit up for it. Report 0% instead.
            float percent = total > 0 ? (float)available / total : 0f;

            if (m_ManpowerCritical)
            {
                Log.Warn($"[Manpower] CRITICAL available={available} total={total} pct={percent * 100f:F1}%");
                EventBus?.SafePublish(new ManpowerCriticalEvent(available, total, percent), "MobilizationSystem");
            }
            else
            {
                Log.Info($"[Manpower] critical cleared available={available} total={total} pct={percent * 100f:F1}%");
            }
        }

        [CompletesDependency("ValidateAfterLoad: rebuild allocation map before retained Release/Casualty requests replay; CalculateEntityCount is diagnostic-only")]
        public void ValidateAfterLoad()
        {
            m_Allocations.Clear();
            int rebuiltManpower = 0;
            if (!m_AAInstallationQuery.IsEmptyIgnoreFilter)
            {
                var entities = m_AAInstallationQuery.ToEntityArray(Unity.Collections.Allocator.Temp);
                foreach (var e in entities)
                {
                    if (!EntityManager.HasComponent<AirDefenseInstallation>(e)) continue;
                    if (EntityManager.HasComponent<Deleted>(e) || EntityManager.HasComponent<Destroyed>(e)) continue;

                    var aa = EntityManager.GetComponentData<AirDefenseInstallation>(e);
                    var building = aa.GetBuildingEntity();
                    if (building == Entity.Null ||
                        !EntityManager.Exists(building) ||
                        EntityManager.HasComponent<Deleted>(building) ||
                        EntityManager.HasComponent<Destroyed>(building))
                    {
                        continue;
                    }

                    if (aa.CrewAssigned > 0)
                    {
                        var key = new AllocKey(e.Index, e.Version);
                        m_Allocations[key] = new AllocEntry((int)aa.Type, aa.CrewAssigned);
                        rebuiltManpower += aa.CrewAssigned;
                    }
                }
                if (entities.IsCreated) entities.Dispose();
            }
            if (rebuiltManpower != m_UsedManpower)
                Log.Warn($"[Mobilization] UsedManpower mismatch: saved={m_UsedManpower} rebuilt={rebuiltManpower}");
            m_UsedManpower = rebuiltManpower;
            // W2 row 166: re-resolve the singleton BEFORE the post-load breakdown
            // write, so the rebuilt values are actually stored (the stale cached
            // handle made the HasComponent guard skip the write, leaving default
            // Mobilization UI for 1+ frames).
            RestoreMobilizationSingleton();
            UpdateBreakdown(true, refreshPopulation: true, seedCriticalLatch: true);
            int pendingReleaseRequests = m_ReleaseRequestQuery.IsEmptyIgnoreFilter ? 0 : m_ReleaseRequestQuery.CalculateEntityCount();
            int pendingCasualtyRequests = m_CasualtyRequestQuery.IsEmptyIgnoreFilter ? 0 : m_CasualtyRequestQuery.CalculateEntityCount();
            // critical= makes the latch state visible on EVERY load. Its absence is what made the
            // 2026-07-25 silence unreadable: nothing in the log could tell "never critical" apart
            // from "critical but suppressed". pop=/popSource= do the same for the rebuild above:
            // when the gate holds it, the numbers printed here are the PREVIOUS breakdown, and
            // popSource says why.
            Log.Info($"ValidateAfterLoad: rebuilt used={m_UsedManpower}, available={m_CachedBreakdown.AvailableManpower}, total={m_CachedBreakdown.TotalManpower}, critical={m_ManpowerCritical}, pop={m_CachedPopulation}, popSource={SourceName(m_PopulationSource)}, aaEntities={m_AAInstallationQuery.CalculateEntityCount()}, pendingRelease={pendingReleaseRequests}, pendingCasualty={pendingCasualtyRequests}");
        }

        public void ResetState()
        {
            ResetBootDefaultsFields();
            RestoreMobilizationSingleton();
            UpdateBreakdown(true, refreshPopulation: true, seedCriticalLatch: true);
        }

        private void ResetBootDefaultsFields()
        {
            ResetPersistFields();
            m_Allocations.Clear();
            m_ForceReleaseSortedScratch.Clear();
            m_CachedBreakdown = default;
            m_CachedPopulation = 0;
            // Cleared together: the number is meaningless without the flag that says it was
            // actually answered, and the next rebuild must re-ask rather than trust a stale zero.
            m_HasPopulationMeasure = false;
            m_PopulationSource = PopulationSource.Unknown;
            m_GateHeldRebuilds = 0;
            m_PendingCriticalSeed = false;
            m_LastUpdateFrame = 0;
            m_DebugMoraleOverrideActive = false;
            m_DebugMoraleOverride = 0f;
            m_LastDodgerHour = -1f;
            m_DodgerActive = false;
            m_LastPublishedDodgerLevel = 0f;
            m_LoggedSettlementUnknown = false;
            m_Singleton.Invalidate();
            m_IsDirty = true;
        }

#if DEBUG
        public void DebugSetMoraleFactor(float value, string source)
        {
            m_DebugMoraleOverride = System.Math.Clamp(value, 0f, 1f);
            m_DebugMoraleOverrideActive = true;
            m_IsDirty = true;
            UpdateBreakdown(true);
            Log.Info($"[DEBUG] {source}: morale factor set to {m_DebugMoraleOverride:F2}");
        }

        public void DebugResetMobilization(string source)
        {
            ResetState();
            Log.Info($"[DEBUG] {source}: mobilization reset");
        }
#endif

        public void SetDefaults(Context context)
        {
            ResetState();
        }

        protected override void OnDestroy()
        {
            if (ServiceRegistry.IsInitialized)
            {
                ServiceRegistry.Instance.Unregister<IMobilizationManpowerReader>(this);
            }

#if DEBUG
            if (ServiceRegistry.IsInitialized)
            {
                ServiceRegistry.Instance.Unregister<IMobilizationDebugMutator>(this);
            }
#endif

            // Symmetric to OnCreate's SubscribeRequired: without this the EventBus
            // retains a delegate bound to this destroyed system instance across every
            // world reload, so handlers accumulate and fire on a dead EntityManager.
            UnsubscribeSafe<WarDayChangedEvent>(OnWarDayChanged);

            var singletonEntity = m_Singleton.Entity;
            if (singletonEntity != Entity.Null && EntityManager.Exists(singletonEntity))
            {
                EntityManager.DestroyEntity(singletonEntity);
                m_Singleton.Invalidate();
            }

            m_Allocations.Clear();
            base.OnDestroy();
            Log.Info("Destroyed");
        }
    }
}
