#if DEBUG
using System;
using CivicSurvival.Core.Features.Wellbeing;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Jobs;
using Unity.Burst;
using Colossal.Logging;
using Colossal.UI.Binding;
using UnityEngine;
using Game;
using Game.Common;
using Game.Simulation;
using CivicSurvival.Core.Utils;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Events;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Diagnostics;
using CivicSurvival.Core.Components.Threats;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Components.Domain.GridWarfare;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Systems;
using CivicSurvival.Core.Systems.Scheduling;
using CivicSurvival.Core.Adapters;
using CivicSurvival.Core.Services;
using CivicSurvival.Domains.Countermeasures.Systems;
using CivicSurvival.Domains.Corruption.Systems;
using CivicSurvival.Domains.ShadowEconomy.Systems;
using CivicSurvival.Core.Features.CrossDomain.DamageAccounting;
using CivicSurvival.Core.Systems.Effects;
using CivicSurvival.Core.Systems.Requests;
using CivicSurvival.Services.Arena;
using CivicSurvival.Services.UI;
using CivicSurvival.Domains.AirDefense.Systems;
using CivicSurvival.Domains.Intel.Systems;
using CivicSurvival.Domains.Spotters.Systems;
using CivicSurvival.Domains.Attention.Systems;
using CivicSurvival.Domains.Blackout.Systems;
using CivicSurvival.Domains.Cognitive.Core.Systems;
using CivicSurvival.Domains.Cognitive.Ops.Systems;
using CivicSurvival.Domains.Cognitive.Threats.Systems;
using CivicSurvival.Domains.Diplomacy.Systems;
using CivicSurvival.Domains.Economics.Systems;
using CivicSurvival.Domains.Engineering.Systems;
using CivicSurvival.Domains.Finance.Systems;
using CivicSurvival.Domains.GridWarfare.Systems;
using CivicSurvival.Domains.Mobilization.Systems;
using CivicSurvival.Domains.Narrative.Systems;
using CivicSurvival.Domains.NeighborEnvy.Systems;
using CivicSurvival.Domains.Network.Systems;
using CivicSurvival.Domains.Notifications.Systems;
using CivicSurvival.Domains.PowerBackup.Systems;
using CivicSurvival.Domains.PowerGrid.Systems;
using CivicSurvival.Domains.Refugees.Systems;
using CivicSurvival.Domains.Scenario.Systems;
using CivicSurvival.Domains.ThreatFlight.Systems;
using CivicSurvival.Domains.ThreatDamage.Systems;
using CivicSurvival.Domains.Waves.Systems;
using CivicSurvival.Domains.ThreatUI.Systems;
using CivicSurvival.Domains.ThreatUI.Audio;
using CivicSurvival.Domains.ThreatUI.UI;
using CivicSurvival.Domains.Tutorial.Systems;
using CivicSurvival.Core.UI;
using static CivicSurvival.Core.UI.B;
using CivicSurvival.Core.Attributes;

namespace CivicSurvival.Services.DevTools
{
    /// <summary>
    /// DEBUG ONLY: Threat spawning controls and system toggle for performance debugging.
    /// </summary>
    [ActIndependent]
    public partial class ThreatDebugSystem : TriggeredThrottledUISystemBase
    {
        private static readonly LogContext Log = new("ThreatDebug");

        protected override int UpdateInterval => 60;

        // Deferred action flags — trigger callbacks set these,
        // OnThrottledUpdate processes them within the ECS update loop
        // where all job dependencies are properly resolved.
        // Direct EntityManager calls from trigger callbacks crash vanilla Burst jobs
        // because CompleteDependency forces completion of jobs with stale chunk data.
        private volatile bool m_PendingDestroyAll;
        private volatile bool m_PendingExplode;
        private volatile bool m_PendingSetDusk;
        private volatile bool m_PendingSetMidnight;
        private volatile bool m_PendingWaveDusk;
        private volatile bool m_PendingForceCrash;
        private volatile bool m_PendingGlitchToggle;
        private volatile bool m_PendingTestTracer;
        private volatile bool m_PendingKillCiviliansNative;
        private volatile bool m_PendingDemolish1Building;
        private volatile bool m_PendingDemolishToggle;

        // Building-demolish harness: reproduce the wave-crash trigger that a sterile drone
        // spawn never hits. Each demolish removes a building from the building query →
        // order-version bump → ThreatDamage/ThreatMovement spatial grid+cache rebuild and
        // swap, racing an in-flight Burst reader. Goes through the SAME path production uses
        // on a hit (BuildingDamageHelper.TryDestroyBuilding → vanilla Destroy event), not a
        // direct EntityManager.DestroyEntity. While the toggle is active, one random live
        // building is demolished per throttled tick.
        private bool m_DemolishToggleActive;
        private EntityQuery m_DemolishBuildingQuery;
        private EntityArchetype m_DestroyEventArchetype;
        private ComponentLookup<Game.Common.Destroyed> m_DemolishDestroyedLookup;
        private ComponentLookup<Game.Common.Deleted> m_DemolishDeletedLookup;
        private IFrameMutationDedup m_FrameMutationDedup = null!;
        private bool m_DemolishPausedLogged;

        // Glitch-drone harness: while active, one live drone gets its avoidance waypoint
        // force-flipped to the opposite side every throttled tick — a faithful replay of the
        // pre-fix even/odd pipeline split (drone banks left, target jumps right, net progress
        // ~0, visibly twitching in place). Used to eyeball the movement watchdog and camera
        // behaviour against a guaranteed-stuck drone without waiting for one to occur.
        private bool m_GlitchDroneActive;
        private bool m_GlitchFlip;
        private bool m_GlitchSpawnRequested;
        private Entity m_GlitchDrone = default(Entity);
        private ComponentLookup<Shahed> m_GlitchShahedLookup;
        private ComponentLookup<ThreatPosition> m_GlitchPositionLookup;
        private ComponentLookup<ActiveThreat> m_GlitchActiveThreatLookup;
        private ComponentLookup<ThreatFlightProgress> m_GlitchFlightProgressLookup;
        private ITerrainHeightReader m_TerrainHeightReader = null!;
        private readonly object m_DebugSpawnLock = new();
        private readonly DebugSpawnCommand[] m_PendingDebugSpawns = new DebugSpawnCommand[16];
        private int m_DebugSpawnHead;
        private int m_DebugSpawnTail;
        private int m_DebugSpawnCount;

