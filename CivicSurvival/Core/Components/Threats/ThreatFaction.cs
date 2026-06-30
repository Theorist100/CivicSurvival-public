using Unity.Entities;

namespace CivicSurvival.Core.Components.Threats
{
    /// <summary>
    /// Faction marker for the otherwise one-sided threat pipeline: an <b>enabled</b>
    /// <see cref="PlayerOutboundThreat"/> bit means "this projectile is the player's outbound
    /// counter-strike", not a wave threat inbound on the city. Absence (or a disabled bit) =
    /// <c>EnemyInbound</c> — the default, so every existing wave threat behaves exactly as
    /// before. This lifts the implicit "threat == enemy of the city" invariant that every
    /// pipeline consumer (AA candidate query, defensive threat counts, city terminalization)
    /// relied on.
    ///
    /// Why an enableable tag and not a <c>byte Faction</c> field on <c>Shahed</c>/<c>Ballistic</c>
    /// (Axiom 11, no per-byte hot-loop reads): the faction filter on the AA candidate query and
    /// the defensive threat counters must be expressed as a query filter
    /// (<c>WithNone</c>/<c>Exclude</c>), not a byte test inside a high-frequency chunk loop —
    /// that is the perf invariant of the targeting/scan path. An enableable bit lets the single
    /// shared drone/ballistic render archetype carry the marker for both factions: the consumer
    /// (<c>ThreatSpawnApplySystem</c>, Modification4) enables it only for outbound projectiles,
    /// leaving inbound waves with the bit disabled so <c>Exclude&lt;PlayerOutboundThreat&gt;</c>
    /// (which excludes chunks where the bit is enabled) keeps inbound behavior untouched.
    ///
    /// Why enableable rather than a plain present/absent tag: the drone and ballistic render
    /// archetypes are shared by both factions (<c>ThreatSpawnApplySystem.m_DroneArchetype</c> /
    /// <c>m_BallisticArchetype</c>). A plain tag in the archetype would be present on every
    /// threat, so "inbound == absent" would be unreachable. The bit is added to the archetype
    /// disabled by default (mirror of <c>PendingDestruction</c>/<c>PendingThreatDeletion</c>),
    /// and only the outbound producer flips it on — exactly the
    /// "added disabled at spawn, flipped on by intent" pattern <c>PendingThreatDeletion</c>
    /// already uses.
    ///
    /// Not serialized (no <c>ISerializable</c>): like <c>ActiveThreat</c> /
    /// <c>PendingDestruction</c> / <c>PendingThreatDeletion</c>, this enableable tag has no
    /// serializer, so it is stripped on save/load (memory
    /// <c>colossal_enableable_tags_not_serialized</c>). For the inbound default that is exactly
    /// right — a restored threat with no tag is <c>EnemyInbound</c>, matching pre-faction
    /// behavior. Restoring an <i>outbound</i> projectile mid-flight across save/load (which does
    /// not exist until the outbound spawn lands in a later phase) would lose the bit and revert
    /// it to inbound; re-asserting the bit on load belongs to that later phase, not to this
    /// faction scaffold.
    /// </summary>
    public struct PlayerOutboundThreat : IComponentData, IEnableableComponent { }
}
