using Game;
using Game.Input;
using Game.Objects;
using Game.Rendering;
using Game.Simulation;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using HarmonyLib;
using CivicSurvival.Core.Events;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Types.Snapshots;
using CivicSurvival.Core.Utils;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Components.Threats;
using CivicSurvival.Core.Attributes;

namespace CivicSurvival.Core.Systems.CameraTracking
{
    /// <summary>
    /// Smooth camera tracking system - runs every frame for fluid drone following.
    /// Detaches when user moves camera manually (detected via InputManager).
    ///
    /// Owns: CameraTrackingState (registered in ServiceRegistry)
    /// </summary>
    [ActIndependent]
    public partial class CameraTrackingSystem : CivicSystemBase
    {
        private static readonly LogContext Log = new("CameraTrackingSystem");

        // Facade lives in ServiceRegistry; this world-bound host owns snapshots.
        [System.NonSerialized] private CameraTrackingState? m_State;
        [System.NonSerialized] private readonly VersionedView<CameraSnapshot> m_SnapshotView = new(CameraSnapshot.Empty);

        private CameraUpdateSystem m_CameraSystem = null!;
        private TerrainSystem m_TerrainSystem = null!;
        private IThreatRadarReader m_RadarReader = null!;
        private IRenderWriteBarrier m_RenderWriteBarrier = null!;

        private Entity m_TrackedEntity;
        private Entity m_LastTrackedEntity;

        // PERF-LOCK: cached at target switch — drone prefab pins UpdateFrameData(10) so
        // m_Index is constant per entity; resolving every frame = EntityManager.GetSharedComponent sync.
        // [NonSerialized]: ephemeral cache, re-resolved on target switch — no save/load relevance.
        [System.NonSerialized] private uint m_TrackedUfIndex = uint.MaxValue;
        private float m_SkipTimeRemaining;
        private bool m_NeedSetRotation;

        // Camera shake state
        private float m_ShakeIntensity;
        private float m_ShakeDuration;
        private float m_ShakeTimeRemaining;
        [System.NonSerialized]
        private Vector3 m_LastShakeOffset;
        private System.Random m_ShakeRandom = new();

        // Camera pivot (direct follow)
        private Vector3 m_SmoothedPivot;

        // Hold camera in place after detach until user provides input
        private bool m_HoldAfterDetach;
        private float m_DetachTransitionRemaining;

        // Re-interpolate position from TransformFrame (same math as OIS) to avoid stale IT
        private BufferLookup<TransformFrame> m_TFBufferLookup;
        private RenderingSystem m_RenderingSystem = null!;
        private BufferLookup<RadarThreatBuffer> m_RadarBufferLookup;
        private EntityQuery m_RadarSingletonQuery;
        private EntityRef? m_PendingTrackRef;

        // Drone-tracking detection for CameraSnapshot.IsDroneTracking — read by
        // MotionBlurHandlerSystem to suppress MB only while camera follows a drone.
        private ComponentLookup<Shahed> m_ShahedLookup;
        private ComponentLookup<ActiveThreat> m_ActiveThreatLookup;

        // Camera input actions for detecting user movement
        private ProxyAction m_MoveAction = null!;
        private ProxyAction m_FastMoveAction = null!;

        // Vanilla Map Tiles view state (Game.UI.InGame.MapTilesUISystem.mapTileViewActive),
        // resolved reflectively for version resilience. Instance fields (re-resolved per world)
        // read on the main thread each frame while tracking — no ECS sync point.
        [System.NonSerialized] private bool m_MapViewResolved;
        [System.NonSerialized] private System.Func<bool>? m_MapViewGetter;

        // ===== Public API =====

        /// <summary>Set entity to track by index+version identity. Resolved to Entity next frame.</summary>
        public void SetTrackedEntity(EntityRef entityRef)
        {
            if (entityRef.Index <= 0)
            {
                if (m_TrackedEntity != Entity.Null)
                    DetachFromTracking();
                return;
            }

            m_PendingTrackRef = entityRef;
            m_HoldAfterDetach = false;
            StopShake();
        }

