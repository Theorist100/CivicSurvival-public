using Game;
using Game.Simulation;
using Unity.Entities;
using CivicSurvival.Core.Components.Threats;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Systems;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Systems.CameraTracking;
using CivicSurvival.Core.Systems.Scheduling;
using CivicSurvival.Core.Utils;
using CivicSurvival.Core.Attributes;

namespace CivicSurvival.Domains.ThreatUI.Systems
{
    /// <summary>
    /// Ticks identify progress for camera-tracked drone, transitions to Focus (PriorityTarget).
    ///
    /// O(1) per frame: uses ComponentLookup to directly access the tracked entity
    /// instead of iterating all active threats. Tracks m_LastTrackedEntity for
    /// efficient detach (reset progress + remove PriorityTarget on camera change).
    ///
    /// Logic:
    /// - If camera tracks a Shahed with IdentifiedTarget:
    ///   - Tick IdentifyProgress += deltaTime / IDENTIFY_DURATION
    ///   - On Progress >= 1.0 → Identified = true
    ///   - If Identified and still tracking → add PriorityTarget tag via ECB
    /// - If camera moves away: reset in-progress, remove PriorityTarget
    /// - Only one PriorityTarget at a time
    ///
    /// Scheduling: writes IdentifiedTarget before threat movement snapshots radar buffers.
    /// </summary>
    [ActIndependent]
    public partial class ThreatIdentifySystem : CivicSystemBase
    {
        private static readonly LogContext Log = new("ThreatIdentifySystem");

        private const float IDENTIFY_DURATION = 2.5f;
        private const float FOCUS_COOLDOWN = 30f;

        private EntityQuery m_ThreatQuery;
        private CameraTrackingState m_CameraTrackingState = null!;
        private GameSimulationEndBarrier m_GameSimulationEndBarrier = null!;
        private ComponentLookup<IdentifiedTarget> m_IdentifiedTargetLookup;
        private ComponentLookup<PriorityTarget> m_PriorityTargetLookup;
        private ComponentLookup<Shahed> m_ShahedLookup;
        private ComponentLookup<ActiveThreat> m_ActiveThreatLookup;

        /// <summary>Last time focus was released (game elapsed time). Prevents focus spam.</summary>
        // M6 FIX: Init to -FOCUS_COOLDOWN so focus is available immediately (hot-reload safe)
        private double m_LastFocusReleaseTime = -FOCUS_COOLDOWN;

        /// <summary>Entity tracked last frame — enables O(1) detach/reset.</summary>
        private Entity m_LastTrackedEntity;

        // Public read-only state for UI system
        /// <summary>Entity index currently being tracked for identification.</summary>
        public int TrackedEntityIndex { get; private set; } = -1;
        /// <summary>Identification progress 0→1.</summary>
        public float IdentifyProgress { get; private set; }
        /// <summary>True when identification is complete.</summary>
        public bool IsIdentified { get; private set; }
        /// <summary>True when target has PriorityTarget tag (focus-held).</summary>
        public bool IsFocusActive { get; private set; }

        protected override void OnCreate()
        {
            base.OnCreate();

            m_ThreatQuery = GetEntityQuery(
                ComponentType.ReadOnly<IdentifiedTarget>(),
                ComponentType.ReadOnly<Shahed>(),
                ComponentType.ReadOnly<ActiveThreat>()
            );

            m_GameSimulationEndBarrier = World.GetOrCreateSystemManaged<GameSimulationEndBarrier>();
            m_IdentifiedTargetLookup = GetComponentLookup<IdentifiedTarget>(false);
            m_PriorityTargetLookup = GetComponentLookup<PriorityTarget>(true);
            m_ShahedLookup = GetComponentLookup<Shahed>(true);
            m_ActiveThreatLookup = GetComponentLookup<ActiveThreat>(true);

            RequireForUpdate(m_ThreatQuery);

            Log.Info("Created");
        }

#pragma warning disable CIVIC233 // m_LastTrackedEntity is reset to Entity.Null (assignment, not usage)
        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            m_CameraTrackingState = ServiceRegistry.Instance.Require<CameraTrackingState>();
        }

        protected override void OnGameLoaded(Colossal.Serialization.Entities.Context serializationContext)
        {
            base.OnGameLoaded(serializationContext);
            m_LastFocusReleaseTime = -FOCUS_COOLDOWN;
            m_LastTrackedEntity = Entity.Null;
#pragma warning restore CIVIC233
        }

        protected override void OnStopRunning()
        {
            base.OnStopRunning();

            // Teardown path: OnUpdateImpl detach won't run, and an ECB created here
            // is not guaranteed to play back after the system stops. Direct
            // EntityManager removal is intentional for this last tracked tag.
            if (m_LastTrackedEntity != Entity.Null)
            {
                m_PriorityTargetLookup.Update(this);
                if (m_PriorityTargetLookup.HasComponent(m_LastTrackedEntity))
                {
                    EntityManager.RemoveComponent<PriorityTarget>(m_LastTrackedEntity);
                }
                m_LastTrackedEntity = Entity.Null;
            }

            TrackedEntityIndex = -1;
            IdentifyProgress = 0f;
            IsIdentified = false;

            if (IsFocusActive)
            {
                double elapsed = SystemAPI.Time.ElapsedTime;
                m_LastFocusReleaseTime = elapsed;
            }

            IsFocusActive = false;
        }