        // Queries for WaveDusk
        private EntityQuery m_TimeDataDebugQuery;
        private EntityQuery m_PowerGridDebugQuery;
        private EntityQuery m_EnemyStateDebugQuery;

        private IPlanetaryClockReader m_PlanetaryClock = null!;
        private IThreatLifecycleDedup m_ThreatLifecycleDedup = null!;
        private SimulationSystem m_SimulationSystem = null!;
        private ThreatLifecycleBarrier m_ThreatLifecycleBarrier = null!;
        private bool m_DestroyAllPausedLogged;
        private bool m_KillCiviliansNativePausedLogged;

        private ProfiledBinding<string> m_ABStatus = null!;
        private ProfiledBinding<string> m_ABProgress = null!;

        private readonly struct DebugSpawnCommand
        {
            public DebugSpawnCommand(int threatCount, int waveNumber, WaveType waveType, int ballisticOverride, string label)
            {
                ThreatCount = threatCount;
                WaveNumber = waveNumber;
                WaveType = waveType;
                BallisticOverride = ballisticOverride;
                Label = label;
            }

            public int ThreatCount { get; }
            public int WaveNumber { get; }
            public WaveType WaveType { get; }
            public int BallisticOverride { get; }
            public string Label { get; }
        }

        protected override void ConfigureTriggers(TriggerRegistry triggers)
        {
            triggers.AddUngated(DebugDestroyAllDrones, OnDebugDestroyAllDrones);
            triggers.AddUngated(DebugSetDusk, OnDebugSetDusk);
            triggers.AddUngated(DebugSetMidnight, OnDebugSetMidnight);
            triggers.AddUngated(DebugSpawnWaveDusk, OnDebugSpawnWaveDusk);
            triggers.AddUngated(DebugSpawn1Drone, OnDebugSpawn1Drone);
            triggers.AddUngated(DebugSpawn25Drones, OnDebugSpawn25Drones);
            triggers.AddUngated(DebugSpawn1Ballistic, OnDebugSpawn1Ballistic);
            triggers.AddUngated(DebugSpawn10Ballistics, OnDebugSpawn10Ballistics);
            triggers.AddUngated(DebugTestTracer, OnDebugTestTracer);
            triggers.AddUngated(DebugExplodeDrone, OnDebugExplodeDrone);
            triggers.AddUngated(DebugGlitchDrone, OnDebugGlitchDrone);
            triggers.AddUngated(DebugForceCrash, OnDebugForceCrash);
            triggers.AddUngated(DebugKill500CiviliansNative, OnDebugKill500CiviliansNative);
            triggers.AddUngated(DebugDemolish1Building, OnDebugDemolish1Building);
            triggers.AddUngated(DebugDemolishBuildingsToggle, OnDebugDemolishBuildingsToggle);
        }

        protected override void OnCreate()
        {
            base.OnCreate();

            m_ABStatus = new ProfiledBinding<string>(Group, Debug_ABTestStatus, "");
            m_ABProgress = new ProfiledBinding<string>(Group, Debug_ABTestProgress, "");
            AddBinding(m_ABStatus.Binding);
            AddBinding(m_ABProgress.Binding);

            m_TimeDataDebugQuery = GetEntityQuery(ComponentType.ReadWrite<TimeData>());
            m_PowerGridDebugQuery = GetEntityQuery(ComponentType.ReadOnly<PowerGridSingleton>());
            m_EnemyStateDebugQuery = GetEntityQuery(ComponentType.ReadOnly<EnemyState>());
            m_GlitchShahedLookup = GetComponentLookup<Shahed>(false);
            m_GlitchPositionLookup = GetComponentLookup<ThreatPosition>(false);
            m_GlitchActiveThreatLookup = GetComponentLookup<ActiveThreat>(true);
            m_GlitchFlightProgressLookup = GetComponentLookup<ThreatFlightProgress>(false);
            m_SimulationSystem = World.GetOrCreateSystemManaged<SimulationSystem>();
            m_ThreatLifecycleBarrier = World.GetOrCreateSystemManaged<ThreatLifecycleBarrier>();

            // Mirror ThreatDamageSystem's building query + destroy-event archetype so the
            // demolish buttons walk the exact production destruction path.
            m_DemolishBuildingQuery = GetEntityQuery(
                ComponentType.ReadOnly<Game.Buildings.Building>(),
                ComponentType.Exclude<Game.Common.Deleted>(),
                ComponentType.Exclude<Game.Common.Destroyed>());
            m_DestroyEventArchetype = EntityManager.CreateArchetype(
                ComponentType.ReadWrite<Game.Common.Event>(),
                ComponentType.ReadWrite<Game.Objects.Destroy>());
            m_DemolishDestroyedLookup = GetComponentLookup<Game.Common.Destroyed>(true);
            m_DemolishDeletedLookup = GetComponentLookup<Game.Common.Deleted>(true);
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            m_PlanetaryClock = ServiceRegistry.Instance.Require<IPlanetaryClockReader>();
            m_ThreatLifecycleDedup = ServiceRegistry.Instance.Require<IThreatLifecycleDedup>();
            m_TerrainHeightReader = ServiceRegistry.Instance.Require<ITerrainHeightReader>();
            m_FrameMutationDedup = ServiceRegistry.Instance.Require<IFrameMutationDedup>();
        }