        /// <summary>
        /// Trigger camera shake effect at a world position. Intensity falls off with
        /// distance to camera pivot — impacts beyond MAX_SHAKE_RANGE are silent.
        /// Skipped entirely when camera is tracking a flying object (player is "in" the
        /// drone, ground explosions shouldn't shake an airborne viewport).
        /// Multiple calls aggregate within MAX_SHAKE_DURATION.
        /// </summary>
        public void TriggerShake(float intensity, float duration, float3 worldPosition)
        {
            // Tracking guard — viewport follows a flying entity, ground impact must not shake it.
            if (m_TrackedEntity != Entity.Null) return;

            var controller = m_CameraSystem?.activeCameraController;
            if (controller == null) return;

            // Distance falloff — close impacts shake, far impacts don't.
            float3 pivot = controller.pivot;
            float distance = math.distance(pivot, worldPosition);
            float falloff = math.saturate(1f - distance / MAX_SHAKE_RANGE);
            if (falloff <= 0f) return;
            float effectiveIntensity = intensity * falloff;

            // Max duration to prevent infinite shake from rapid impacts
            const float MAX_SHAKE_DURATION = 2f;

            if (m_ShakeTimeRemaining > 0)
            {
                // Already shaking - add partial time, don't reset
                m_ShakeIntensity = System.Math.Max(m_ShakeIntensity, effectiveIntensity);
                m_ShakeTimeRemaining = System.Math.Min(m_ShakeTimeRemaining + duration * SHAKE_OVERLAP_FACTOR, MAX_SHAKE_DURATION);
                m_ShakeDuration = m_ShakeTimeRemaining;  // Keep ratio correct for decay
            }
            else
            {
                // New shake
                m_ShakeIntensity = effectiveIntensity;
                m_ShakeDuration = duration;
                m_ShakeTimeRemaining = duration;
            }

            // BUG-CAM-034 FIX: Clamp intensity to prevent camera flying off screen
            m_ShakeIntensity = System.Math.Min(m_ShakeIntensity, MAX_SHAKE_INTENSITY);
        }

        /// <summary>
        /// World ground-target (pivot) X/Z of the active camera — the point the
        /// camera looks at, used for the radar "you are here" marker. Returns false
        /// when no camera controller is available (early city load), letting the
        /// caller fall back to its no-position sentinel. Pure read on the main
        /// thread (no ECS data access), safe to call from a UI panel update while
        /// the simulation is paused.
        /// </summary>
        public bool TryGetCameraGroundPosition(out float x, out float z)
        {
            var controller = m_CameraSystem?.activeCameraController;
            if (controller == null)
            {
                x = 0f;
                z = 0f;
                return false;
            }

            Vector3 pivot = controller.pivot;
            x = pivot.x;
            z = pivot.z;
            return true;
        }

        // Camera settings for tracking
        private const float CAMERA_ZOOM = 25f;
        private const float CAMERA_PITCH = 45f;
        private const float SHAKE_OVERLAP_FACTOR = 0.3f;
        private const float TARGET_SWITCH_SETTLE_TIME = 0.16f;
        private const float DETACH_TRANSITION_SECONDS = 0.35f;
        private const float HALF_TURN_DEGREES = 180f;

        // BUG-CAM-034 FIX: Maximum shake intensity to prevent camera flying off
        private const float MAX_SHAKE_INTENSITY = 15f;

        // Shake distance falloff — beyond this range from camera pivot, impacts are silent.
        // Prevents distant ground explosions from rattling the viewport (which makes flying
        // drones appear to jitter even though they're physically unaffected).
        private const float MAX_SHAKE_RANGE = 1000f;

        // Debug logging

