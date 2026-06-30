using System.Collections.Generic;
using Game;
using Game.Rendering;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Components.Domain.AirDefense;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Domains.AirDefense.Systems
{
    /// <summary>
    /// Draws AA tracer rounds as emissive 3D cylinders (stretched tubes) along the flight ray via
    /// <c>Graphics.RenderMesh</c> — a shared procedural cylinder mesh + an additive HDRP/Unlit
    /// material, submitted directly to HDRP each frame, BYPASSING the BRG/OIS/culling pipeline AND
    /// the vanilla OverlayRenderSystem. This system also owns tracer lifetime (decrement + expiry).
    ///
    /// Why this path (two prior approaches failed, see Docs/Plans/TRACER_RENDER_FIX_PLAN.md):
    /// - BRG mesh + OIS interpolated a 1100 m/s vertical tracer ~0.5s in the past → its render
    ///   position fell to the spawn point (underground).
    /// - OverlayRenderSystem.DrawLine sizes/gates the quad by horizontal (XZ) length, so a near-
    ///   vertical streak was culled (xz≈0 &lt; 0.01) or collapsed into a flickering square.
    /// Computing the head analytically and stretching a real world-space mesh by the true 3D length
    /// has neither failure mode.
    ///
    /// Geometry is a real 3D cylinder oriented ALONG the flight axis — a tube looks the same from
    /// every side, so there is no camera-facing/billboard math (no camera dependency at all). An
    /// orthonormal basis is built perpendicular to the axis and the unit cylinder is stretched by the
    /// streak length and tracer diameter. Built once into a shared mesh.
    ///
    /// All per-frame work runs on the MAIN THREAD: tracer count is a handful of rounds, so the
    /// orientation math is a couple of cross products and there is no Burst job and no
    /// <c>Dependency.Complete()</c> sync point (the very sync class we are avoiding by not going
    /// through BRG). Each live tracer is stretched into a transform and submitted immediately; expiry
    /// is deferred to the vanilla <c>EndFrameBarrier</c> ECB (1-frame latency, no completion).
    ///
    /// Per-tracer colour comes from a pooled MaterialPropertyBlock.
    ///
    /// Two passes per tracer: (1) the additive emissive glow above, and (2) an opaque alpha-blended
    /// CORE drawn slightly thinner inside it. The glow is pure additive, so under the bright daytime
    /// HDRP auto-exposure its HDR boost is divided down and sinks into the lit sky (readable by night,
    /// faint by day). The core overwrites the background with its own colour instead of adding to it,
    /// so the streak stays visible by day independent of exposure. It keeps the tracer colour at full
    /// saturation along the whole streak and fades only its tail's opacity (its own white-rgb /
    /// falloff-alpha gradient), so the tail dissolves into the scene instead of darkening to an opaque
    /// black cord. See <c>CORE_DIAMETER</c> / <c>BuildCoreMaterial</c>.
    ///
    /// Phase (Axiom 7, registration-site only): <c>SystemUpdatePhase.Rendering</c>. RenderMesh
    /// registers a one-frame intermediate renderer that HDRP draws regardless of in-frame system
    /// order, so no ordering relative to OverlayRenderSystem is needed any more.
    ///
    /// Pause (Axiom 14): Rendering ticks during pause, but the per-frame delta comes from
    /// <c>UnityEngine.Time.time</c> (frozen while paused), so a live tracer's lifetime and head
    /// position freeze in pause instead of advancing. No new tracers spawn in pause (the producer is
    /// in GameSimulation). No other system depends on tracer timing.
    ///
    /// Save/load: tracers carry no render archetype and the draw is immediate-mode, so there is no
    /// persistent render state and no restored-entity crash to purge.
    ///
    /// Visual-only system — no gameplay impact.
    /// </summary>
    [ActIndependent]
    [HotPathSystem]
    public partial class TracerRenderSystem : CivicSystemBase
    {
        private static readonly LogContext Log = new("TracerRenderSystem");

        // Streak geometry. Tail ~20m gives a short real-tracer dash; a ~1.2m diameter reads as a
        // round bolt at city camera distance without becoming a pillar.
        private const float TAIL_LENGTH = 20f;
        private const float DIAMETER = 1.2f;

        // Opaque core diameter, drawn slightly thinner than the additive glow so it sits as a solid
        // cord inside the halo. The glow is pure additive/emissive and therefore washes out under the
        // daytime HDRP auto-exposure (its HDR boost is divided by a high exposure and sinks into the
        // bright sky). The core is alpha-blended with its own base colour, so it OVERWRITES the
        // background instead of adding to it — the streak stays readable by day regardless of exposure,
        // while the glow keeps the night look. Tunable after the first runtime pass.
        private const float CORE_DIAMETER = 0.7f;

        // Head→tail brightness gradient baked into an emissive map. Power > 1 concentrates brightness
        // at the head (v=1) and fades the tail (v=0) toward 0 so, under additive, the tail vanishes.
        private const int GRADIENT_RESOLUTION = 64;
        private const float HEAD_FALLOFF_POWER = 2.5f;
        private const float INV_GRADIENT_MAX = 1f / (GRADIENT_RESOLUTION - 1);

        // Opaque-core tail falloff. Gentler than the glow's so the daytime-visible body of the streak
        // reads for a longer stretch instead of collapsing to just the head. The core's gradient fades
        // ONLY the alpha (transparency) — its rgb stays the full tracer colour, so the tail dissolves
        // into the background instead of darkening to an opaque black cord.
        private const float CORE_HEAD_FALLOFF_POWER = 1.5f;

        // Emissive multiplier over the tracer colour so the additive tube reads as a hot, glowing
        // round at city distance. Tunable after the first runtime pass.
        private const float EMISSIVE_BOOST = 8f;

        // Minimum streak length (m) below which a just-spawned round has not travelled far enough to
        // stretch a tube — skip it for the one frame until it has.
        private const float MIN_STREAK_LENGTH = 0.01f;

        // Radial resolution of the procedural cylinder. 8 segments read as round at distance and keep
        // the shared mesh tiny.
        private const int RADIAL_SEGMENTS = 8;

        // Reciprocal of the segment count (compile-time const) so the UV/angle parameter avoids a
        // runtime division. Indices per radial quad = 2 triangles × 3 vertices.
        private const float INV_RADIAL_SEGMENTS = 1f / RADIAL_SEGMENTS;
        private const int INDICES_PER_SEGMENT = 6;

        // Above this |axis.y| the flight axis is treated as vertical and the basis reference flips to
        // world X, so cross(axis, reference) never degenerates.
        private const float VERTICAL_AXIS_THRESHOLD = 0.99f;

        private const float TWO_PI = 6.2831853f;

        // Hard ceiling on the number of tracers SUBMITTED to Graphics.RenderMesh per frame. A
        // saturated AA barrage spawns hundreds of concurrent tracer entities (TracerSpawnSystem =
        // one entity per round; bursts × stagger × ~0.8s lifetime), each drawn twice (glow + core),
        // so without a cap the submit count climbs past ~1000 RenderMesh calls/frame and tanks FPS.
        // Cap applies to DRAW only — lifetime/expiry decrements for every tracer regardless (see the
        // first pass in OnUpdateImpl). 512 preserves the "tracers everywhere over a night city" look at
        // full dense-AA saturation (peak ~200-400 live: ~20+ guns × burst 4-8 × 0.7-0.8s lifetime); the
        // cap only guards an absurd extreme. Tracer render was never a measured bottleneck (PERF.log
        // 0.3-0.5ms, not top-15) — this is a ceiling, not an optimization. Frustum-cull above already
        // drops off-screen tracers for free.
        private const int MAX_DRAWN_TRACERS = 512;

        private EntityQuery m_TracerQuery;
        private EndFrameBarrier m_EndFrameBarrier = null!;
        // Vanilla camera world position for nearest-first draw selection. Read via the registered
        // CameraUpdateSystem (Axiom 5/8) — NOT Camera.main in the hot path.
        private CameraUpdateSystem m_CameraSystem = null!;

        // Reused per-frame candidate buffer (drawable tracers + distance to camera). Persistent so a
        // saturated frame reuses the capacity instead of re-allocating; cleared each frame.
        private NativeList<TracerDraw> m_Candidates;

        // Reused frustum-plane buffer so the per-frame cull never allocates in the hot path.
        // Filled via the non-allocating CalculateFrustumPlanes overload.
        private const int FRUSTUM_PLANE_COUNT = 6; // a view frustum always has 6 planes (Unity GeometryUtility contract)
        private readonly Plane[] m_FrustumPlanes = new Plane[FRUSTUM_PLANE_COUNT];

        // One drawable tracer's precomputed geometry + colour + camera distance. Unmanaged so it
        // lives in a NativeList. Matrices are rebuilt in the second pass for the selected subset only.
        private struct TracerDraw
        {
            public float3 Mid;
            public float3 Axis;   // unit flight axis
            public float Len;     // streak length
            public float4 Color;
            public float DistanceSq;
        }

        private Mesh m_CylinderMesh = null!;
        private Material m_Material = null!;
        private Material m_CoreMaterial = null!;
        private Texture2D m_GradientTex = null!;
        // Core's own gradient: rgb=white (keeps the tracer colour saturated along the whole streak),
        // alpha=falloff (fades the tail to transparent). Separate from m_GradientTex, whose rgb=falloff
        // is required for the additive glow's emissive fade but would darken the opaque core to black.
        private Texture2D m_CoreGradientTex = null!;
        // One persistent MaterialPropertyBlock per concurrent draw slot — grown on demand, reused
        // across frames so per-tracer colour costs no per-frame GC allocation. Separate pools for the
        // additive glow (drives _EmissiveColor) and the opaque core (drives _UnlitColor); a shared MPB
        // would leak the core's opaque base colour into the additive pass and break its tail fade.
        private readonly List<MaterialPropertyBlock> m_Blocks = new();
        private readonly List<MaterialPropertyBlock> m_CoreBlocks = new();

        // Pause-safe frame delta (Time.time pauses with the game). Initialized negative so the very
        // first delta after construction/load is clamped to 0 (a 0-init would make `now - field`
        // large on a fresh load where time ≈ 0).
        private float m_LastTime = float.NegativeInfinity;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_TracerQuery = GetEntityQuery(ComponentType.ReadWrite<Tracer>());
            m_EndFrameBarrier = World.GetOrCreateSystemManaged<EndFrameBarrier>();
            m_CameraSystem = World.GetOrCreateSystemManaged<CameraUpdateSystem>();
            m_Candidates = new NativeList<TracerDraw>(MAX_DRAWN_TRACERS * 2, Allocator.Persistent);

            BuildMesh();
            BuildMaterial();
            BuildCoreMaterial();

            // PERF-LOCK: idle when no tracers — RequireForUpdate gate (+ the IsEmpty re-check in
            // OnUpdateImpl) means no query iteration and no RenderMesh submit between waves. Removing
            // this runs the whole render pass every frame even with zero live tracers.
            RequireForUpdate(m_TracerQuery);

            Log.Info("Created");
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            // Re-baseline on every start/load so the first frame's delta is ~0 rather than the gap
            // accumulated since construction (or a post-load Time.time discontinuity).
            m_LastTime = UnityEngine.Time.time;
        }

        protected override void OnDestroy()
        {
            if (m_Material != null)
                UnityEngine.Object.Destroy(m_Material);
            if (m_CoreMaterial != null)
                UnityEngine.Object.Destroy(m_CoreMaterial);
            if (m_CylinderMesh != null)
                UnityEngine.Object.Destroy(m_CylinderMesh);
            if (m_GradientTex != null)
                UnityEngine.Object.Destroy(m_GradientTex);
            if (m_CoreGradientTex != null)
                UnityEngine.Object.Destroy(m_CoreGradientTex);
            if (m_Candidates.IsCreated)
                m_Candidates.Dispose();
            Log.Info("Destroyed");
            base.OnDestroy();
        }

        protected override void OnUpdateImpl()
        {
            // PERF-LOCK: idle when no tracers — RequireForUpdate gate + this IsEmpty re-check so
            // nothing is touched unless there is a tracer to draw or expire.
            if (m_TracerQuery.IsEmpty)
                return;
            if (m_Material == null)
                return; // shader missing — already logged at create; nothing renders

            float now = UnityEngine.Time.time;
            float dt = now - m_LastTime;
            m_LastTime = now;
            // First tick after a negative-infinity baseline (or any backward Time.time) → no advance.
            if (dt < 0f || float.IsInfinity(dt)) dt = 0f;
            // Clamp large gaps. RequireForUpdate idles this system while no tracer exists, so
            // m_LastTime freezes between waves — the first frame after a spawn would otherwise see a
            // multi-second dt and instantly expire the just-spawned tracer. Cap to a typical frame
            // step so a fresh tracer lives.
            else if (dt > 0.1f) dt = 0.1f;

            var rp = new RenderParams(m_Material)
            {
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false,
                // No motion vectors: the streak moves tens of metres per frame and would otherwise be
                // smeared by motion blur (mirror of the drone motion-vector handling).
                motionVectorMode = MotionVectorGenerationMode.ForceNoMotion
            };

            // Second pass: opaque alpha-blended core inside the glow. Null only if the shader was
            // missing at build (already logged); then the glow alone renders.
            bool hasCore = m_CoreMaterial != null;
            var rpCore = hasCore
                ? new RenderParams(m_CoreMaterial)
                {
                    shadowCastingMode = ShadowCastingMode.Off,
                    receiveShadows = false,
                    motionVectorMode = MotionVectorGenerationMode.ForceNoMotion
                }
                : default;

            // ECB allocated lazily — only when a tracer actually expires this frame (most frames none
            // do). The expiry destroy is deferred to EndFrameBarrier (1-frame latency, no completion).
            EntityCommandBuffer ecb = default;
            bool hasEcb = false;

            // Camera world position for nearest-first selection + frustum planes for cull. Vanilla
            // camera, read via the registered system (Axiom 5/8), never Camera.main in the loop.
            float3 cameraPos = m_CameraSystem.position;
            Camera activeCamera = m_CameraSystem.activeCamera;
            bool hasFrustum = activeCamera != null;
            if (activeCamera != null)
                GeometryUtility.CalculateFrustumPlanes(activeCamera, m_FrustumPlanes);

            // PASS 1 — lifecycle for ALL tracers (decrement + expiry), collect drawable + visible
            // candidates. Lifecycle MUST run for every tracer regardless of the draw cap, otherwise a
            // capped-out tracer would never decrement → never expire → entity leak. The cap below
            // throttles only the RenderMesh submit, never the destroy.
            m_Candidates.Clear();

            foreach (var (tracerRef, entity) in SystemAPI.Query<RefRW<Tracer>>().WithEntityAccess())
            {
                ref Tracer tracer = ref tracerRef.ValueRW;
                tracer.RemainingLife -= dt;

                if (tracer.RemainingLife <= 0f)
                {
                    if (!hasEcb)
                    {
                        ecb = m_EndFrameBarrier.CreateCommandBuffer();
                        hasEcb = true;
                    }
                    ecb.DestroyEntity(entity);
                    continue;
                }

                // Elapsed flight time → head position along the ray. Lifetime is constant after spawn,
                // so elapsed = Lifetime - RemainingLife (pause-correct: RemainingLife only advances on
                // rendered, unpaused frames). RemainingLife is seeded as Lifetime + FireDelay, so elapsed
                // stays negative while the round is still waiting its burst-stagger delay in the barrel.
                float elapsed = tracer.Lifetime - tracer.RemainingLife;
                if (elapsed <= 0f)
                    continue; // burst round not launched yet (still inside its FireDelay) — draw nothing
                float travelled = tracer.Speed * elapsed;
                float3 head = tracer.Origin + tracer.Direction * travelled;

                // Clamp the tail to the distance already travelled so a just-spawned round does not
                // draw a streak that pokes behind its own origin.
                float tail = math.min(TAIL_LENGTH, travelled);
                float3 start = head - tracer.Direction * tail;

                float3 axis = head - start;
                float len = math.length(axis);
                if (len < MIN_STREAK_LENGTH)
                    continue; // just spawned — nothing to stretch yet (invisible this one frame)
                axis /= len;

                float3 mid = (start + head) * 0.5f;

                // PERF-LOCK: frustum-cull before collecting — a tracer outside the view frustum is
                // never submitted (was always submitted before; worldBounds was set but the draw was
                // never skipped). Covers "look away → no tracer render load".
                float extent = math.max(len, DIAMETER);
                if (hasFrustum)
                {
                    var bounds = new Bounds(
                        new Vector3(mid.x, mid.y, mid.z),
                        new Vector3(extent * 2f, extent * 2f, extent * 2f));
                    if (!GeometryUtility.TestPlanesAABB(m_FrustumPlanes, bounds))
                        continue;
                }

                m_Candidates.Add(new TracerDraw
                {
                    Mid = mid,
                    Axis = axis,
                    Len = len,
                    Color = tracer.Color,
                    DistanceSq = math.lengthsq(mid - cameraPos)
                });
            }

            // PERF-LOCK: tracer draw capped at MAX_DRAWN_TRACERS, nearest-to-camera first — without
            // this, a saturated barrage submits 1000+ RenderMesh/frame and regresses FPS. Removing
            // the cap or the partial-select restores the unbounded submit. Lifetime is unaffected
            // (PASS 1 above expires every tracer regardless of the cap).
            int candidateCount = m_Candidates.Length;
            int drawCount = math.min(candidateCount, MAX_DRAWN_TRACERS);
            if (candidateCount > MAX_DRAWN_TRACERS)
                SelectNearest(m_Candidates, MAX_DRAWN_TRACERS);

            // PASS 2 — submit the selected subset (glow + optional core).
            for (int drawIndex = 0; drawIndex < drawCount; drawIndex++)
            {
                TracerDraw d = m_Candidates[drawIndex];
                float3 mid = d.Mid;
                float3 axis = d.Axis;
                float len = d.Len;

                // Orthonormal basis perpendicular to the flight axis (a 3D tube is identical from all
                // sides → no billboard / camera math). Flip the reference axis near vertical so the
                // cross product never degenerates.
                float3 reference = math.abs(axis.y) > VERTICAL_AXIS_THRESHOLD
                    ? new float3(1f, 0f, 0f)
                    : new float3(0f, 1f, 0f);
                float3 right = math.normalize(math.cross(axis, reference));
                float3 fwd = math.cross(axis, right); // already unit: axis ⟂ right, both unit

                // Columns map the unit cylinder (radius 0.5 in XZ, y-span [-0.5,0.5] = length):
                // x→right*DIAMETER, y→axis*len, z→fwd*DIAMETER, translation→mid.
                var matrix = new Matrix4x4(
                    new float4(right * DIAMETER, 0f),
                    new float4(axis * len, 0f),
                    new float4(fwd * DIAMETER, 0f),
                    new float4(mid, 1f));

                while (m_Blocks.Count <= drawIndex)
                    m_Blocks.Add(new MaterialPropertyBlock());
                MaterialPropertyBlock mpb = m_Blocks[drawIndex];
                var color = new Color(d.Color.x, d.Color.y, d.Color.z, d.Color.w);
                // Per-tracer colour drives emissive only; the shared gradient map fades it head→tail.
                // _UnlitColor stays black on the material so the tail goes fully dark under additive.
                mpb.SetColor(ShaderIds.EmissiveColor, color * EMISSIVE_BOOST);
                rp.matProps = mpb;

                float extent = math.max(len, DIAMETER);
                rp.worldBounds = new Bounds(
                    new Vector3(mid.x, mid.y, mid.z),
                    new Vector3(extent * 2f, extent * 2f, extent * 2f));

                Graphics.RenderMesh(in rp, m_CylinderMesh, 0, matrix);

                if (hasCore)
                {
                    // Same axis/length, thinner radius → a solid cord threaded through the halo.
                    var coreMatrix = new Matrix4x4(
                        new float4(right * CORE_DIAMETER, 0f),
                        new float4(axis * len, 0f),
                        new float4(fwd * CORE_DIAMETER, 0f),
                        new float4(mid, 1f));

                    while (m_CoreBlocks.Count <= drawIndex)
                        m_CoreBlocks.Add(new MaterialPropertyBlock());
                    MaterialPropertyBlock coreMpb = m_CoreBlocks[drawIndex];
                    // Force opaque head (alpha 1) regardless of the tracer's own alpha; the gradient
                    // map's alpha channel still fades the tail to transparent. The opaque base colour is
                    // what survives daytime exposure where the additive glow washes out.
                    coreMpb.SetColor(ShaderIds.UnlitColor, new Color(d.Color.x, d.Color.y, d.Color.z, 1f));
                    rpCore.matProps = coreMpb;
                    rpCore.worldBounds = rp.worldBounds;

                    Graphics.RenderMesh(in rpCore, m_CylinderMesh, 0, coreMatrix);
                }
            }
        }

        // Partial selection: move the <paramref name="count"/> nearest-to-camera candidates (smallest
        // DistanceSq) to the front of the list via Hoare quickselect. Avoids a full sort of a
        // saturated candidate set — only the kept subset needs to be at the front; their internal
        // order does not matter (each carries its own MaterialPropertyBlock).
        private static void SelectNearest(NativeList<TracerDraw> list, int count)
        {
            int lo = 0;
            int hi = list.Length - 1;
            while (lo < hi)
            {
                float pivot = list[(lo + hi) / 2].DistanceSq;
                int i = lo;
                int j = hi;
                while (i <= j)
                {
                    while (list[i].DistanceSq < pivot) i++;
                    while (list[j].DistanceSq > pivot) j--;
                    if (i <= j)
                    {
                        (list[i], list[j]) = (list[j], list[i]);
                        i++;
                        j--;
                    }
                }
                // Recurse into only the partition that contains the cut point (count-th element).
                if (count - 1 <= j) hi = j;
                else if (count - 1 >= i) lo = i;
                else break;
            }
        }

        private void BuildMesh()
        {
            m_CylinderMesh = new Mesh { name = "CivicTracerCylinder" };

            int segs = RADIAL_SEGMENTS;
            int ringVerts = segs + 1; // duplicate the seam vertex so UVs wrap cleanly
            var vertices = new List<Vector3>(ringVerts * 2);
            var normals = new List<Vector3>(ringVerts * 2);
            var uvs = new List<Vector2>(ringVerts * 2);

            // Open tube (no end caps): cheap, and under additive + CullMode.Off the open ends read as
            // a soft glow rather than holes. Radius 0.5 in XZ, length span [-0.5, 0.5] along Y.
            for (int i = 0; i < ringVerts; i++)
            {
                float t = i * INV_RADIAL_SEGMENTS;
                float angle = t * TWO_PI;
                float cos = math.cos(angle);
                float sin = math.sin(angle);
                var radial = new Vector3(cos, 0f, sin);

                vertices.Add(new Vector3(0.5f * cos, -0.5f, 0.5f * sin)); // bottom ring
                normals.Add(radial);
                uvs.Add(new Vector2(t, 0f));

                vertices.Add(new Vector3(0.5f * cos, 0.5f, 0.5f * sin)); // top ring
                normals.Add(radial);
                uvs.Add(new Vector2(t, 1f));
            }

            // Two triangles per radial quad. Interleaved layout: bottom = 2i, top = 2i + 1.
            var triangles = new List<int>(segs * INDICES_PER_SEGMENT);
            for (int i = 0; i < segs; i++)
            {
                int b0 = 2 * i;
                int t0 = 2 * i + 1;
                int b1 = 2 * (i + 1);
                int t1 = 2 * (i + 1) + 1;
                triangles.Add(b0); triangles.Add(t0); triangles.Add(b1);
                triangles.Add(b1); triangles.Add(t0); triangles.Add(t1);
            }

            m_CylinderMesh.SetVertices(vertices);
            m_CylinderMesh.SetNormals(normals);
            m_CylinderMesh.SetUVs(0, uvs);
            m_CylinderMesh.SetTriangles(triangles, 0);
            // Local bounds of the unit cylinder; the per-renderer worldBounds set per draw governs culling.
            m_CylinderMesh.bounds = new Bounds(Vector3.zero, Vector3.one);
        }

        private void BuildMaterial()
        {
            Shader? shader = Shader.Find("HDRP/Unlit");
            if (shader == null)
            {
                Log.Error("HDRP/Unlit shader not found — tracers will not render");
                return;
            }

            m_Material = new Material(shader) { name = "CivicTracerStreak" };
            // Transparent + additive so the tube glows and adds over the scene; ZWrite off and the
            // HDRP transparent depth test (LEqual) keeps it hidden behind buildings/terrain.
            m_Material.SetFloat(ShaderIds.SurfaceType, 1f);              // Transparent
            m_Material.SetFloat(ShaderIds.BlendMode, 1f);               // Additive
            m_Material.SetFloat(ShaderIds.ZWrite, 0f);
            m_Material.SetFloat(ShaderIds.CullMode, (float)CullMode.Off); // visible from both faces
            // Unlit base stays black so only emissive contributes — under additive the tail then
            // darkens fully to nothing. Per-tracer colour comes from the MaterialPropertyBlock.
            m_Material.SetColor(ShaderIds.UnlitColor, Color.black);
            m_Material.SetColor(ShaderIds.EmissiveColor, Color.black);

            // Head→tail brightness via an emissive map sampled along the cylinder length (UV.v):
            // bright at the head (v=1), fading to 0 at the tail (v=0). HDRP multiplies the map by the
            // per-draw _EmissiveColor, so each tracer keeps its colour while the tail darkens out.
            m_GradientTex = BuildGradientTexture();
            m_Material.SetTexture(ShaderIds.EmissiveColorMap, m_GradientTex);
            // Use _EmissiveColor directly as HDR (no separate LDR×intensity path).
            m_Material.SetFloat(ShaderIds.UseEmissiveIntensity, 0f);
            // Sync keywords / blend state / render queue from the properties above (vanilla path).
            HDMaterial.ValidateMaterial(m_Material);
            // Belt-and-suspenders: ValidateMaterial enables the emissive-map keyword from the non-null
            // map, but enforce it so the gradient is definitely sampled.
            m_Material.EnableKeyword("_EMISSIVE_COLOR_MAP");
        }

        private void BuildCoreMaterial()
        {
            Shader? shader = Shader.Find("HDRP/Unlit");
            if (shader == null)
            {
                Log.Error("HDRP/Unlit shader not found — tracer core will not render");
                return;
            }

            m_CoreMaterial = new Material(shader) { name = "CivicTracerCore" };
            // Transparent + ALPHA blend (over), not additive: the core overwrites the background with
            // its own colour so it stays visible under bright daytime exposure, where the additive glow
            // washes out. ZWrite off + HDRP transparent depth test keeps it hidden behind geometry.
            m_CoreMaterial.SetFloat(ShaderIds.SurfaceType, 1f);              // Transparent
            m_CoreMaterial.SetFloat(ShaderIds.BlendMode, 0f);               // Alpha (src-over)
            m_CoreMaterial.SetFloat(ShaderIds.ZWrite, 0f);
            m_CoreMaterial.SetFloat(ShaderIds.CullMode, (float)CullMode.Off);
            // Per-tracer base colour comes from the MPB; white here so the MPB colour passes through
            // unmodulated. The core gradient is white-rgb / falloff-alpha, so it leaves the colour at
            // full saturation along the whole streak and fades ONLY the tail's opacity — the tail
            // dissolves into the scene rather than darkening to an opaque black cord.
            m_CoreGradientTex = BuildCoreGradientTexture();
            m_CoreMaterial.SetColor(ShaderIds.UnlitColor, Color.white);
            m_CoreMaterial.SetTexture(ShaderIds.UnlitColorMap, m_CoreGradientTex);
            // Sync keywords / blend state / render queue from the properties above (vanilla path).
            HDMaterial.ValidateMaterial(m_CoreMaterial);
        }

        private Texture2D BuildGradientTexture()
        {
            // 1-wide × GRADIENT_RESOLUTION-tall: UV.v (length) selects the row, UV.u (around the tube)
            // is constant. Linear so the brightness multiplies the linear-space emissive colour.
            var tex = new Texture2D(1, GRADIENT_RESOLUTION, TextureFormat.RGBA32, mipChain: false, linear: true)
            {
                name = "CivicTracerGradient",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            var pixels = new Color[GRADIENT_RESOLUTION];
            for (int y = 0; y < GRADIENT_RESOLUTION; y++)
            {
                // Row 0 = tail (v=0), top row = head (v=1). Power curve concentrates brightness at the
                // head so it "burns" and the tail fades quickly to black.
                float v = y * INV_GRADIENT_MAX;
                float b = math.pow(v, HEAD_FALLOFF_POWER);
                pixels[y] = new Color(b, b, b, b);
            }

            tex.SetPixels(pixels);
            tex.Apply(updateMipmaps: false);
            return tex;
        }

        private Texture2D BuildCoreGradientTexture()
        {
            // Same layout as the glow gradient (1×N, UV.v selects the row along the streak length), but
            // rgb stays white so the opaque core keeps its full tracer colour from head to tail; only
            // the alpha channel carries the falloff so the tail fades to transparent, never to black.
            var tex = new Texture2D(1, GRADIENT_RESOLUTION, TextureFormat.RGBA32, mipChain: false, linear: true)
            {
                name = "CivicTracerCoreGradient",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            var pixels = new Color[GRADIENT_RESOLUTION];
            for (int y = 0; y < GRADIENT_RESOLUTION; y++)
            {
                float v = y * INV_GRADIENT_MAX;
                float a = math.pow(v, CORE_HEAD_FALLOFF_POWER);
                pixels[y] = new Color(1f, 1f, 1f, a);
            }

            tex.SetPixels(pixels);
            tex.Apply(updateMipmaps: false);
            return tex;
        }

        private static class ShaderIds
        {
            public static readonly int SurfaceType = Shader.PropertyToID("_SurfaceType");
            public static readonly int BlendMode = Shader.PropertyToID("_BlendMode");
            public static readonly int ZWrite = Shader.PropertyToID("_ZWrite");
            public static readonly int CullMode = Shader.PropertyToID("_CullMode");
            public static readonly int UnlitColor = Shader.PropertyToID("_UnlitColor");
            public static readonly int UnlitColorMap = Shader.PropertyToID("_UnlitColorMap");
            public static readonly int EmissiveColor = Shader.PropertyToID("_EmissiveColor");
            public static readonly int EmissiveColorMap = Shader.PropertyToID("_EmissiveColorMap");
            public static readonly int UseEmissiveIntensity = Shader.PropertyToID("_UseEmissiveIntensity");
        }
    }
}