        protected override void OnThrottledUpdate()
        {
            // Process deferred actions (safe: within ECS update loop, dependencies resolved)
            if (m_PendingDestroyAll)
            {
                m_PendingDestroyAll = false;
                ExecuteDestroyAll();
            }

            if (m_PendingExplode)
            {
                m_PendingExplode = false;
                ExecuteExplodeDrone();
            }

            if (m_PendingSetDusk)
            {
                m_PendingSetDusk = false;
                ExecuteSetDusk();
            }

            if (m_PendingSetMidnight)
            {
                m_PendingSetMidnight = false;
                ExecuteSetMidnight();
            }

            if (m_PendingWaveDusk)
            {
                m_PendingWaveDusk = false;
                ExecuteSpawnScaledWave();
            }

            if (m_PendingForceCrash)
            {
                m_PendingForceCrash = false;
                ExecuteForceCrash();
            }

            if (m_PendingTestTracer)
            {
                m_PendingTestTracer = false;
                ExecuteTestTracer();
            }

            if (m_PendingKillCiviliansNative)
            {
                m_PendingKillCiviliansNative = false;
                ExecuteKill500CiviliansNative();
            }

            if (m_PendingDemolish1Building)
            {
                m_PendingDemolish1Building = false;
                ExecuteDemolishBuildings(1);
            }

            if (m_PendingDemolishToggle)
            {
                m_PendingDemolishToggle = false;
                m_DemolishToggleActive = !m_DemolishToggleActive;
                Log.Info($"[DEBUG] Demolish buildings (continuous) {(m_DemolishToggleActive ? "ON" : "OFF")}");
            }

            if (m_DemolishToggleActive)
                ExecuteDemolishBuildings(1);

            while (TryDequeueDebugSpawn(out var spawn))
            {
                ExecuteDebugSpawn(spawn);
            }

            if (m_PendingGlitchToggle)
            {
                m_PendingGlitchToggle = false;
                m_GlitchDroneActive = !m_GlitchDroneActive;
                if (!m_GlitchDroneActive)
                    ReleaseGlitchDrone();
                Log.Info($"[DEBUG] Glitch drone {(m_GlitchDroneActive ? "ON" : "OFF")}");
            }

            if (m_GlitchDroneActive)
                ExecuteGlitchDroneTick();

            // Poll A/B test state — update UI binding
            if (PerformanceProfiler.ABTestRunning)
            {
                if (m_ABStatus.Value != DebugABTestUiState.Status)
                    m_ABStatus.Update(DebugABTestUiState.Status);
                m_ABProgress.Update(PerformanceProfiler.GetABProgressText());
            }
            else if (m_ABStatus.Value.Length > 0)
            {
                m_ABStatus.Update("");
                m_ABProgress.Update("");
            }
        }

        private void OnDebugDestroyAllDrones() => m_PendingDestroyAll = true;
        private void OnDebugExplodeDrone() => m_PendingExplode = true;
        private void OnDebugGlitchDrone() => m_PendingGlitchToggle = true;
        private void OnDebugForceCrash() => m_PendingForceCrash = true;
        private void OnDebugTestTracer() => m_PendingTestTracer = true;
        private void OnDebugKill500CiviliansNative() => m_PendingKillCiviliansNative = true;
        private void OnDebugDemolish1Building() => m_PendingDemolish1Building = true;
        private void OnDebugDemolishBuildingsToggle() => m_PendingDemolishToggle = true;

