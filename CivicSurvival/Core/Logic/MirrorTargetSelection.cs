using CivicSurvival.Core.Components.Domain.GridWarfare;
using CivicSurvival.Core.Types;
using Unity.Entities;
using Unity.Mathematics;

namespace CivicSurvival.Core.Logic
{
    /// <summary>
    /// The ONE strike-target selection rule of the mirror-city model, shared by the two seams that
    /// must agree byte-for-byte: <c>PlayerAttackSystem.SelectAutoTarget</c> (Execute time — what the
    /// player is told the strike aims at) and <c>MirrorCitySystem.ApplyStrikeToTarget</c> (arrival
    /// time — what the strike actually lands on). Before this unit each side carried its own copy of
    /// the fold and the copies could drift; now both call here.
    ///
    /// The rule:
    ///   1. An explicit pick (a War Room selection / the payload's TargetId) is honoured while it
    ///      still names a LIVE target of the right axis — any destructible tier, including an AA
    ///      site (a deliberate SEAD strike).
    ///   2. Otherwise the automatic pick is the live CONTRIBUTING target of the axis with the
    ///      highest hp — reserve (invulnerable) and AA sites (zero axis contribution) are excluded,
    ///      so an auto strike never wastes itself on a target that cannot move the axis.
    ///   3. Ties within <see cref="HpTieEpsilon"/> break to the LOWEST id, deterministically; the
    ///      epsilon comparison is strict (a sub-epsilon hp advantage does not defeat the id
    ///      tie-break, so the declared "ties break to the lowest id" contract actually holds).
    ///
    /// Pure fold over the buffer — no World, no RNG, no config.
    /// </summary>
    public static class MirrorTargetSelection
    {
        /// <summary>Hp tie-break tolerance (float equality is unreliable).</summary>
        public const float HpTieEpsilon = 0.001f;

        /// <summary>
        /// Arrival-time resolution: the buffer index the strike lands on. Honours a still-valid
        /// explicit <paramref name="targetId"/>; otherwise the automatic rule above; otherwise the
        /// axis's (invulnerable) reserve. Returns -1 only if the axis has no target at all.
        /// </summary>
        public static int ResolveStrikeIndex(DynamicBuffer<EnemyTarget> buffer, ushort targetId, AttackCategory axis)
        {
            if (targetId != EnemyTarget.NoTargetId)
            {
                for (int i = 0; i < buffer.Length; i++)
                {
                    var t = buffer[i];
                    // Same tier contract as SelectAutoTargetId's preferred branch: an explicit id is
                    // never honoured on the invulnerable Reserve. A persisted TargetId can go stale
                    // across a city regeneration (ids are re-assigned) and land on a Reserve of the
                    // same axis — honouring it would burn the paid strike for zero effect, where the
                    // auto rule below re-picks a live contributing target instead.
                    if (t.Id == targetId && t.Axis == axis && t.Hp > 0f && t.Tier != MirrorTargetTier.Reserve)
                        return i;
                }
                // Explicit target invalid / dead / wrong axis / reserve → fall through to the automatic rule.
            }

            int best = -1;
            float bestHp = -1f;
            ushort bestId = 0;
            for (int i = 0; i < buffer.Length; i++)
            {
                var t = buffer[i];
                if (!IsAutoCandidate(t, axis))
                    continue;
                if (Beats(t, bestHp, bestId, best < 0))
                {
                    best = i;
                    bestHp = t.Hp;
                    bestId = t.Id;
                }
            }
            if (best >= 0)
                return best;

            // Axis has no live contributing target — route to its (invulnerable) reserve.
            for (int i = 0; i < buffer.Length; i++)
            {
                if (buffer[i].Axis == axis && buffer[i].Tier == MirrorTargetTier.Reserve)
                    return i;
            }
            return -1;
        }

        /// <summary>
        /// Execute-time auto-selection: the target id the operation records as its aim. Honours a
        /// still-valid <paramref name="preferred"/> pick (any live destructible tier, including a
        /// SEAD pick on an AA site); otherwise the automatic rule. Returns
        /// <see cref="EnemyTarget.NoTargetId"/> when nothing qualifies — arrival then re-resolves
        /// through <see cref="ResolveStrikeIndex"/>'s own fallback.
        /// </summary>
        public static ushort SelectAutoTargetId(DynamicBuffer<EnemyTarget> buffer, AttackCategory axis, ushort preferred)
        {
            ushort bestId = EnemyTarget.NoTargetId;
            float bestHp = -1f;
            for (int i = 0; i < buffer.Length; i++)
            {
                var t = buffer[i];
                if (t.Axis != axis || t.Hp <= 0f)
                    continue;

                // A still-valid explicit War Room pick wins outright over the auto rule.
                if (preferred != EnemyTarget.NoTargetId && t.Id == preferred && t.Tier != MirrorTargetTier.Reserve)
                    return t.Id;

                if (t.Tier == MirrorTargetTier.Reserve || t.Tier == MirrorTargetTier.AaSite)
                    continue;

                if (Beats(t, bestHp, bestId, bestId == EnemyTarget.NoTargetId))
                {
                    bestHp = t.Hp;
                    bestId = t.Id;
                }
            }
            return bestId;
        }

        private static bool IsAutoCandidate(in EnemyTarget t, AttackCategory axis)
            => t.Axis == axis
               && t.Hp > 0f
               && t.Tier != MirrorTargetTier.Reserve
               && t.Tier != MirrorTargetTier.AaSite;

        /// <summary>
        /// Strict highest-hp-then-lowest-id comparison: a candidate wins only with an hp advantage
        /// ABOVE the epsilon, or (within the epsilon) with a lower id.
        /// </summary>
        private static bool Beats(in EnemyTarget t, float bestHp, ushort bestId, bool noBestYet)
        {
            if (noBestYet)
                return true;
            if (t.Hp > bestHp + HpTieEpsilon)
                return true;
            return math.abs(t.Hp - bestHp) <= HpTieEpsilon && t.Id < bestId;
        }
    }
}
