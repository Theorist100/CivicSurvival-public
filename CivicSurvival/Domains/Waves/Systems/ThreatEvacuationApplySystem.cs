using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Components.Threats;
using CivicSurvival.Core.Events;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;
using CivicSurvival.Localization;
using Game;
using Game.Common;
using Game.Events;
using Game.Simulation;
using Unity.Entities;

namespace CivicSurvival.Domains.Waves.Systems
{
    /// <summary>
    /// Applier half of the evacuation pair (planner: <see cref="ThreatEvacuationSystem"/>, which
    /// documents WHY the split exists — two vanilla gates that no single phase satisfies). This
    /// half materializes the planner's staged danger rows as <c>Event + Endanger</c> command
    /// entities plus one <see cref="EvacuationDangerOwner"/> + <c>Duration</c> owner per drain,
    /// all through vanilla <c>EndFrameBarrier</c> — the same barrier vanilla danger producers use
    /// (<c>WaterDangerSystem.cs:328</c>).
    ///
    /// Phase: PostSimulation. Both constraints hold here:
    /// - it ticks while the sim is paused (decompile <c>SimulationSystem.OnUpdate:272,295</c> —
    ///   the PostSimulation update call sits outside the speed gate), so a wave spawned into a
    ///   paused intro still raises danger;
    /// - it sits inside the <c>EndFrameBarrier</c> allow-window
    ///   (<c>AllowBarrier&lt;EndFrameBarrier&gt;</c> opens it in MainLoop after
    ///   <c>ModificationSystem</c>, <c>SystemOrder.cs:62</c>; <c>SimulationSystem</c> runs in
    ///   LateUpdate, after MainLoop), so <c>CreateCommandBuffer</c> is legal — unlike from
    ///   Modification4, where it throws (live crash 2026-07-23).
    ///
    /// Also owns the lapsed-owner sweep: vanilla drops every <c>InDanger</c> pointing at an owner
    /// whose <c>Duration</c> lapsed (<c>InDangerSystem.IsStillInDanger</c>), but nothing in
    /// vanilla removes the owner entity itself — and a mid-attack save restores owners this
    /// session never created.
    /// </summary>
    [ActIndependent]
    public partial class ThreatEvacuationApplySystem : CivicSystemBase
    {
        private static readonly LogContext Log = new("ThreatEvacuationApply");

        /// <summary>Owner sweep cadence — matches <c>InDangerSystem.UPDATE_INTERVAL</c>.</summary>
        private const uint OWNER_SWEEP_INTERVAL = 64u;

        /// <summary>
        /// Last frame the sweep ran. PostSimulation keeps ticking while the sim is paused but
        /// <c>frameIndex</c> freezes — a bare <c>frame % N == 0</c> would then re-fire every
        /// paused tick whenever the frozen frame happens to be a multiple of N.
        /// </summary>
        [System.NonSerialized] private uint m_LastSweepFrame = uint.MaxValue;

        private SimulationSystem m_SimulationSystem = null!;
        private EndFrameBarrier m_EndFrameBarrier = null!;
        // Feature-gate-safe sibling resolve (CIVIC400/403): registration-site creation only,
        // resolved in OnStartRunning with ??= (Axiom 15). Null until the planner's feature ran.
        [System.NonSerialized] private ThreatEvacuationSystem? m_Planner;

        private EntityQuery m_OwnerQuery;
        // Working shelters, for the once-per-session "no shelters" player tip.
        private EntityQuery m_ShelterQuery;
        /// <summary>Once-per-session latch for the no-shelter social-feed tip.</summary>
        [System.NonSerialized] private bool m_NoShelterTipShown;

        private EntityArchetype m_OwnerArchetype;
        private EntityArchetype m_EndangerArchetype;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_SimulationSystem = World.GetOrCreateSystemManaged<SimulationSystem>();
            m_EndFrameBarrier = World.GetOrCreateSystemManaged<EndFrameBarrier>();

            m_OwnerQuery = GetEntityQuery(
                ComponentType.ReadOnly<EvacuationDangerOwner>(),
                ComponentType.ReadOnly<Duration>(),
                ComponentType.Exclude<Deleted>());
            m_ShelterQuery = GetEntityQuery(
                ComponentType.ReadOnly<Game.Buildings.EmergencyShelter>(),
                ComponentType.Exclude<Deleted>());

