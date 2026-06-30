using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Components.Domain.GridWarfare;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Interfaces.Threats
{
    /// <summary>
    /// Cross-domain boundary for launching a player's outbound counter-strike (Axiom 5).
    /// Implemented by the Waves producer (<c>ThreatSpawnSystem</c>), which already owns the
    /// threat prefabs, map bounds, RNG, and the off-barrier <c>ThreatSpawnIntent</c> append
    /// path. The GridWarfare effect owner (<c>EnemyOperationEffectSystem</c>) crosses through
    /// this interface to fire a projectile instead of duplicating the spawn math or importing
    /// the Waves domain.
    ///
    /// <see cref="Launch"/> is a synchronous main-thread append into the spawn-intent buffer
    /// (NOT a structural change), so it is safe — and pause-safe — to call from ModificationEnd
    /// where the effect owner commits. The actual <c>CreateEntity</c> happens render-safe in
    /// <c>ThreatSpawnApplySystem</c> (Modification4, after the render-completion gate), exactly
    /// like every wave spawn. Null-object semantics: when Waves is closed or the threat prefabs
    /// are not yet resolved, <see cref="Launch"/> returns false (fail-closed — the caller treats
    /// it like "no munition available" and does not commit the launch).
    /// </summary>
    [GenerateNullObject]
    [OwnedByFeatureId(FeatureIds.WavesName)]
    public interface IOutboundStrikeService
    {
        /// <summary>True once the threat prefabs are resolved and an outbound strike can be launched.</summary>
        [NullReturn(false)]
        bool CanLaunch { get; }

        /// <summary>
        /// Record an off-barrier <c>ThreatSpawnIntent</c> for a player outbound counter-strike:
        /// a projectile of <paramref name="kind"/> (drone/ballistic) launched from the player's
        /// map toward the frontier, carrying an <c>OutboundStrikePayload</c> of
        /// (<paramref name="axis"/>, <paramref name="damage"/>, <paramref name="seed"/>) resolved at
        /// arrival. <paramref name="seed"/> is the launch-frozen intercept-roll seed, drawn
        /// deterministically by the caller (not a runtime <c>Random</c>); it is recorded on the
        /// projectile's payload so the arrival verdict is reproducible after load and on a server.
        /// Returns false (and records nothing) when the producer cannot launch yet (prefabs
        /// unresolved / Waves closed) — the caller then leaves the operation uncommitted. Mirrors the
        /// wave producer's "fully resolve here, CreateEntity in the consumer" contract.
        /// </summary>
        [NullReturn(false)]
        bool Launch(ArsenalKind kind, AttackCategory axis, float damage, uint seed);
    }
}
