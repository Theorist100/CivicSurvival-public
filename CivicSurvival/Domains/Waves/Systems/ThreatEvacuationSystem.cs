using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Components.Threats;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Events;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Interfaces.Threats;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;
using Game.Common;
using Game.Events;
using Game.Prefabs;
using Game.Simulation;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace CivicSurvival.Domains.Waves.Systems
{
    /// <summary>
    /// Raises vanilla evacuation danger on the buildings inbound drones are aimed at, so the
    /// game's own shelter stack (<c>CitizenEvacuateSystem</c> → <c>Purpose.EmergencyShelter</c>
    /// → <c>EmergencyShelterAISystem</c>) reacts to mod strikes. Without this, the only producers
    /// of <c>InDanger</c> are <c>WeatherPhenomenonSystem</c> / <c>WaterDangerSystem</c>, so
    /// emergency shelters are decorative against the mod's core threat.
    ///
    /// Two danger tiers per drone:
    /// - the TARGET building (the one that will actually collapse — casualties come only from
    ///   <c>ThreatDamageSystem.DestroyBuilding</c>, and a Shahed never collapses neighbours: its
    ///   area pass uses <c>destructionThreshold = float.MaxValue</c>, fire only);
    /// - a PANIC ring: residential buildings within <c>Waves.EvacuationPanicRadius</c> of the
    ///   predicted impact point also evacuate. That ring saves no lives mechanically (those
    ///   buildings do not collapse) — it exists because people next to an incoming strike run
    ///   anyway, and it is what puts crowds on the street during a raid. Residential only:
    ///   panicking a hospital or power plant would cost service function for zero safety gain.
    ///   The ring uses the same full residential catalogue the targeting path reads
    ///   (<see cref="IThreatTargetSource.Civilian"/>, force-refreshed by the producer right
    ///   before every wave) — no spatial index, one linear pass per wave.
    ///
    /// Ballistic missiles are deliberately excluded. They DO collapse neighbours, but at
    /// <c>Threats.BallisticSpeed</c> the flight from the map edge lasts ~20-30 s — no citizen
    /// reaches a shelter in that window, so raising danger would only spend pathfinds. Missiles
    /// stay a threat that only air defense answers.
    ///
    /// Mechanism (all teardown is vanilla's):
    /// - one <see cref="EvacuationDangerOwner"/> + <c>Duration</c> entity per wave;
    /// - one short-lived <c>Event + Endanger</c> command entity per target, pointing at that owner;
    /// - vanilla <c>EndangerSystem</c> (Modification4) dedupes per target and applies
    ///   <c>InDanger</c>; <c>InDangerSystem</c> removes it once the owner's Duration lapses.
    /// This system never writes <c>InDanger</c> itself — no co-writing of vanilla components.
    ///
    /// Split into planner + applier, and BOTH halves are phase-load-bearing:
    ///
    /// - THIS system (planner) runs in Modification4, BEFORE <c>ThreatSpawnApplySystem</c> — the
    ///   consumer drains and clears the intent buffer, and Modification4 ticks while paused (the
    ///   intro wave can spawn into a paused sim). A GameSimulation reader misses the buffer
    ///   whenever the sim is paused on the spawn frame (silent-wave incident 2026-07-23: waves at
    ///   02:38/02:57 raised no danger while 02:01/03:05 worked). It emits NO commands — it only
    ///   stages the wave's danger rows into <see cref="m_StagedRows"/>.
    /// - <c>ThreatEvacuationApplySystem</c> (PostSimulation — ticks while paused, decompile
    ///   <c>SimulationSystem.OnUpdate:272,295</c>) drains the staged rows and materializes them
    ///   through vanilla <c>EndFrameBarrier</c>. The split exists because of TWO vanilla gates
    ///   that no single phase satisfies:
    ///   (a) <c>EndFrameBarrier</c> is a <c>SafeCommandBufferSystem</c>: <c>CreateCommandBuffer</c>
    ///       throws outside its allow-window, which <c>AllowBarrier&lt;EndFrameBarrier&gt;</c>
    ///       opens in MainLoop AFTER <c>ModificationSystem</c> (<c>SystemOrder.cs:59-62</c>) — so
    ///       Modification4 can never write to it (live crash 2026-07-23);
    ///   (b) an Event-tagged row written through <c>ModificationBarrier4</c> materializes
    ///       mid-frame and is destroyed by the SAME frame's cleanup sweep before
    ///       <c>EndangerSystem</c> ever reads it (observed live: 56 raised, 0 InDanger) —
    ///       vanilla danger producers all write through <c>EndFrameBarrier</c>
    ///       (<c>WaterDangerSystem.cs:328</c>), whose playback at next-MainLoop start gives the
    ///       row exactly one readable frame.
    /// </summary>
    [ActIndependent]
    public partial class ThreatEvacuationSystem : CivicSystemBase, IPostLoadValidation, IEvacuationDebriefingSource
    {
        private static readonly LogContext Log = new("ThreatEvacuationSystem");

        /// <summary>
        /// Simulation frames per second — the same conversion vanilla danger producers use when
        /// turning a lead time in seconds into <c>Endanger.m_EndFrame</c> (decompile
        /// <c>WaterDangerSystem.cs:97</c>: <c>m_SimulationFrame + 64 + (uint)(num * 60f)</c>).
        /// </summary>
        private const float SIM_FRAMES_PER_SECOND = 60f;

        /// <summary>
        /// Vanilla's own settling margin on danger end-frames (<c>+ 64</c> in both danger
        /// producers): <c>InDangerSystem</c> runs on a 64-frame interval, so an end-frame closer
        /// than one interval can be cleared before it is ever acted on.
        /// </summary>
        private const uint DANGER_INTERVAL_MARGIN = 64u;

        private SimulationSystem m_SimulationSystem = null!;

        private EntityQuery m_IntentQuery;

        private ComponentLookup<PrefabRef> m_PrefabRefLookup;
        private ComponentLookup<Game.Buildings.Building> m_BuildingLookup;
        private ComponentLookup<Game.Buildings.EmergencyShelter> m_ShelterLookup;
        private ComponentLookup<Destroyed> m_DestroyedLookup;
        private ComponentLookup<Deleted> m_DeletedLookup;
        // Wave-end debriefing snapshot lookup.
        private ComponentLookup<Game.Citizens.TravelPurpose> m_TravelPurposeLookup;

        /// <summary>
        /// Wave batch keys already turned into danger rows. The intent buffer is drained by
        /// <c>ThreatSpawnApplySystem</c> in the NEXT Modification4, so this producer can see the
        /// same wave on more than one GameSimulation tick; without the key set it would emit a
        /// duplicate <c>Endanger</c> row per target per tick. Process-lifetime only (the buffer is
        /// stripped on save/load and the producer's batch sequence restarts at 1), cleared in
        /// <see cref="ValidateAfterLoad"/>.
        /// </summary>
        [System.NonSerialized] private NativeHashSet<uint> m_RaisedBatchKeys;

        /// <summary>
        /// Full residential catalogue for the panic ring — the same cached list the targeting path
        /// reads (<c>ThreatTargetCacheSystem</c>), force-refreshed by the producer right before
        /// every wave, so it is never staler than the targets themselves. Process-lifetime service
        /// handle (<c>??=</c> in OnStartRunning) — not save state.
        /// </summary>
        [System.NonSerialized] private IThreatTargetSource? m_TargetSource;

        /// <summary>
        /// Producer sibling, for the Alert-time strike preset (danger raised at the SIREN — the
        /// citizens get the whole Alert window plus flight time to reach a shelter). Feature-gate-
        /// safe resolve (CIVIC400/403) in OnStartRunning.
        /// </summary>
        [System.NonSerialized] private ThreatSpawnSystem? m_SpawnSystem;

        /// <summary>
        /// Last wave staged from the Alert preset. Guards both re-staging the preset on later
        /// ticks AND re-staging the same targets from the spawn intents when the drones launch
        /// (the preset guarantees intents fly to the pre-warned buildings). Process-lifetime,
        /// cleared on load with the batch keys.
        /// </summary>
        [System.NonSerialized] private int m_PreAlertStagedWave = -1;

        /// <summary>
        /// One staged danger row: the applier turns each into an <c>Event + Endanger</c> command
        /// entity pointing at the drain's shared owner. Runtime-only handoff between the two
        /// halves of this system pair — never serialized (cleared on load with the batch keys).
        /// </summary>
        internal struct StagedDanger
        {
            public Entity Target;
            public uint EndFrame;
            public DangerFlags Flags;
        }

        /// <summary>
        /// Staged rows awaiting materialization by <c>ThreatEvacuationApplySystem</c> (same
        /// frame's PostSimulation). Owner window: the applier stamps ONE owner entity per drain
        /// with <c>Duration
        /// { m_StartFrame = StagedOwnerStart, m_EndFrame = StagedOwnerEnd }</c>.
        /// </summary>
        [System.NonSerialized] internal NativeList<StagedDanger> m_StagedRows;
        [System.NonSerialized] internal uint m_StagedOwnerStart;
        [System.NonSerialized] internal uint m_StagedOwnerEnd;
        /// <summary>Panic-ring / shelter-locked share of the staged rows, for the applier's log line.</summary>
        [System.NonSerialized] internal int m_StagedPanicCount;
        [System.NonSerialized] internal int m_StagedShelterCount;

        /// <summary>
        /// Staged owner-window clamp, set by <see cref="OnWaveEnded"/>: the wave's danger window
        /// was OPENED on an estimate (siren remainder + worst-case flight — generous by design),
        /// but the actual end of the wave is a FACT the game announces via <c>WaveEndedEvent</c>.
        /// The applier rewrites every live owner's <c>Duration.m_EndFrame</c> down to this frame
        /// (wave end + grace), so the all-clear — and the en-route march cancellation — follow the
        /// REAL wave, not the estimate (live finding 2026-07-23 19:17: game already in Calm,
        /// danger still up for 10 more sim-minutes). 0 = no clamp pending.
        /// </summary>
        [System.NonSerialized] internal uint m_StagedOwnerClampFrame;

        /// <summary>
        /// Wave-end evacuation snapshot (<see cref="IEvacuationDebriefingSource"/>): written only
        /// in <see cref="OnWaveEnded"/>, read by ThreatUI via ServiceRegistry when building the
        /// debriefing DTO — the modal shows the state at wave end.
        /// </summary>
        public int LastWaveEvacSheltered { get; private set; }
        public int LastWaveEvacEnRoute { get; private set; }

        protected override void OnCreate()
        {
            base.OnCreate();

            m_SimulationSystem = World.GetOrCreateSystemManaged<SimulationSystem>();

            m_IntentQuery = GetEntityQuery(ComponentType.ReadOnly<ThreatSpawnIntent>());

            m_PrefabRefLookup = GetComponentLookup<PrefabRef>(isReadOnly: true);
            m_BuildingLookup = GetComponentLookup<Game.Buildings.Building>(isReadOnly: true);
            m_ShelterLookup = GetComponentLookup<Game.Buildings.EmergencyShelter>(isReadOnly: true);
            m_DestroyedLookup = GetComponentLookup<Destroyed>(isReadOnly: true);
            m_DeletedLookup = GetComponentLookup<Deleted>(isReadOnly: true);
            m_TravelPurposeLookup = GetComponentLookup<Game.Citizens.TravelPurpose>(isReadOnly: true);

            m_RaisedBatchKeys = new NativeHashSet<uint>(8, Allocator.Persistent);
            m_StagedRows = new NativeList<StagedDanger>(64, Allocator.Persistent);

            // Wave end = the factual last impact. The danger window is OPENED on an estimate
            // (siren remainder + worst-case flight); this event replaces the estimate with fact.
            SubscribeRequired<WaveEndedEvent>(OnWaveEnded);

            // Debriefing snapshot source (Axiom 5: ThreatUI reads via the Core interface).
            if (ServiceRegistry.IsInitialized)
                ServiceRegistry.Instance.Register<IEvacuationDebriefingSource>(this);

            Log.Info("Created");
        }

        /// <summary>
        /// Publisher-context handler (WaveExecutor's GameSimulation tick) — field write only; the
        /// applier does the actual owner rewrite in its own PostSimulation tick.
        /// </summary>
        private void OnWaveEnded(WaveEndedEvent evt)
        {
            float graceSeconds = BalanceConfig.Current.Waves.EvacuationDangerGraceSeconds;
            uint graceFrames = (uint)math.max(0f, graceSeconds * SIM_FRAMES_PER_SECOND);
            m_StagedOwnerClampFrame = m_SimulationSystem.frameIndex + graceFrames;

            // Snapshot is taken on OUR next tick (CIVIC281: this handler runs in the publisher's
            // system context, so SystemAPI here would query through WaveExecutor's state).
            m_WaveEndCensusPending = true;

            Log.Info($"Wave #{evt.WaveNumber} ended — danger window clamped to frame {m_StagedOwnerClampFrame} "
                     + $"(+{graceSeconds:F0}s grace)");
        }

        /// <summary>Deferred wave-end snapshot request (see <see cref="OnWaveEnded"/>).</summary>
        [System.NonSerialized] private bool m_WaveEndCensusPending;

        /// <summary>
        /// Debriefing snapshot: who made it into a shelter by wave end, who is still running.
        /// One pass over citizens with an active TravelPurpose — once per wave, on the first
        /// planner tick after the wave ends (frames after the event; the modal opens later).
        /// </summary>
        private void TakeWaveEndCensus()
        {
            m_TravelPurposeLookup.Update(this);
            int sheltered = 0, enRoute = 0;
            // POPULATION-MEASURE-EXEMPT: this counts people by EVACUATION STATE, not the size of
            // the city. The subject is TravelPurpose; Citizen is only the membership filter that
            // keeps the query off vehicles and animals, and the two tallies below are partitions
            // of the evacuation funnel (in a shelter / still marching), never a total — households
            // and citizens with no travel purpose are outside both. Neither number is comparable
            // to the city's population and neither is published as one; they feed the wave-end
            // debriefing modal. The city's resident count comes from ICityPopulationReader.
            foreach (var purpose in SystemAPI
                         .Query<RefRO<Game.Citizens.TravelPurpose>>()
                         .WithAll<Game.Citizens.Citizen>()
                         .WithNone<Deleted>())
            {
                switch (purpose.ValueRO.m_Purpose)
                {
                    case Game.Citizens.Purpose.InEmergencyShelter: sheltered++; break;
                    case Game.Citizens.Purpose.EmergencyShelter: enRoute++; break;
                    default: break; // outside the evacuation funnel
                }
            }
            LastWaveEvacSheltered = sheltered;
            LastWaveEvacEnRoute = enRoute;
            Log.Info($"Evacuation at wave end: sheltered={sheltered}, enRoute={enRoute}");
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            // Same resolve pattern as ThreatSpawnSystem: the cache system registers the source in
            // its OnCreate, which runs before any system starts (Axiom 15 timing).
            m_TargetSource ??= ServiceRegistry.Instance.Require<IThreatTargetSource>();

            // Feature-gate-safe sibling resolve (CIVIC400/403): registration-site creation only.
            m_SpawnSystem ??= FeatureRegistry.Instance.Query<ThreatSpawnSystem>();
        }

        protected override void OnDestroy()
        {
            UnsubscribeSafe<WaveEndedEvent>(OnWaveEnded);
            if (ServiceRegistry.IsInitialized)
                ServiceRegistry.Instance.Unregister<IEvacuationDebriefingSource>(this);
            if (m_RaisedBatchKeys.IsCreated)
                m_RaisedBatchKeys.Dispose();
            if (m_StagedRows.IsCreated)
                m_StagedRows.Dispose();
            base.OnDestroy();
        }

        /// <summary>
        /// A loaded save restarts the producer's batch sequence at 1, so keys from the previous
        /// session would collide and silently swallow the first waves' danger. Staged rows are
        /// runtime-only handoff — a save in the stage→apply window re-issues the wave through the
        /// WaveExecutor resume path, same as the intent buffer itself.
        /// </summary>
        public void ValidateAfterLoad()
        {
            if (m_RaisedBatchKeys.IsCreated)
                m_RaisedBatchKeys.Clear();
            if (m_StagedRows.IsCreated)
                m_StagedRows.Clear();
            m_PreAlertStagedWave = -1;
            m_StagedOwnerClampFrame = 0;
        }

        protected override void OnUpdateImpl()
        {
            uint frame = m_SimulationSystem.frameIndex;

            // Deferred wave-end snapshot (requested by OnWaveEnded, taken in OUR context).
            if (m_WaveEndCensusPending)
            {
                m_WaveEndCensusPending = false;
                TakeWaveEndCensus();
            }

            // Single config snapshot per tick (CIVIC347 — torn-read guard on hot-reload).
            float graceSeconds = BalanceConfig.Current.Waves.EvacuationDangerGraceSeconds;

            // Alert-time preset: the producer pre-selects the wave's strike targets at the SIREN
            // (minutes before spawn) — stage danger for them immediately so citizens get the
            // whole Alert window plus flight time to reach a shelter. One-shot per wave.
            if (m_SpawnSystem is { } sp
                && sp.m_PreselectedWaveNumber >= 0
                && sp.m_PreselectedWaveNumber != m_PreAlertStagedWave
                && sp.m_PreselectedStrikes.IsCreated
                && sp.m_PreselectedStrikes.Length > 0)
            {
                StageDangerFromPreselect(sp, frame, graceSeconds);
                m_PreAlertStagedWave = sp.m_PreselectedWaveNumber;
            }

            // PERF-LOCK: calm-phase early-out. Outside a spawn tick the intent buffer is length 0,
            // so the planner costs one buffer length read per tick. Everything below allocates or
            // touches lookups — keep it behind this gate. (Owner sweep lives on the applier.)
            bool hasIntents = m_IntentQuery.TryGetSingletonBuffer<ThreatSpawnIntent>(out var intents, isReadOnly: true)
                              && intents.Length > 0;
            if (!hasIntents)
                return;

            StageDangerForNewWaves(intents, frame, graceSeconds);
        }

        /// <summary>
        /// Stage danger from the producer's Alert-time strike preset: direct targets + panic ring
        /// around every predicted impact point, all with the wave's conservative last-impact
        /// estimate as the end-frame (generous by design — too long only delays teardown; too
        /// short would release shelters before the real impacts).
        /// </summary>
        private void StageDangerFromPreselect(ThreatSpawnSystem sp, uint frame, float graceSeconds)
        {
            m_PrefabRefLookup.Update(this);
            m_BuildingLookup.Update(this);
            m_ShelterLookup.Update(this);
            m_DestroyedLookup.Update(this);
            m_DeletedLookup.Update(this);

            uint graceFrames = (uint)math.max(0f, graceSeconds * SIM_FRAMES_PER_SECOND);
            uint endFrame = sp.m_PreselectedImpactFrameEstimate + graceFrames;

            var strikes = sp.m_PreselectedStrikes;
            var targets = new NativeList<Entity>(strikes.Length, Allocator.Temp);
            var endFrames = new NativeList<uint>(strikes.Length, Allocator.Temp);
            var seen = new NativeHashSet<int>(strikes.Length, Allocator.Temp);
            var panicCenters = new NativeList<float3>(strikes.Length, Allocator.Temp);
            var panicEnds = new NativeList<uint>(strikes.Length, Allocator.Temp);

            try
            {
                for (int i = 0; i < strikes.Length; i++)
                {
                    ThreatSpawnSystem.PreselectedStrike strike = strikes[i];

                    // Every strike panics its neighbourhood; only building strikes also get the
                    // direct-target danger (a ground strike destroys nothing).
                    panicCenters.Add(strike.TargetPos);
                    panicEnds.Add(endFrame);

                    if (strike.TargetIndex < 0)
                        continue;

                    var building = new Entity { Index = strike.TargetIndex, Version = strike.TargetVersion };
                    if (!IsEndangerable(building) || !seen.Add(building.Index))
                        continue;

                    targets.Add(building);
                    endFrames.Add(endFrame);
                }

                int panicAdded = AddPanicRing(panicCenters, panicEnds, targets, endFrames, seen);

                if (targets.Length == 0)
                    return;

                StageRows(targets, endFrames, frame, endFrame, panicAdded, "at Alert siren");
            }
            finally
            {
                if (targets.IsCreated) targets.Dispose();
                if (endFrames.IsCreated) endFrames.Dispose();
                if (seen.IsCreated) seen.Dispose();
                if (panicCenters.IsCreated) panicCenters.Dispose();
                if (panicEnds.IsCreated) panicEnds.Dispose();
            }
        }

        /// <summary>
        /// Stage one danger row per distinct drone target of every batch not staged yet. The
        /// applier materializes the rows through <c>EndFrameBarrier</c> in this frame's
        /// PostSimulation and points them all at a single per-drain owner.
        /// </summary>
        private void StageDangerForNewWaves(DynamicBuffer<ThreatSpawnIntent> intents, uint frame, float graceSeconds)
        {
            m_PrefabRefLookup.Update(this);
            m_BuildingLookup.Update(this);
            m_ShelterLookup.Update(this);
            m_DestroyedLookup.Update(this);
            m_DeletedLookup.Update(this);

            uint graceFrames = (uint)math.max(0f, graceSeconds * SIM_FRAMES_PER_SECOND);

            // Collected first so the owner's Duration can cover the LAST impact of the wave: the
            // owner is the single teardown switch, and a Duration shorter than an individual
            // Endanger's end-frame would clear that danger early (IsStillInDanger checks both).
            var targets = new NativeList<Entity>(intents.Length, Allocator.Temp);
            var endFrames = new NativeList<uint>(intents.Length, Allocator.Temp);
            var seen = new NativeHashSet<int>(intents.Length, Allocator.Temp);
            var keys = new NativeList<uint>(4, Allocator.Temp);
            // Panic-ring inputs: predicted impact point + danger end of every inbound drone of the
            // new batches, INCLUDING ground strikes — a near miss panics the street even though it
            // collapses nothing.
            var panicCenters = new NativeList<float3>(intents.Length, Allocator.Temp);
            var panicEnds = new NativeList<uint>(intents.Length, Allocator.Temp);

            try
            {
                for (int i = 0; i < intents.Length; i++)
                {
                    ThreatSpawnIntent intent = intents[i];

                    if (intent.WaveBatchKey == 0 || m_RaisedBatchKeys.Contains(intent.WaveBatchKey))
                        continue;

                    if (!keys.Contains(intent.WaveBatchKey))
                        keys.Add(intent.WaveBatchKey);

                    // Drones only (Kind 0), inbound only. Ballistic flight is 20-30 s — nobody
                    // reaches a shelter, so no danger and no panic for missiles.
                    if (intent.Kind != 0) continue;
                    if (intent.Faction != ThreatSpawnIntent.FactionEnemyInbound) continue;

                    // Wave already staged from the Alert preset: the spawn flies to exactly the
                    // pre-warned buildings (SpawnWave consumes the preset), so re-staging here
                    // would only duplicate Endanger rows. Vanilla would dedupe the InDanger, but
                    // hundreds of redundant Event entities per wave are avoidable noise.
                    if (intent.WaveNumber == m_PreAlertStagedWave) continue;

                    uint endFrame = frame + DANGER_INTERVAL_MARGIN + FlightFrames(intent) + graceFrames;

                    // Every inbound drone panics its neighbourhood; only building strikes also get
                    // the direct-target danger below.
                    panicCenters.Add(intent.TargetPos);
                    panicEnds.Add(endFrame);

                    // A ground strike (TargetBuildingIndex < 0) destroys nothing and kills nobody —
                    // no direct target to warn, panic ring only.
                    if (intent.TargetBuildingIndex < 0) continue;

                    var building = new Entity { Index = intent.TargetBuildingIndex, Version = intent.TargetBuildingVersion };
                    if (!IsEndangerable(building)) continue;
                    if (!seen.Add(building.Index)) continue;

                    targets.Add(building);
                    endFrames.Add(endFrame);
                }

                for (int k = 0; k < keys.Length; k++)
                    m_RaisedBatchKeys.Add(keys[k]);

                int panicAdded = AddPanicRing(panicCenters, panicEnds, targets, endFrames, seen);

                if (targets.Length == 0)
                    return;

                uint ownerEndFrame = 0;
                for (int i = 0; i < endFrames.Length; i++)
                    ownerEndFrame = math.max(ownerEndFrame, endFrames[i]);

                StageRows(targets, endFrames, frame, ownerEndFrame, panicAdded, "at spawn");
            }
            finally
            {
                if (targets.IsCreated) targets.Dispose();
                if (endFrames.IsCreated) endFrames.Dispose();
                if (seen.IsCreated) seen.Dispose();
                if (keys.IsCreated) keys.Dispose();
                if (panicCenters.IsCreated) panicCenters.Dispose();
                if (panicEnds.IsCreated) panicEnds.Dispose();
            }
        }

        /// <summary>
        /// Common staging tail for both entry points (Alert preset / spawn intents): shared
        /// per-drain owner window, per-target shelter downgrade (vanilla danger producers
        /// downgrade Evacuate → StayIndoors for buildings that ARE shelters at the producer,
        /// decompile <c>WaterDangerSystem.cs:77-81</c> — <c>EndangerSystem</c> does NOT do it
        /// for us), and the staging log. A second wave staged before the previous drain widens
        /// the shared owner window instead of adding an owner — same teardown semantics.
        /// </summary>
        private void StageRows(
            NativeList<Entity> targets,
            NativeList<uint> endFrames,
            uint frame,
            uint ownerEndFrame,
            int panicAdded,
            string sourceTag)
        {
            if (m_StagedRows.Length == 0)
            {
                m_StagedOwnerStart = frame;
                m_StagedOwnerEnd = ownerEndFrame;
                m_StagedPanicCount = 0;
                m_StagedShelterCount = 0;
            }
            else
            {
                m_StagedOwnerEnd = math.max(m_StagedOwnerEnd, ownerEndFrame);
            }

            int shelters = 0;
            for (int i = 0; i < targets.Length; i++)
            {
                Entity target = targets[i];

                DangerFlags flags = DangerFlags.Evacuate;
                if (m_ShelterLookup.HasComponent(target))
                {
                    flags = DangerFlags.StayIndoors;
                    shelters++;
                }

                m_StagedRows.Add(new StagedDanger
                {
                    Target = target,
                    EndFrame = endFrames[i],
                    Flags = flags
                });
            }

            m_StagedPanicCount += panicAdded;
            m_StagedShelterCount += shelters;

            Log.Info($"Evacuation staged {sourceTag}: {targets.Length} building(s) "
                     + $"({targets.Length - panicAdded} direct, {panicAdded} panic-ring, {shelters} shelter-locked), "
                     + $"owner window {frame}→{ownerEndFrame}");
        }

        /// <summary>
        /// Appends residential buildings within <c>Waves.EvacuationPanicRadius</c> of any impact
        /// point to the danger list. One linear pass over the full residential catalogue
        /// (<see cref="IThreatTargetSource.Civilian"/>) per wave — a few thousand distance checks,
        /// no spatial index. A building near several drones stays endangered until the LAST of
        /// them lands (max end-frame). Returns how many buildings the ring added.
        /// </summary>
        private int AddPanicRing(
            NativeList<float3> panicCenters,
            NativeList<uint> panicEnds,
            NativeList<Entity> targets,
            NativeList<uint> endFrames,
            NativeHashSet<int> seen)
        {
            float radius = BalanceConfig.Current.Waves.EvacuationPanicRadius;
            if (panicCenters.Length == 0 || radius <= 0f || m_TargetSource is not { IsReady: true })
                return 0;

            float radiusSq = radius * radius;
            var civilian = m_TargetSource.Civilian;
            int added = 0;

            for (int c = 0; c < civilian.Length; c++)
            {
                TargetData row = civilian[c];

                uint bestEnd = 0;
                for (int p = 0; p < panicCenters.Length; p++)
                {
                    if (math.distancesq(row.Position.xz, panicCenters[p].xz) <= radiusSq)
                        bestEnd = math.max(bestEnd, panicEnds[p]);
                }
                if (bestEnd == 0)
                    continue;

                // Same liveness gate as direct targets; seen-dedup also keeps a residential
                // building that IS a drone target on its direct-target end-frame.
                if (!IsEndangerable(row.Entity) || !seen.Add(row.Entity.Index))
                    continue;

                targets.Add(row.Entity);
                endFrames.Add(bestEnd);
                added++;
            }

            return added;
        }

        /// <summary>
        /// Flight time of this drone converted to simulation frames — the warning window the
        /// player's citizens actually get (map-edge spawn, so minutes rather than seconds).
        /// </summary>
        private static uint FlightFrames(ThreatSpawnIntent intent)
        {
            if (intent.Speed <= 0f || intent.TotalDistance <= 0f)
                return 0u;
            return (uint)math.max(0f, intent.TotalDistance / intent.Speed * SIM_FRAMES_PER_SECOND);
        }

        /// <summary>
        /// A target worth warning: a live building vanilla will accept. <c>EndangerSystem</c> skips
        /// anything without <c>PrefabRef</c>, and citizens react through <c>CurrentBuilding</c>, so
        /// a non-building target would raise danger nobody can read.
        /// </summary>
        private bool IsEndangerable(Entity building)
        {
            return building != Entity.Null
                   && m_PrefabRefLookup.HasComponent(building)
                   && m_BuildingLookup.HasComponent(building)
                   && !m_DestroyedLookup.HasComponent(building)
                   && !m_DeletedLookup.HasComponent(building);
        }

    }
}