            // Owner carries Duration ONLY — never Game.Common.Event, which would make
            // PrepareCleanUpSystem destroy it the same frame (see EvacuationDangerOwner docs).
            m_OwnerArchetype = EntityManager.CreateArchetype(
                ComponentType.ReadWrite<EvacuationDangerOwner>(),
                ComponentType.ReadWrite<Duration>());

            // Command-entity shape vanilla danger producers use (decompile WaterDangerSystem.cs:304).
            // Game.Common.Event here is deliberate: it is what disposes of the row after
            // EndangerSystem has read it.
            m_EndangerArchetype = EntityManager.CreateArchetype(
                ComponentType.ReadWrite<Game.Common.Event>(),
                ComponentType.ReadWrite<Endanger>());

            Log.Info("Created");
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            // Feature-gate-safe sibling resolve (CIVIC400/403): registration-site creation only.
            m_Planner ??= FeatureRegistry.Instance.Query<ThreatEvacuationSystem>();
        }

        protected override void OnUpdateImpl()
        {
            if (m_Planner is not { } planner)
                return;

            uint frame = m_SimulationSystem.frameIndex;

            // PERF-LOCK: idle early-out — no staged rows and no live owners means nothing to
            // materialize and nothing to sweep; the applier costs two cheap checks per tick.
            bool hasStaged = planner.m_StagedRows.IsCreated && planner.m_StagedRows.Length > 0;
            bool hasOwners = !m_OwnerQuery.IsEmptyIgnoreFilter;
            if (!hasStaged && !hasOwners)
                return;

            // Wave-end clamp: replace the open-time estimate with the factual wave end (+grace).
            // Direct write on our OWN owner entities (mod-owned, no render components); shrink
            // only — a later wave's fresh owner with a longer window is its own entity.
            if (planner.m_StagedOwnerClampFrame > 0)
            {
                // Consume unconditionally: a clamp with no live owners is stale (wave's danger
                // already tore down) and must not linger to bite the NEXT wave's fresh owner.
                uint clamp = planner.m_StagedOwnerClampFrame;
                planner.m_StagedOwnerClampFrame = 0;
                int clamped = 0;
                foreach (var duration in SystemAPI.Query<RefRW<Duration>>()
                             .WithAll<EvacuationDangerOwner>()
                             .WithNone<Deleted>())
                {
                    if (duration.ValueRO.m_EndFrame > clamp)
                    {
                        duration.ValueRW.m_EndFrame = clamp;
                        clamped++;
                    }
                }
                if (clamped > 0)
                    Log.Info($"Danger window clamped to wave end for {clamped} owner(s) → all-clear at frame {clamp}");
            }

            if (hasOwners && frame % OWNER_SWEEP_INTERVAL == 0 && frame != m_LastSweepFrame)
            {
                m_LastSweepFrame = frame;
                SweepLapsedOwners(frame);
            }

            if (hasStaged)
                ApplyStagedRows(planner);
        }

        /// <summary>
        /// Materialize every staged row through <c>EndFrameBarrier</c>: one shared owner entity
        /// (the drain's single teardown switch — its <c>Duration</c> covers the LAST impact, so
        /// no individual row outlives it) and one <c>Event + Endanger</c> command entity per row.
        /// The deferred owner Entity is remapped into the rows during barrier playback.
        /// </summary>
        private void ApplyStagedRows(ThreatEvacuationSystem planner)
        {
            var staged = planner.m_StagedRows;

            EntityCommandBuffer ecb = m_EndFrameBarrier.CreateCommandBuffer();

            Entity owner = ecb.CreateEntity(m_OwnerArchetype);
            ecb.SetComponent(owner, new Duration
            {
                m_StartFrame = planner.m_StagedOwnerStart,
                m_EndFrame = planner.m_StagedOwnerEnd
            });

            for (int i = 0; i < staged.Length; i++)
            {
                ThreatEvacuationSystem.StagedDanger row = staged[i];
                Entity rowEntity = ecb.CreateEntity(m_EndangerArchetype);
                ecb.SetComponent(rowEntity, new Endanger
                {
                    m_Event = owner,
                    m_Target = row.Target,
                    m_Flags = row.Flags,
                    m_EndFrame = row.EndFrame
                });
            }

            Log.Info($"Evacuation raised: {staged.Length} building(s) "
                     + $"({staged.Length - planner.m_StagedPanicCount} direct, {planner.m_StagedPanicCount} panic-ring, "
                     + $"{planner.m_StagedShelterCount} shelter-locked), "
                     + $"owner duration {planner.m_StagedOwnerStart}→{planner.m_StagedOwnerEnd}");

            staged.Clear();
            planner.m_StagedPanicCount = 0;
            planner.m_StagedShelterCount = 0;

            // Player tip, once per session: danger just went up and the city has NO working
            // shelter — the whole mechanic is invisible until the player builds one, so say it
            // in-world (social feed, city services voice) instead of letting citizens quietly
            // stay home. Query check is a handful of entities.
            if (!m_NoShelterTipShown && m_ShelterQuery.IsEmptyIgnoreFilter)
            {
                m_NoShelterTipShown = true;
                EventBus?.SafePublish(new SocialPostEvent(
                    "@city_services",
                    LocalizationManager.T("EVAC_TIP_NO_SHELTERS"),
                    SocialMood.Warning), nameof(ThreatEvacuationApplySystem));
            }
        }