        // DEBUG: fire a synthetic AA burst ~40m in front of the camera, straight up, so tracers
        // are spawned in-frame for a visibility check independent of real combat (where they fly
        // far away at 800-1100 m/s and are hard to catch). Goes through the real spawn→render
        // pipeline (AAFireEvent → TracerSpawnSystem → TracerRenderSystem). Gepard = 5 tracers.
        private void ExecuteTestTracer()
        {
            try
            {
                if (World == null || !World.IsCreated) return;

                var cam = UnityEngine.Camera.main;
                if (cam == null)
                {
                    Log.Warn("[DEBUG] Test tracer skipped: no camera");
                    return;
                }

#pragma warning disable CIVIC169 // Debug method: independent TryGet per button click
                var eventBus = ServiceRegistry.TryGet<IEventBus>();
#pragma warning restore CIVIC169
                if (eventBus == null) return;

                var camPos = cam.transform.position;
                var fwd = cam.transform.forward;
                float3 aaPos = new float3(camPos.x + fwd.x * 40f, 0f, camPos.z + fwd.z * 40f);
                float ground = m_TerrainHeightReader.TrySampleHeight(aaPos, out var h) ? h : 0f;
                aaPos.y = math.max(0f, ground) + 5f;
                float3 threatPos = aaPos + new float3(0f, 500f, 0f);

                eventBus.SafePublish(new AAFireEvent(aaPos, threatPos, AAType.Gepard, -1), "ThreatDebugSystem");
                Log.Info($"[DEBUG] Test tracer fired in front of camera at ({aaPos.x:F0},{aaPos.y:F0},{aaPos.z:F0}) → up");
            }
            catch (Exception ex) { Log.Warn($"[DEBUG] ExecuteTestTracer failed: {ex}"); }
        }

#pragma warning disable CIVIC006 // Debug tool: direct Shahed data writes from the throttled update (deferred from trigger)
        /// <summary>
        /// One throttled tick of the glitch harness: latch onto a live drone (re-latching if
        /// the previous one died) and force its avoidance waypoint to alternate sides, so the
        /// drone visibly twitches in place. The long cooldown blocks the movement job's own
        /// replans/clears from fighting the forced state between our ticks.
        /// </summary>
        private void ExecuteGlitchDroneTick()
        {
            try
            {
                if (World == null || !World.IsCreated) return;

                m_GlitchShahedLookup.Update(this);
                m_GlitchPositionLookup.Update(this);
                m_GlitchActiveThreatLookup.Update(this);
                m_GlitchFlightProgressLookup.Update(this);

                if (m_GlitchDrone == default(Entity)
                    || !m_GlitchShahedLookup.HasComponent(m_GlitchDrone)
                    || !m_GlitchPositionLookup.HasComponent(m_GlitchDrone)
                    || !m_GlitchActiveThreatLookup.IsComponentEnabled(m_GlitchDrone))
                {
                    m_GlitchDrone = default(Entity);
                    foreach (var (_, entity) in SystemAPI.Query<RefRO<Shahed>>()
                        .WithAll<ActiveThreat, ThreatPosition>().WithEntityAccess())
                    {
                        m_GlitchDrone = entity;
                        break;
                    }
                    if (m_GlitchDrone == default(Entity))
                    {
                        // Empty sky (e.g. right after Destroy All) — spawn our own target once
                        // and latch onto it on a later tick. The flag keeps the request single
                        // even though the spawn pipeline needs several ticks to deliver.
                        if (!m_GlitchSpawnRequested)
                        {
                            m_GlitchSpawnRequested = true;
                            QueueDebugSpawn(new DebugSpawnCommand(
                                threatCount: 1,
                                waveNumber: 94,
                                waveType: WaveType.MassiveStrike,
                                ballisticOverride: -1,
                                label: "[DEBUG] Glitch drone: no live drones — spawning one (wave #94)"));
                        }
                        return;
                    }
                    m_GlitchSpawnRequested = false;
                    TeleportGlitchDroneToCamera();
                    Log.Info($"[DEBUG] Glitch drone latched onto entity={m_GlitchDrone.Index}");
                }

                var shahed = m_GlitchShahedLookup[m_GlitchDrone];
                float3 pos = m_GlitchPositionLookup[m_GlitchDrone].Position;

                float3 toTarget = shahed.TargetPosition - pos;
                toTarget.y = 0f;
                float3 dir = math.normalizesafe(toTarget, new float3(0f, 0f, 1f));
                float3 perp = math.cross(math.up(), dir);   // horizontal perpendicular to the target bearing

                m_GlitchFlip = !m_GlitchFlip;
                float3 wp = pos + perp * (m_GlitchFlip ? 250f : -250f);
                wp.y = pos.y;

                shahed.IsAvoiding = true;
                shahed.AvoidanceWaypoint = wp;
                shahed.AvoidanceObstacle = default;
                shahed.PreviousAvoidanceObstacle = default;
                shahed.AvoidanceCooldown = 30f;
                m_GlitchShahedLookup[m_GlitchDrone] = shahed;
            }
            catch (Exception ex) { Log.Warn($"[DEBUG] ExecuteGlitchDroneTick failed: {ex}"); }
        }

        /// <summary>
        /// Pull the latched drone into view: ~400 m ahead of the camera, above terrain.
        /// The checkpoint follows the teleport so the stuck detector measures from the
        /// new spot instead of instantly flagging a multi-km "jump without progress".
        /// </summary>
        private void TeleportGlitchDroneToCamera()
        {
            var cam = UnityEngine.Camera.main;
            if (cam == null) return;               // keep the original position — camera unavailable
            if (!m_GlitchPositionLookup.HasComponent(m_GlitchDrone)
                || !m_GlitchShahedLookup.HasComponent(m_GlitchDrone))
                return;

            var camPos = cam.transform.position;
            var fwd = cam.transform.forward;
            float3 pos = new float3(camPos.x + fwd.x * 400f, 0f, camPos.z + fwd.z * 400f);
            float ground = m_TerrainHeightReader.TrySampleHeight(pos, out var h) ? h : 0f;
            pos.y = math.max(ground + 150f, camPos.y);

            var threatPos = m_GlitchPositionLookup[m_GlitchDrone];
            threatPos.Position = pos;
            threatPos.Velocity = float3.zero;
            m_GlitchPositionLookup[m_GlitchDrone] = threatPos;

            var shahed = m_GlitchShahedLookup[m_GlitchDrone];
            shahed.LastCheckpointPos = pos;
            shahed.TimeSinceCheckpoint = 0f;
            m_GlitchShahedLookup[m_GlitchDrone] = shahed;

            Log.Info($"[DEBUG] Glitch drone teleported to camera at ({pos.x:F0},{pos.y:F0},{pos.z:F0})");
        }

        /// <summary>
        /// Drop the forced avoidance state so the drone resumes its normal flight. Both
        /// stuck-watchdog channels are re-seeded: they are gated off while IsAvoiding is
        /// forced, so without the reset their stale pre-glitch state would mark the drone
        /// exhausted within seconds of release (seen on entity 68568, 2026-06-10 22:16).
        /// </summary>
        private void ReleaseGlitchDrone()
        {
            try
            {
                m_GlitchShahedLookup.Update(this);
                m_GlitchFlightProgressLookup.Update(this);
                if (m_GlitchDrone != default(Entity)
                    && World != null && World.IsCreated)
                {
                    if (m_GlitchShahedLookup.HasComponent(m_GlitchDrone))
                    {
                        var shahed = m_GlitchShahedLookup[m_GlitchDrone];
                        shahed.IsAvoiding = false;
                        shahed.AvoidanceWaypoint = float3.zero;
                        shahed.AvoidanceObstacle = default;
                        shahed.PreviousAvoidanceObstacle = default;
                        shahed.AvoidanceCooldown = 0f;
                        shahed.TimeSinceCheckpoint = 0f;
                        shahed.LastCheckpointPos = float3.zero;   // job re-seeds from current position
                        m_GlitchShahedLookup[m_GlitchDrone] = shahed;
                        if (m_GlitchFlightProgressLookup.HasComponent(m_GlitchDrone))
                        {
                            // MaxValue: the next UpdateAndCheckStuck call re-baselines min-distance.
                            m_GlitchFlightProgressLookup[m_GlitchDrone] = new ThreatFlightProgress
                            {
                                MinDistanceToTarget = float.MaxValue,
                                MinDistanceTime = 0
                            };
                        }
                        Log.Info($"[DEBUG] Glitch drone released entity={m_GlitchDrone.Index}");
                    }
                }
            }
            catch (Exception ex) { Log.Warn($"[DEBUG] ReleaseGlitchDrone failed: {ex}"); }
            m_GlitchDrone = default(Entity);
            m_GlitchSpawnRequested = false;
        }
#pragma warning restore CIVIC006