        protected override void OnCreate()
        {
            base.OnCreate();

            // Subscribe to wave end event to stop shake when all threats neutralized
            SubscribeRequired<WaveEndedEvent>(OnWaveEnded);

            // PERF: Lookups in OnCreate (Axiom 8), .Update(this) every frame
            m_TFBufferLookup = GetBufferLookup<TransformFrame>(true);
            m_RadarBufferLookup = GetBufferLookup<RadarThreatBuffer>(true);
            m_RadarSingletonQuery = GetEntityQuery(ComponentType.ReadOnly<RadarDataSingleton>());
            m_ShahedLookup = GetComponentLookup<Shahed>(true);
            m_ActiveThreatLookup = GetComponentLookup<ActiveThreat>(true);

            if (ServiceRegistry.IsInitialized)
            {
                m_State = ServiceRegistry.Instance.Get<CameraTrackingState>();
                if (m_State != null)
                    BindFacade(m_State);
                else
                    Log.Warn("CameraTrackingState facade not registered — camera tracking consumers will see default state");
            }
            else
            {
                Log.Warn("ServiceRegistry not initialized — CameraTrackingState facade unavailable");
            }

            Log.Info("Created (state + service registered)");
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            m_CameraSystem = World.GetOrCreateSystemManaged<CameraUpdateSystem>();
            m_TerrainSystem = World.GetOrCreateSystemManaged<TerrainSystem>();
            m_RenderingSystem = World.GetOrCreateSystemManaged<RenderingSystem>();
            m_RenderWriteBarrier = ServiceRegistry.Instance.Require<IRenderWriteBarrier>();
            m_RadarReader = ServiceRegistryFeatureExtensions.TryGetOrNullObject(NullThreatRadarReader.Instance);

            var inputManager = InputManager.instance;
            if (inputManager != null)
            {
                m_MoveAction = inputManager.FindAction("Camera", "Move");
                m_FastMoveAction = inputManager.FindAction("Camera", "Move Fast");
            }
            else
            {
                Log.Warn("InputManager.instance null in OnStartRunning; camera detach-on-input disabled until next world start");
            }
        }

        private void OnWaveEnded(WaveEndedEvent evt)
        {
            // Stop any lingering camera shake when wave ends
            if (m_ShakeTimeRemaining > 0)
            {
                if (Log.IsDebugEnabled) Log.Debug($"Wave {evt.WaveNumber} ended - stopping shake");
                StopShake();
            }
        }

#pragma warning disable CIVIC233 // m_TrackedEntity is Entity.Null when no target — CameraSnapshot handles null
        private void UpdateStateSnapshot()
        {
            bool isDroneTracking = m_TrackedEntity != Entity.Null
                && m_ShahedLookup.HasComponent(m_TrackedEntity)
                && m_ActiveThreatLookup.HasComponent(m_TrackedEntity)
                && m_ActiveThreatLookup.IsComponentEnabled(m_TrackedEntity);
            m_SnapshotView.Publish(new CameraSnapshot(m_TrackedEntity, 0f, isDroneTracking));
#pragma warning restore CIVIC233
        }

        internal CameraSnapshot GetCurrentSnapshot()
        {
            int observerVersion = -1;
            return m_SnapshotView.Observe(ref observerVersion).Value;
        }

        internal void BindFacade(CameraTrackingState facade)
        {
            m_State = facade;
            facade.CurrentHost = this;
        }

        /// <summary>
        /// Detach camera from tracked entity.
        /// holdPosition=true: camera stays at current position until user provides input.
        /// holdPosition=false: immediately release to vanilla (user already moving).
        /// </summary>
        private void DetachFromTracking(bool holdPosition = true, bool transition = true)
        {
            m_TrackedEntity = Entity.Null;
            m_TrackedUfIndex = uint.MaxValue;

            if (holdPosition)
            {
                m_HoldAfterDetach = true;
                m_DetachTransitionRemaining = DETACH_TRANSITION_SECONDS;
                m_SnapshotView.Publish(new CameraSnapshot(Entity.Null, 1f));
                Log.Info("Detach: camera held in place");
            }
            else if (transition)
            {
                m_HoldAfterDetach = false;
                m_DetachTransitionRemaining = DETACH_TRANSITION_SECONDS;
                m_SnapshotView.Publish(new CameraSnapshot(Entity.Null));
                Log.Info("Detach: released to user");
            }
            else
            {
                // Immediate release with no transition lerp — used for the Map Tiles view
                // hand-off so the patch sees neither tracking nor transition and stays inert,
                // letting vanilla HandleMapViewCamera own the camera the same frame.
                m_HoldAfterDetach = false;
                m_DetachTransitionRemaining = 0f;
                m_SnapshotView.Publish(CameraSnapshot.Empty);
                Log.Info("Detach: map-view hand-off (immediate)");
            }
        }