        protected override void OnUpdateImpl()
        {
            Entity trackedEntity = m_CameraTrackingState.Current.TrackedEntity;
            float deltaTime = SystemAPI.Time.DeltaTime;
            double elapsedTime = SystemAPI.Time.ElapsedTime;

            using (PerformanceProfiler.MeasureDebug("SP:TIS.LookupSync"))
            {
                m_IdentifiedTargetLookup.Update(this);
                m_PriorityTargetLookup.Update(this);
                m_ShahedLookup.Update(this);
                m_ActiveThreatLookup.Update(this);
            }

            // Reset UI state each frame (capture previous focus state for detach path)
            bool wasFocusPreviousFrame = IsFocusActive;
            TrackedEntityIndex = -1;
            IdentifyProgress = 0f;
            IsIdentified = false;
            IsFocusActive = false;

            // NOTE: focusCooldownActive computed after detach block (W7-M17 FIX)

            // O(1): validate tracked entity is an active Shahed drone
            // ComponentLookup.HasComponent returns false for destroyed entities
            bool trackedIsValid = trackedEntity != Entity.Null
                && m_ShahedLookup.HasComponent(trackedEntity)
                && m_ActiveThreatLookup.HasComponent(trackedEntity)
                && m_ActiveThreatLookup.IsComponentEnabled(trackedEntity)
                && m_IdentifiedTargetLookup.HasComponent(trackedEntity);

            // Detach when: previous target exists AND (camera changed OR target became invalid)
            bool needsDetach = m_LastTrackedEntity != Entity.Null
                && (!trackedIsValid || m_LastTrackedEntity != trackedEntity);

            bool ecbCreated = false;
            EntityCommandBuffer ecb = default;

            // ── O(1) Detach: reset previous target ──
            if (needsDetach)
            {
                if (m_IdentifiedTargetLookup.HasComponent(m_LastTrackedEntity))
                {
                    var prev = m_IdentifiedTargetLookup[m_LastTrackedEntity];
                    if (prev.IdentifyProgress > 0f && !prev.Identified)
                    {
                        prev.IdentifyProgress = 0f;
                        m_IdentifiedTargetLookup[m_LastTrackedEntity] = prev;
                    }

                    if (m_PriorityTargetLookup.HasComponent(m_LastTrackedEntity))
                    {
                        ecb = m_GameSimulationEndBarrier.CreateCommandBuffer();
                        ecbCreated = true;
                        ecb.RemoveComponent<PriorityTarget>(m_LastTrackedEntity);
                        Log.Info($"[IDENTIFY] PriorityTarget removed from {m_LastTrackedEntity.Index} (camera detached)");
                    }
                }

                // Use previous frame's focus state — handles destroyed entities
                // (HasComponent fails on dead entities, but IsFocusActive was true last frame)
                if (wasFocusPreviousFrame)
                {
                    m_LastFocusReleaseTime = elapsedTime;
                }
            }

            // W7-M17 FIX: Compute AFTER detach to include freshly-written m_LastFocusReleaseTime
            bool focusCooldownActive = (elapsedTime - m_LastFocusReleaseTime) < FOCUS_COOLDOWN;

            // ── O(1) Track: process current target ──
            if (trackedIsValid)
            {
                TrackedEntityIndex = trackedEntity.Index;
                var identify = m_IdentifiedTargetLookup[trackedEntity];

                if (!identify.Identified)
                {
                    // Tick identification progress (bounded [0, 1], not unbounded accumulator)
#pragma warning disable CIVIC056
                    identify.IdentifyProgress += deltaTime / IDENTIFY_DURATION;
#pragma warning restore CIVIC056
                    if (identify.IdentifyProgress >= 1.0f)
                    {
                        identify.IdentifyProgress = 1.0f;
                        identify.Identified = true;
                        Log.Info($"[IDENTIFY] Target {trackedEntity.Index} CONFIRMED (Drone Type-A)");
                    }
                    m_IdentifiedTargetLookup[trackedEntity] = identify;
                }

                IdentifyProgress = identify.IdentifyProgress;
                IsIdentified = identify.Identified;

                // Focus: add PriorityTarget if identified and cooldown expired
                if (identify.Identified && !focusCooldownActive)
                {
                    if (!m_PriorityTargetLookup.HasComponent(trackedEntity))
                    {
#pragma warning disable CIVIC118 // Lazy ECB: created at most once via ecbCreated guard
                        if (!ecbCreated) { ecb = m_GameSimulationEndBarrier.CreateCommandBuffer(); ecbCreated = true; }
#pragma warning restore CIVIC118
                        ecb.AddComponent<PriorityTarget>(trackedEntity);
                        Log.Info($"[IDENTIFY] PriorityTarget added to {trackedEntity.Index}");
                    }
                    IsFocusActive = true;
                }
            }

            if (ecbCreated)
                m_GameSimulationEndBarrier.AddJobHandleForProducer(Dependency);

            m_LastTrackedEntity = trackedIsValid ? trackedEntity : Entity.Null;
        }
    }
}