        // DEBUG-only: deliberately kills the process with a native access violation
        // inside a Burst job — the same crash class as real field crashes
        // (c0000005 in lib_burst_generated). Writes the breadcrumb marker first so a
        // next-launch TelemetryCrashDetector.TryConsume can prove the crash pipeline
        // records native crashes. Dereferences a null pointer passed through a field
        // so Burst cannot fold it away statically.
        #if ENABLE_BURST
        [BurstCompile]
        #endif
        private unsafe struct ForceNativeCrashJob : IJob
        {
            public System.IntPtr Target;

            public void Execute()
            {
                *(int*)Target = unchecked((int)0xDEADBEEF);
            }
        }

        private void ExecuteForceCrash()
        {
            Log.Warn("[DEBUG] Force native crash requested — writing breadcrumb and dereferencing null in a Burst job. The process will terminate now.");
            NativeCrashBreadcrumb.Mark(NativeCrashMarkers.DevForcedCrash);
            // Run() executes the Burst-compiled job synchronously on this thread, so the
            // access violation lands in lib_burst_generated (the real crash class) without
            // a Schedule/Complete sync point. This is a deliberate process kill, not a query job.
            new ForceNativeCrashJob { Target = System.IntPtr.Zero }.Run();
        }
        private void OnDebugSetDusk() => m_PendingSetDusk = true;
        private void OnDebugSetMidnight() => m_PendingSetMidnight = true;
        private void OnDebugSpawnWaveDusk() => m_PendingWaveDusk = true;

        private void ExecuteDestroyAll()
        {
            try
            {
                if (World == null || !World.IsCreated) return;
                if (m_SimulationSystem.selectedSpeed <= 0f)
                {
                    m_PendingDestroyAll = true;
                    if (!m_DestroyAllPausedLogged)
                    {
                        Log.Warn("[DEBUG] Destroy-all deferred until simulation resumes");
                        m_DestroyAllPausedLogged = true;
                    }
                    return;
                }
                m_DestroyAllPausedLogged = false;

                EntityCommandBuffer ecb = default;
                bool hasEcb = false;
                int shahedCount = 0;
                int ballisticCount = 0;

#pragma warning disable CIVIC343 // Debug teardown selects currently live threats; enabled PendingDestruction means already queued.
                foreach (var (_, entity) in SystemAPI.Query<RefRO<Shahed>>().WithAll<ActiveThreat>().WithNone<PendingDestruction, Deleted>().WithEntityAccess())
#pragma warning restore CIVIC343
                {
                    if (!m_ThreatLifecycleDedup.TryQueueDeleted(entity))
                        continue;

                    if (!hasEcb) { ecb = m_ThreatLifecycleBarrier.CreateCommandBuffer(); hasEcb = true; }
                    // PERF-LOCK: ActiveThreat disable is deferred because DroneRenderWriteJob can still own render-facing threat chunks this frame.
                    ecb.SetComponentEnabled<ActiveThreat>(entity, false);
                    ecb.SetComponentEnabled<PendingDestruction>(entity, true);
                    // Render-safe deletion signal (same as ThreatTerminalizationSystem): the
                    // structural Deleted add is done by ThreatDeletionApplySystem in Modification4.
                    ecb.SetComponentEnabled<PendingThreatDeletion>(entity, true);
                    shahedCount++;
                }

#pragma warning disable CIVIC343 // Debug teardown selects currently live threats; enabled PendingDestruction means already queued.
                foreach (var (_, entity) in SystemAPI.Query<RefRO<Ballistic>>().WithAll<ActiveThreat>().WithNone<PendingDestruction, Deleted>().WithEntityAccess())
#pragma warning restore CIVIC343
                {
                    if (!m_ThreatLifecycleDedup.TryQueueDeleted(entity))
                        continue;

                    if (!hasEcb) { ecb = m_ThreatLifecycleBarrier.CreateCommandBuffer(); hasEcb = true; }
                    // PERF-LOCK: ActiveThreat disable is deferred because DroneRenderWriteJob can still own render-facing threat chunks this frame.
                    ecb.SetComponentEnabled<ActiveThreat>(entity, false);
                    ecb.SetComponentEnabled<PendingDestruction>(entity, true);
                    // Render-safe deletion signal (same as ThreatTerminalizationSystem): the
                    // structural Deleted add is done by ThreatDeletionApplySystem in Modification4.
                    ecb.SetComponentEnabled<PendingThreatDeletion>(entity, true);
                    ballisticCount++;
                }

                if (hasEcb)
                    m_ThreatLifecycleBarrier.AddJobHandleForProducer(Dependency);

                Log.Info($"[DEBUG] Marked {shahedCount} Shaheds + {ballisticCount} Ballistics for cleanup");
            }
            catch (Exception ex) { Log.Warn($"[DEBUG] ExecuteDestroyAll failed: {ex}"); }
        }