        /// <summary>
        /// Reads vanilla Game.UI.InGame.MapTilesUISystem.mapTileViewActive (static) reflectively.
        /// True while the player is in the Map Tiles purchase view, where vanilla's
        /// HandleMapViewCamera owns the camera and our tracking must stand down.
        /// </summary>
        private bool IsMapTileViewActive()
        {
            if (!m_MapViewResolved)
            {
                m_MapViewResolved = true;
                var type = AccessTools.TypeByName("Game.UI.InGame.MapTilesUISystem")
                           ?? AccessTools.TypeByName("Game.Tools.MapTilesUISystem");
                if (type != null)
                {
                    var prop = AccessTools.Property(type, "mapTileViewActive");
                    if (prop?.GetMethod is { IsStatic: true } getter)
                    {
                        m_MapViewGetter = (System.Func<bool>)System.Delegate.CreateDelegate(typeof(System.Func<bool>), getter);
                    }
                    else
                    {
                        var field = AccessTools.Field(type, "mapTileViewActive");
                        if (field is { IsStatic: true })
                            m_MapViewGetter = () => (bool)field.GetValue(null);
                    }
                }

                if (m_MapViewGetter == null)
                    Log.Warn("MapTilesUISystem.mapTileViewActive not found — map-view tracking hand-off disabled");
            }

            return m_MapViewGetter != null && m_MapViewGetter();
        }