        /// <summary>
        /// Mark lapsed owners <c>Deleted</c> — <c>PrepareCleanUpSystem</c> collects them and
        /// <c>CleanUpSystem</c> destroys them, the same path vanilla event entities take.
        /// </summary>
        private void SweepLapsedOwners(uint frame)
        {
            // Lazy ECB: most sweeps find nothing to delete (an owner outlives its wave by design),
            // and an ECB created per sweep would queue an empty playback every 64 frames.
            EntityCommandBuffer? ecb = null;
            int swept = 0;

            foreach (var (duration, owner) in SystemAPI.Query<RefRO<Duration>>()
                         .WithAll<EvacuationDangerOwner>()
                         .WithNone<Deleted>()
                         .WithEntityAccess())
            {
                if (frame < duration.ValueRO.m_EndFrame)
                    continue;

                ecb ??= m_EndFrameBarrier.CreateCommandBuffer();
                ecb.Value.AddComponent<Deleted>(owner);
                swept++;
            }

            if (swept > 0)
            {
                if (Log.IsDebugEnabled)
                    Log.Debug($"Swept {swept} lapsed evacuation owner(s) at frame {frame}");

                // All-clear: the wave's danger window is over. Mechanically everyone OUTSIDE is
                // already safe (casualties are drawn from citizens INSIDE a collapsing building),
                // so anyone still marching runs toward a shelter that will only release them on
                // arrival — a pointless half-hour round trip for thousands (live finding
                // 2026-07-23: half the column still en route at all-clear). Cancel the march for
                // everyone still on the way; vanilla rebuilds their normal routine from wherever
                // they stand. Citizens already INSIDE (InEmergencyShelter) are untouched — the
                // shelter releases them through its own probabilistic exit, same as vanilla
                // disasters.
                ecb ??= m_EndFrameBarrier.CreateCommandBuffer();
                CancelEnRouteMarches(ecb.Value);
            }
        }

        /// <summary>
        /// Remove the shelter TravelPurpose from every citizen still en route — the exact release
        /// mechanism the vanilla shelter itself uses for its occupants
        /// (<c>EmergencyShelterAISystem.cs:244</c>: <c>RemoveComponent&lt;TravelPurpose&gt;</c>
        /// through <c>EndFrameBarrier</c>). One full pass per all-clear, not per tick.
        /// </summary>
        private void CancelEnRouteMarches(EntityCommandBuffer ecb)
        {
            int cancelled = 0;
            // POPULATION-MEASURE-EXEMPT: a mutation pass, not a measurement. It removes the shelter
            // TravelPurpose from citizens still en route on all-clear; Citizen is the membership
            // filter, TravelPurpose is the subject. The `cancelled` tally counts COMMANDS EMITTED so
            // the log can say how many marches were called off — it is bounded by the evacuation
            // funnel, not by the city, and is never read as a population. The city's resident count
            // comes from ICityPopulationReader.
            foreach (var (purpose, entity) in SystemAPI
                         .Query<RefRO<Game.Citizens.TravelPurpose>>()
                         .WithAll<Game.Citizens.Citizen>()
                         .WithNone<Deleted>()
                         .WithEntityAccess())
            {
                if (purpose.ValueRO.m_Purpose != Game.Citizens.Purpose.EmergencyShelter)
                    continue;

                ecb.RemoveComponent<Game.Citizens.TravelPurpose>(entity);
                cancelled++;
            }

            if (cancelled > 0)
                Log.Info($"Evacuation all-clear: cancelled {cancelled} en-route march(es)");
        }
    }
}