        // DEBUG: kill up to 500 real vanilla citizens the same route the casualty
        // path now uses (ThreatDamageSystem.ReportCasualties): mark HealthProblem{Dead|RequireTransport}
        // instead of a structural Deleted. Vanilla deathcare then requests a hearse and applies the
        // Deleted itself on EndFrameBarrier (MainLoop), so NO Deleted+UpdateFrame citizen is exposed
        // during GameSimulation — this does NOT reproduce the UpdateGroupSystem mis-phase trigger
        // (unlike ExecuteKill500Civilians). Use it to exercise the deathcare casualty path on demand.
        private void ExecuteKill500CiviliansNative()
        {
            try
            {
                if (World == null || !World.IsCreated) return;
                // HealthProblem lands via the GameSimulation-window barrier (mirrors the casualty
                // producer, which is pause-gated); that phase does not tick while paused, so defer.
                if (m_SimulationSystem.selectedSpeed <= 0f)
                {
                    m_PendingKillCiviliansNative = true;
                    if (!m_KillCiviliansNativePausedLogged)
                    {
                        Log.Warn("[DEBUG] Kill-civilians (native) deferred until simulation resumes");
                        m_KillCiviliansNativePausedLogged = true;
                    }
                    return;
                }
                m_KillCiviliansNativePausedLogged = false;

                const int target = 500;
                EntityCommandBuffer ecb = default;
                bool hasEcb = false;
                int killed = 0;
                foreach (var (_, entity) in SystemAPI.Query<RefRO<Game.Citizens.Citizen>>()
                             .WithNone<Deleted, Game.Citizens.HealthProblem>().WithEntityAccess())
                {
                    if (!hasEcb) { ecb = m_ThreatLifecycleBarrier.CreateCommandBuffer(); hasEcb = true; }
                    var deathProblem = new Game.Citizens.HealthProblem(
                        Unity.Entities.Entity.Null,
                        Game.Citizens.HealthProblemFlags.Dead | Game.Citizens.HealthProblemFlags.RequireTransport);
                    ecb.AddComponent<Game.Citizens.HealthProblem>(entity, deathProblem);
                    if (++killed >= target) break;
                }

                Log.Info($"[DEBUG] Killed {killed} civilians (HealthProblem Dead+RequireTransport → vanilla deathcare) — does NOT trigger UpdateGroupSystem mis-phase");
            }
            catch (Exception ex) { Log.Warn($"[DEBUG] ExecuteKill500CiviliansNative failed: {ex}"); }
        }