        protected override void OnUpdateImpl()
        {
            using (PerformanceProfiler.Measure("CameraTracking.OnUpdate"))
            {
                OnUpdateInternal();
            }
        }

#pragma warning disable CIVIC233 // m_LastTrackedEntity used only for change detection (== comparison), not ECS data access
        private void OnUpdateInternal()
        {
#pragma warning restore CIVIC233
            // PERF-LOCK: skip entirely when there is nothing to do — the 4× Lookup.Update
            // below bumps TransformFrame sync every frame otherwise (Rendering writes TF).
            // Do NOT move Lookup.Update above this gate. See CLAUDE.md Axiom 15.
            if (m_TrackedEntity == Entity.Null
                && !m_PendingTrackRef.HasValue
                && !m_HoldAfterDetach
                && m_ShakeTimeRemaining <= 0f
                && m_DetachTransitionRemaining <= 0f)
                return;

            // Map-view hand-off: when the player opens the vanilla Map Tiles view, vanilla's
            // HandleMapViewCamera owns the camera. Release tracking immediately (no detach lerp)
            // so SyncRenderFrame and the CameraController patch stay inert and don't fight it.
            if ((m_TrackedEntity != Entity.Null || m_PendingTrackRef.HasValue) && IsMapTileViewActive())
            {
                m_PendingTrackRef = null;
                if (m_TrackedEntity != Entity.Null)
                    DetachFromTracking(holdPosition: false, transition: false);
                return;
            }

            var renderTicket = m_RenderWriteBarrier.Consume(GetType(), RenderWriteComponentMask.ThreatTransformFrame);

            // CRITICAL: Update lookups every frame before any usage (DOTS safety)
            m_TFBufferLookup.Update(this);
            m_RadarBufferLookup.Update(this);
            m_ShahedLookup.Update(this);
            m_ActiveThreatLookup.Update(this);

            // Resolve pending radar reference through the live buffer, matching both index and version.
            if (m_PendingTrackRef.HasValue)
            {
                var pending = m_PendingTrackRef.Value;
                Entity resolved = Entity.Null;

                if (m_RadarSingletonQuery.TryGetSingletonEntity<RadarDataSingleton>(out var radarEntity))
                {
                    if (m_RadarBufferLookup.TryGetBuffer(radarEntity, out var buffer))
                    {
                        for (int i = 0; i < buffer.Length; i++)
                        {
                            if (buffer[i].EntityIndex == pending.Index && buffer[i].EntityVersion == pending.Version)
                            {
                                resolved = pending.ToEntity();
                                break;
                            }
                        }
                    }
                }

                if (resolved == Entity.Null && Log.IsDebugEnabled)
                    Log.Debug($"Stale radar focus: idx={pending.Index} ver={pending.Version} (no matching live row)");

                m_TrackedEntity = resolved;
                m_PendingTrackRef = null;
                if (m_TrackedEntity != Entity.Null)
                    StopShake();

                // Row W2-55: always refresh snapshot, including on null resolution.
                // Otherwise CameraTrackingState retains the previous TrackedEntity, and
                // CameraControllerPatch reads a dead entity → pins smoothing=1f and skips
                // terrain clamp for one or more frames.
                UpdateStateSnapshot();
            }

            // Hold camera after detach — wait for user input to release
            if (m_HoldAfterDetach)
            {
                bool userInput = (m_MoveAction != null && m_MoveAction.IsInProgress()) ||
                                 (m_FastMoveAction != null && m_FastMoveAction.IsInProgress());

                if (userInput)
                {
                    m_HoldAfterDetach = false;
                    m_SnapshotView.Publish(new CameraSnapshot(Entity.Null));
                    Log.Info("User input - releasing held camera");
                }
            }

            // Not tracking anything
            if (m_TrackedEntity == Entity.Null)
            {
                PublishDetachTransition();

                // Camera shake runs independently of tracking state.
                var shakeController = m_CameraSystem?.activeCameraController;
                if (shakeController != null)
                    ApplyCameraShake(shakeController);

                m_LastTrackedEntity = Entity.Null;
                return;
            }

            // Detect target switch - skip input detection for a few frames
            if (m_TrackedEntity != m_LastTrackedEntity)
            {
                m_LastTrackedEntity = m_TrackedEntity;
                m_SkipTimeRemaining = TARGET_SWITCH_SETTLE_TIME;
                m_NeedSetRotation = true;
                m_TrackedUfIndex = TryGetTrackedUpdateFrameIndex(renderTicket, m_TrackedEntity, out uint cachedUf)
                    ? cachedUf
                    : uint.MaxValue;
            }

            if (m_SkipTimeRemaining > 0f)
            {
                m_SkipTimeRemaining -= UnityEngine.Time.deltaTime;
            }
            else
            {
                // Check if user is providing camera MOVEMENT input - detach if so
                // Rotation (Q/E/middle mouse) does NOT detach - user can rotate while tracking
                // Zoom (scroll wheel) does NOT detach - user can zoom while tracking.
                // Zoom events leak from Coherent UI into InputManager causing false detach on UI clicks.
                bool userInput = (m_MoveAction != null && m_MoveAction.IsInProgress()) ||
                                 (m_FastMoveAction != null && m_FastMoveAction.IsInProgress());

                if (userInput)
                {
                    Log.Info("User camera movement detected - detaching");
                    DetachFromTracking(holdPosition: false);
                }
            }

            // Republish snapshot every frame while tracking so IsDroneTracking stays
            // fresh — covers the edge case where ActiveThreat is removed from the
            // entity mid-flight without destroying it. Skipped if the detach branch
            // above already nulled m_TrackedEntity (that path published its own
            // detach snapshot with TransitionProgress).
            if (m_TrackedEntity != Entity.Null)
                UpdateStateSnapshot();

            // BRG integration: re-interpolate position from TransformFrame every render update.
            SyncRenderFrame(renderTicket);
        }

