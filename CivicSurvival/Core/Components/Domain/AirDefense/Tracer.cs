using Colossal.Serialization.Entities;
using Unity.Entities;
using Unity.Mathematics;

namespace CivicSurvival.Core.Components.Domain.AirDefense
{
    /// <summary>
    /// AA tracer round — visual-only entity flying from AA to threat.
    /// Short lifetime (~0.5-1s), straight line.
    ///
    /// EPHEMERAL: destroyed within ~1 second, empty serialization.
    ///
    /// Rendered as a 3D emissive cylinder stretched along the flight ray via <c>Graphics.RenderMesh</c>
    /// (NOT a BRG mesh entity): this component carries the full flight state (origin, direction, speed,
    /// remaining life) so the render system computes the moving head position and orients a tube mesh
    /// along the ray each frame. There is NO render archetype, no Transform/TransformFrame/MeshBatch —
    /// the tracer never enters the OIS / culling / batch pipeline. This is why the previous BRG path failed:
    /// OIS interpolates ~0.5s in the past, so a 1100 m/s vertical tracer's interpolated position
    /// dropped to the spawn point (underground); a directly-submitted world-space quad has no such
    /// lag (and no horizontal-projection gate, unlike the OverlayRenderSystem line path).
    ///
    /// Written by: <c>TracerSpawnSystem</c> (spawn).
    /// Read by: <c>TracerRenderSystem</c> (per-frame streak submit + lifetime decrement + expiry).
    /// </summary>
    public struct Tracer : IComponentData, IEmptySerializable
    {
        /// <summary>World spawn position (AA barrel origin) — the tail anchor of the flight ray.</summary>
        public float3 Origin;

        /// <summary>Normalized flight direction.</summary>
        public float3 Direction;

        /// <summary>Flight speed in m/s (~800 Bofors, ~1100 Gepard, ~1700 Patriot).</summary>
        public float Speed;

        /// <summary>Total lifetime in seconds (constant after spawn) — used to derive elapsed flight time.</summary>
        public float Lifetime;

        /// <summary>Seconds until destroy (decremented each rendered frame; freezes in pause). Seeded as
        /// Lifetime + FireDelay so a staggered burst round still flies its full Lifetime after launch.</summary>
        public float RemainingLife;

        /// <summary>
        /// Burst stagger: seconds this round waits in the barrel before it launches, so the rounds of one
        /// AA burst leave sequentially (a stream) instead of all at once (a fan). Round i of a burst gets
        /// i * BURST_GAP; the render skips the round while its launch delay has not elapsed. Zero for a
        /// single-round shot (e.g. Patriot).
        /// </summary>
        public float FireDelay;

        /// <summary>Streak colour (linear-space RGBA) — multiplied into the emissive of the tracer material.</summary>
        public float4 Color;

        public void SetDefaults()
        {
            this = default;
        }

        // IEmptySerializable marker: ephemeral tracer (destroyed within ~1s) — no
        // persisted payload. A tracer restored from a save (none should exist) simply
        // expires on the first rendered frame; it carries no render components, so it
        // cannot crash the vanilla batch pipeline the way a restored BRG entity did.
    }
}