        // DEBUG: demolish `count` random live buildings via the SAME path production uses on a
        // drone/ballistic hit — BuildingDamageHelper.TryDestroyBuilding emits a vanilla Destroy
        // event (DestroySystem applies Destroyed + strips producer/consumer), NOT a direct
        // EntityManager.DestroyEntity. Each demolish drops a building from the building query →
        // order-version bump → ThreatDamage/ThreatMovement spatial grid + cache rebuild and swap,
        // exercising the suspected wave-crash race (swap under a live Burst reader) that a sterile
        // drone spawn never triggers because nothing changes the building set.
        [CompletesDependency("ExecuteDemolishBuildings: debug tool fired from DevTools UI (one-shot / throttled toggle). ToEntityArray reads the building set to pick a random target; not on any hot update path")]
        private void ExecuteDemolishBuildings(int count)
        {
            try
            {
                if (World == null || !World.IsCreated) return;
                // The destroy path lands on a GameSimulation-window barrier that does not tick
                // while paused; defer so the button is not silently dropped on pause.
                if (m_SimulationSystem.selectedSpeed <= 0f)
                {
                    if (!m_DemolishPausedLogged)
                    {
                        Log.Warn("[DEBUG] Demolish buildings deferred until simulation resumes");
                        m_DemolishPausedLogged = true;
                    }
                    return;
                }
                m_DemolishPausedLogged = false;

                m_DemolishDestroyedLookup.Update(this);
                m_DemolishDeletedLookup.Update(this);

                var buildings = m_DemolishBuildingQuery.ToEntityArray(Unity.Collections.Allocator.Temp);
                try
                {
                    if (buildings.Length == 0)
                    {
                        Log.Info("[DEBUG] Demolish: no live buildings");
                        return;
                    }

                    // Vary the seed per call (frameCount) so successive ticks hit different
                    // buildings; +1 avoids the zero seed Unity.Mathematics.Random rejects.
                    var rng = new Unity.Mathematics.Random((uint)UnityEngine.Time.frameCount * 747796405u + 1u);
                    EntityCommandBuffer ecb = default;
                    bool hasEcb = false;
                    int demolished = 0;
                    int attempts = math.min(count, buildings.Length);
                    for (int i = 0; i < attempts; i++)
                    {
                        Entity building = buildings[rng.NextInt(0, buildings.Length)];

                        if (!hasEcb) { ecb = m_ThreatLifecycleBarrier.CreateCommandBuffer(); hasEcb = true; }
                        // Position only feeds the VFX/casualty event location, irrelevant to the
                        // grid-swap repro, so pass zero instead of a render-ticketed Transform read.
                        // Self-guards (cross-frame Destroyed/Deleted + same-frame dedup) live in the
                        // helper, so a repeat random pick is safely skipped.
                        if (BuildingDamageHelper.TryDestroyBuilding(
                                ecb, building, float3.zero, m_FrameMutationDedup,
                                m_DemolishDestroyedLookup, m_DemolishDeletedLookup,
                                m_DestroyEventArchetype, isCritical: false, isPowerPlant: false))
                            demolished++;
                    }

                    if (hasEcb)
                        m_ThreatLifecycleBarrier.AddJobHandleForProducer(Dependency);

                    if (demolished > 0)
                        Log.Info($"[DEBUG] Demolished {demolished} building(s) via production Destroy path (of {buildings.Length} live)");
                }
                finally
                {
                    if (buildings.IsCreated) buildings.Dispose();
                }
            }
            catch (Exception ex) { Log.Warn($"[DEBUG] ExecuteDemolishBuildings failed: {ex}"); }
        }

#pragma warning disable CIVIC006 // Debug tool: EntityManager.SetComponentData to advance time (deferred to OnUpdate)
        private void ExecuteSetDusk()
        {
            try
            {
                if (World == null || !World.IsCreated) return;

#pragma warning disable CIVIC051 // Debug tool: EntityManager.SetComponentData intentional (same as vanilla DebugAdvanceTime)
                if (!m_PlanetaryClock.TryGetClock(out int dayOfYear, out float currentHour))
                {
                    Log.Warn("[DEBUG] SetDusk skipped: planetary clock unavailable");
                    return;
                }

                if (m_TimeDataDebugQuery.TryGetSingleton<TimeData>(out var timeData))
                {
                    var timeEntity = m_TimeDataDebugQuery.GetSingletonEntity();

                    // Approximate sunset hour at 51°N (CS2 default latitude)
                    // from current day of year. ±15min accuracy — good enough for debug.
                    float sunsetHour = 18.75f + 2.25f * math.sin(2f * math.PI * (dayOfYear - 80) / 365f);
                    float hoursToAdvance = (sunsetHour - currentHour + GameRate.HOURS_PER_DAY) % GameRate.HOURS_PER_DAY;

                    int ticksToAdvance = CalculateForwardTicks(hoursToAdvance);
                    timeData.m_FirstFrame = SubtractTicksClamped(timeData.m_FirstFrame, ticksToAdvance);
                    EntityManager.SetComponentData(timeEntity, timeData);
#pragma warning restore CIVIC051
                    Log.Info($"[DEBUG] Time → sunset ~{sunsetHour:F1}h (day {dayOfYear}, was {currentHour:F1}h, +{hoursToAdvance:F1}h)");
                }
                else
                {
                    Log.Warn("[DEBUG] SetDusk: TimeData not available");
                }
            }
            catch (Exception ex) { Log.Warn($"[DEBUG] ExecuteSetDusk failed: {ex}"); }
        }
#pragma warning restore CIVIC006

#pragma warning disable CIVIC006 // Debug tool: EntityManager.SetComponentData to advance time (deferred to OnUpdate)
        private void ExecuteSetMidnight()
        {
            try
            {
                if (World == null || !World.IsCreated) return;

#pragma warning disable CIVIC051 // Debug tool: EntityManager.SetComponentData intentional (same as vanilla DebugAdvanceTime)
                if (!m_PlanetaryClock.TryGetClock(out int dayOfYear, out float currentHour))
                {
                    Log.Warn("[DEBUG] SetMidnight skipped: planetary clock unavailable");
                    return;
                }

                if (m_TimeDataDebugQuery.TryGetSingleton<TimeData>(out var timeData))
                {
                    var timeEntity = m_TimeDataDebugQuery.GetSingletonEntity();

                    // Deep night — 02:00, fully dark, max contrast for emissive/tracer review.
                    const float midnightHour = 2f;
                    float hoursToAdvance = (midnightHour - currentHour + GameRate.HOURS_PER_DAY) % GameRate.HOURS_PER_DAY;

                    int ticksToAdvance = CalculateForwardTicks(hoursToAdvance);
                    timeData.m_FirstFrame = SubtractTicksClamped(timeData.m_FirstFrame, ticksToAdvance);
                    EntityManager.SetComponentData(timeEntity, timeData);
#pragma warning restore CIVIC051
                    Log.Info($"[DEBUG] Time → midnight {midnightHour:F1}h (day {dayOfYear}, was {currentHour:F1}h, +{hoursToAdvance:F1}h)");
                }
                else
                {
                    Log.Warn("[DEBUG] SetMidnight: TimeData not available");
                }
            }
            catch (Exception ex) { Log.Warn($"[DEBUG] ExecuteSetMidnight failed: {ex}"); }
        }
#pragma warning restore CIVIC006

        private void ExecuteSpawnScaledWave()
        {
            try
            {
                if (World == null || !World.IsCreated) return;

#pragma warning disable CIVIC169 // Debug method: independent TryGet per button click
                var eventBus = ServiceRegistry.TryGet<IEventBus>();
#pragma warning restore CIVIC169
                if (eventBus == null) return;

                bool powerGridReady = m_PowerGridDebugQuery.TryGetSingleton<PowerGridSingleton>(out var powerGrid);
                if (!powerGridReady)
                    powerGrid = default;
                bool enemyStateReady = m_EnemyStateDebugQuery.TryGetSingleton<EnemyState>(out var enemyState);
                if (!enemyStateReady)
                    enemyState = default;

#pragma warning disable CIVIC169 // Debug method: independent TryGet per button click
                var climate = ServiceRegistry.TryGet<ClimateState>();
                var modSettings = ServiceRegistry.TryGet<ModSettings>();
#pragma warning restore CIVIC169

                // Debug-кнопка: surplus-надбавка не нужна (nameplateKW/peakDemandKW=дефолт 0 → база ProductionMW).
                var ctx = WaveContextGatherer.Gather(powerGrid, enemyState, climate, modSettings, powerGridReady: powerGridReady, enemyStateReady: enemyStateReady);
                var random = new Unity.Mathematics.Random((uint)UnityEngine.Time.frameCount + 0x9999);
                int threatCount = WaveScalingService.CalculateThreatCount(ctx, WaveType.MassiveStrike, 99, ref random);

                eventBus.SafePublish(
                    new SpawnWaveRequestEvent(threatCount, WaveNumber: 99, WaveType.MassiveStrike),
                    "ThreatDebugSystem"
                );
                Log.Info($"[DEBUG] Scaled wave: {threatCount} drones ({ctx.CitySizeMW}MW city)");
            }
            catch (Exception ex) { Log.Warn($"[DEBUG] ExecuteSpawnScaledWave failed: {ex}"); }
        }