        private void PublishDetachTransition()
        {
            if (m_DetachTransitionRemaining <= 0f)
                return;

            m_DetachTransitionRemaining = math.max(0f, m_DetachTransitionRemaining - UnityEngine.Time.deltaTime);
            float progress = DETACH_TRANSITION_SECONDS > 0f
                ? math.saturate(m_DetachTransitionRemaining / DETACH_TRANSITION_SECONDS)
                : 0f;
            m_SnapshotView.Publish(new CameraSnapshot(Entity.Null, progress));
        }

        /// <summary>
        /// Called each rendering update.
        /// Re-interpolates position from TransformFrame buffer (same Bezier math as OIS)
        /// so camera and OIS produce identical positions — eliminates stale-IT jitter.
        /// </summary>
        private void SyncRenderFrame(RenderWriteTicket renderTicket)
        {
            var controller = m_CameraSystem?.activeCameraController;
            if (controller == null) return;

            bool isTracking = false;

            if (m_TrackedEntity != Entity.Null)
            {
                // Re-interpolate from TransformFrame (vanilla pattern: SelectedInfoUISystem.GetInterpolatedPosition)
                // Camera computes the SAME Bezier as OIS → zero desync, zero jitter
                bool foundEntity = TryGetTrackedTransformFrame(renderTicket, m_TrackedEntity, out var tfBuffer);

                if (!foundEntity || m_TrackedUfIndex == uint.MaxValue)
                {
                    Log.Info("Tracked threat destroyed");
                    DetachFromTracking();
                }
                else
                {
                    isTracking = true;
                    ObjectInterpolateSystem.CalculateUpdateFrames(
                        m_RenderingSystem.frameIndex, m_RenderingSystem.frameTime,
                        m_TrackedUfIndex, out var s1, out var s2, out var t);
                    var it = ObjectInterpolateSystem.CalculateTransform(tfBuffer[(int)s1], tfBuffer[(int)s2], t);
                    float3 renderPos = it.m_Position;

                    Vector3 targetPivot = new Vector3(renderPos.x, renderPos.y, renderPos.z);

                    // Direct follow: no smoothing
                    m_SmoothedPivot = targetPivot;

                    controller.pivot = m_SmoothedPivot;
                    m_LastShakeOffset = Vector3.zero; // tracking overwrites pivot, so previous transient offset is already gone
                    controller.zoom = CAMERA_ZOOM;

                    // Set camera angle only once when switching target
                    if (m_NeedSetRotation)
                    {
                        bool rotationApplied = false;
                        var threats = m_RadarReader.GetRadarThreats();
                        foreach (var threat in threats)
                        {
                            if (threat.Entity.Index == m_TrackedEntity.Index && threat.Entity.Version == m_TrackedEntity.Version)
                            {
                                float yaw = math.atan2(threat.Vx, threat.Vz) * (HALF_TURN_DEGREES / math.PI);
                                controller.rotation = new Vector3(CAMERA_PITCH, yaw, 0f);
                                Log.Info($"Target {m_TrackedEntity.Index}: yaw={yaw:F0}°, pitch={CAMERA_PITCH}°");
                                rotationApplied = true;
                                break;
                            }
                        }

                        if (rotationApplied)
                            m_NeedSetRotation = false;
                    }

                    ApplyCameraShake(controller);
                }
            }
            // FIX: Force-update camera transform from final pivot (eliminates 1-frame lag).
            // Without this, controller.pivot is set but transform.localPosition still uses
            // the previous frame's pivot (computed in UpdateCamera which ran BEFORE OnRender).
            if (isTracking && controller is CameraController cc)
            {
                var rot = controller.rotation;              // Vector3(m_Angle.y, m_Angle.x, 0)
#pragma warning disable S2234 // Intentional: rotation = (pitch, yaw, 0), we reconstruct (yaw, pitch)
                float2 angle = new float2(rot.y, rot.x);   // m_Angle = (yaw, pitch)
#pragma warning restore S2234
                float2 rad = math.radians(angle);

                float3 dir;
                dir.x = -math.sin(rad.x);
                dir.y = 0f;
                dir.z = -math.cos(rad.x);
                float3 xzDir = dir;
                dir *= math.cos(rad.y);
                dir.y = math.sin(rad.y);
                float3 lookDir = -dir;
                dir *= cc.zoom;

                float3 finalPivot = (float3)cc.pivot;      // after shake offset
                float3 camPos = finalPivot + dir;
                float minZoom = cc.zoomRange.min;
                camPos.y += minZoom * 0.5f;

                // Vanilla terrain collision offset (lines 386-389 of CameraController.UpdateCamera)
                // Prevents camera from clipping through terrain at low angles
                if (m_TerrainSystem != null)
                {
                    TerrainHeightData heightData = m_TerrainSystem.GetHeightData();
                    if (heightData.isCreated)
                    {
#pragma warning disable CIVIC201 // Vanilla CameraController arithmetic constants
                        float terrainAtCam = TerrainUtils.SampleHeight(ref heightData, camPos);
                        float threshold = terrainAtCam + minZoom * 0.5f + (cc.zoom - minZoom) * 0.1f;
                        float ratio = (camPos.y - threshold) / math.max(cc.zoom, 0.001f);
                        float adjust = (math.sqrt(ratio * ratio + 0.2f) - ratio) * (0.5f * cc.zoom);
                        camPos.y += adjust;
#pragma warning restore CIVIC201
                    }
                }

                float3 right = math.cross(xzDir, new float3(0f, 1f, 0f));
                float3 up = math.cross(lookDir, right);
                quaternion camRot = quaternion.LookRotation(lookDir, up);

                cc.transform.localPosition = new Vector3(camPos.x, camPos.y, camPos.z);
                cc.transform.localRotation = new Quaternion(camRot.value.x, camRot.value.y, camRot.value.z, camRot.value.w);

                // FIX: Also update Camera.main directly — Cinemachine copies vcamT→mainCam
                // only in next frame's ManualUpdate(), causing 1-frame lag.
                var mainCam = Camera.main;
                if (mainCam != null)
                {
                    mainCam.transform.position = cc.transform.position;
                    mainCam.transform.rotation = cc.transform.rotation;
                }
            }
        }

        private bool TryGetTrackedTransformFrame(
            RenderWriteTicket renderTicket,
            Entity entity,
            out DynamicBuffer<TransformFrame> transformFrames)
        {
            EnsureRenderTicket(renderTicket, RenderWriteComponentMask.ThreatTransformFrame);
            return m_TFBufferLookup.TryGetBuffer(entity, out transformFrames)
                && transformFrames.Length == 4;
        }

        private bool TryGetTrackedUpdateFrameIndex(RenderWriteTicket renderTicket, Entity entity, out uint updateFrameIndex)
        {
            EnsureRenderTicket(renderTicket, RenderWriteComponentMask.ThreatTransformFrame);
            updateFrameIndex = 0;

            // Guard existence BEFORE GetSharedComponent via the cached, already-updated
            // BufferLookup (assert-free: HasBuffer routes through EntityComponentStore.HasComponent,
            // which returns false for a destroyed entity instead of asserting). A stale radar row
            // can hand us an index+version whose entity was destroyed since the row was written;
            // calling GetSharedComponent on a dead entity makes EntityComponentStore.GetArchetype
            // fail its Assert.IsTrue(exists) and throw UnityEngine.Assertions.AssertionException —
            // neither ArgumentException nor InvalidOperationException, so it escapes the catch below
            // and surfaces as a SceneFlow CRITICAL. A tracked drone always carries TransformFrame.
            if (!m_TFBufferLookup.HasBuffer(entity))
            {
                if (Log.IsDebugEnabled)
                    Log.Debug($"UpdateFrame unavailable — entity {entity.Index}:{entity.Version} gone");
                return false;
            }

            try
            {
                updateFrameIndex = EntityManager.GetSharedComponent<UpdateFrame>(entity).m_Index;
                return true;
            }
            catch (System.ArgumentException)
            {
                // Live render entity without an UpdateFrame shared component (not a drone archetype).
                // Existence is already guaranteed above, so this is the only remaining miss.
                if (Log.IsDebugEnabled)
                    Log.Debug($"UpdateFrame missing on entity {entity.Index}:{entity.Version}");
                return false;
            }
        }