        [CompletesDependency("ExecuteExplodeDrone: debug tool fired from DevTools UI (one-shot user action). ToComponentDataArray reads thread positions for single-frame VFX play; not on any update path")]
        private void ExecuteExplodeDrone()
        {
            try
            {
                if (World == null || !World.IsCreated) return;

                var vfx = World.GetExistingSystemManaged<VanillaVfxSystem>();
                if (vfx == null || !vfx.IsReady)
                {
                    Log.Warn("[DEBUG] VanillaVfxSystem not ready");
                    return;
                }

                var query = EntityManager.CreateEntityQuery(
                    ComponentType.ReadOnly<Shahed>(),
                    ComponentType.ReadOnly<ThreatPosition>(),
                    ComponentType.ReadOnly<ActiveThreat>());

                if (query.IsEmpty)
                {
                    Log.Info("[DEBUG] No active drones to explode");
                    return;
                }

                var positions = query.ToComponentDataArray<ThreatPosition>(Unity.Collections.Allocator.Temp);
                float3 pos;
                try
                {
                    if (positions.Length == 0)
                    {
                        Log.Info("[DEBUG] No active drones to explode");
                        return;
                    }

                    pos = positions[0].Position;
                }
                finally
                {
                    if (positions.IsCreated) positions.Dispose();
                }

                vfx.RequestExplosion(pos, ExplosionType.DirectHit);

                Log.Info($"[DEBUG] Explosion VFX at drone pos ({pos.x:F0},{pos.y:F0},{pos.z:F0})");
            }
            catch (Exception ex) { Log.Warn($"[DEBUG] ExecuteExplodeDrone failed: {ex}"); }
        }

        private static int CalculateForwardTicks(float hoursToAdvance)
        {
            if (!math.isfinite(hoursToAdvance) || hoursToAdvance <= 0f)
                return 0;

            return math.max(0, Mathf.RoundToInt(hoursToAdvance / GameRate.HOURS_PER_DAY * TimeSystem.kTicksPerDay));
        }

        private static uint SubtractTicksClamped(uint firstFrame, int ticksToAdvance)
        {
            if (ticksToAdvance <= 0)
                return firstFrame;

            long shifted = (long)firstFrame - ticksToAdvance;
            return shifted <= 0L ? 0u : (uint)shifted;
        }

        private void QueueDebugSpawn(DebugSpawnCommand command)
        {
            lock (m_DebugSpawnLock)
            {
                if (m_DebugSpawnCount == m_PendingDebugSpawns.Length)
                {
                    m_PendingDebugSpawns[m_DebugSpawnTail] = command;
                    m_DebugSpawnTail = (m_DebugSpawnTail + 1) % m_PendingDebugSpawns.Length;
                    m_DebugSpawnHead = m_DebugSpawnTail;
                    return;
                }

                m_PendingDebugSpawns[m_DebugSpawnTail] = command;
                m_DebugSpawnTail = (m_DebugSpawnTail + 1) % m_PendingDebugSpawns.Length;
                m_DebugSpawnCount++;
            }
        }

        private bool TryDequeueDebugSpawn(out DebugSpawnCommand command)
        {
            lock (m_DebugSpawnLock)
            {
                if (m_DebugSpawnCount > 0)
                {
                    command = m_PendingDebugSpawns[m_DebugSpawnHead];
                    m_DebugSpawnHead = (m_DebugSpawnHead + 1) % m_PendingDebugSpawns.Length;
                    m_DebugSpawnCount--;
                    return true;
                }
            }

            command = default;
            return false;
        }

        private void ExecuteDebugSpawn(DebugSpawnCommand command)
        {
            try
            {
                if (World == null || !World.IsCreated) return;
#pragma warning disable CIVIC169 // Debug method: independent TryGet per button click
                var eventBus = ServiceRegistry.TryGet<IEventBus>();
#pragma warning restore CIVIC169
                if (eventBus == null) return;

                eventBus.SafePublish(
                    new SpawnWaveRequestEvent(command.ThreatCount, command.WaveNumber, command.WaveType, command.BallisticOverride),
                    "ThreatDebugSystem"
                );
                Log.Info(command.Label);
            }
            catch (Exception ex) { Log.Warn($"[DEBUG] ExecuteDebugSpawn failed: {ex}"); }
        }

        private void OnDebugSpawn1Drone()
        {
            QueueDebugSpawn(new DebugSpawnCommand(
                threatCount: 1,
                waveNumber: 98,
                waveType: WaveType.MassiveStrike,
                ballisticOverride: -1,
                label: "[DEBUG] Spawn 1 drone (wave #98)"));
        }

        private void OnDebugSpawn25Drones()
        {
            QueueDebugSpawn(new DebugSpawnCommand(
                threatCount: 25,
                waveNumber: 95,
                waveType: WaveType.MassiveStrike,
                ballisticOverride: -1,
                label: "[DEBUG] Spawn 25 drones (wave #95)"));
        }

        private void OnDebugSpawn1Ballistic()
        {
            QueueDebugSpawn(new DebugSpawnCommand(
                threatCount: 0,
                waveNumber: 97,
                waveType: WaveType.MassiveStrike,
                ballisticOverride: 1,
                label: "[DEBUG] Spawn 1 ballistic (wave #97)"));
        }

        private void OnDebugSpawn10Ballistics()
        {
            QueueDebugSpawn(new DebugSpawnCommand(
                threatCount: 0,
                waveNumber: 96,
                waveType: WaveType.MassiveStrike,
                ballisticOverride: 10,
                label: "[DEBUG] Spawn 10 ballistics (wave #96)"));
        }

    }
}
#endif