        private static void EnsureRenderTicket(RenderWriteTicket renderTicket, RenderWriteComponentMask requiredMask)
        {
            if (!renderTicket.Covers(requiredMask))
                throw new System.InvalidOperationException($"Render write ticket does not cover {requiredMask}.");
        }

        /// <summary>
        /// Apply camera shake effect with linear decay.
        /// Offsets camera pivot position by random amount.
        /// Uses unscaledDeltaTime so shake decays even when game is paused.
        /// </summary>
        private void ApplyCameraShake(Game.Rendering.IGameCameraController controller)
        {
            if (m_ShakeTimeRemaining <= 0)
            {
                ClearCameraShakeOffset(controller);
                m_ShakeIntensity = 0;
                m_ShakeDuration = 0;
                return;
            }

            // Calculate decay factor (1.0 at start → 0.0 at end)
            float t = m_ShakeDuration > 0 ? m_ShakeTimeRemaining / m_ShakeDuration : 0f;
            float currentIntensity = m_ShakeIntensity * t;

            // Skip if intensity too low (prevents micro-jitter)
            if (currentIntensity < 0.01f)
            {
                StopShake(controller);
                return;
            }

            // Random offset in XZ plane (horizontal shake)
            float offsetX = ((float)m_ShakeRandom.NextDouble() * 2f - 1f) * currentIntensity;
            float offsetZ = ((float)m_ShakeRandom.NextDouble() * 2f - 1f) * currentIntensity;

            // Apply shake as a transient offset. Previous frame's offset is removed first,
            // so impacts cannot permanently walk the camera pivot.
            var basePivot = controller.pivot - m_LastShakeOffset;
            m_LastShakeOffset = new Vector3(offsetX, 0f, offsetZ);
            controller.pivot = basePivot + m_LastShakeOffset;

            // Decay shake using UNSCALED time (works even when game paused/slowed)
            m_ShakeTimeRemaining -= UnityEngine.Time.unscaledDeltaTime;

            // Force stop if time went negative (floating point safety)
            if (m_ShakeTimeRemaining < 0)
            {
                StopShake(controller);
            }
        }

        /// <summary>
        /// Force stop camera shake immediately.
        /// Call this when wave ends or player manually cancels.
        /// </summary>
        public void StopShake()
        {
            StopShake(m_CameraSystem?.activeCameraController);
        }

        private void StopShake(Game.Rendering.IGameCameraController? controller)
        {
            m_ShakeTimeRemaining = 0;
            m_ShakeIntensity = 0;
            m_ShakeDuration = 0;
            if (controller != null)
                ClearCameraShakeOffset(controller);
            else
                m_LastShakeOffset = Vector3.zero;
        }

        private void ClearCameraShakeOffset(Game.Rendering.IGameCameraController controller)
        {
            if (m_LastShakeOffset == Vector3.zero)
                return;

            controller.pivot -= m_LastShakeOffset;
            m_LastShakeOffset = Vector3.zero;
        }

        protected override void OnDestroy()
        {
            // Unsubscribe from events
            UnsubscribeSafe<WaveEndedEvent>(OnWaveEnded);

            if (m_State != null && ReferenceEquals(m_State.CurrentHost, this))
                m_State.CurrentHost = null;
            m_State = null;
            Log.Info("Destroyed");
            base.OnDestroy();
        }
    }
}
