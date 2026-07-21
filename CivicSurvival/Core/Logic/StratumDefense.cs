using Unity.Burst;
using Unity.Mathematics;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Logic
{
    /// <summary>
    /// Single source of the two orthogonal countermeasure-effectiveness rules:
    ///
    /// 1. <b>Hero ↔ attack TYPE</b> (rock-paper-scissors): the active archetype hard-counters
    ///    exactly one <see cref="PsyOpsAttackType"/> (Voice←Rumor, Arestovych←FakeVideo,
    ///    Patriot←Propaganda) and is weak against the other two. This dampens the discrete
    ///    PSYOPS impact that feeds cognitive infection — effectiveness is by TYPE, never by
    ///    household stratum.
    ///
    /// 2. <b>Tool ↔ STRATUM</b>: Telemarathon is strongest on the middle stratum, Buckwheat on
    ///    the poor; both still blanket every stratum at a reduced floor. Some (tool × stratum)
    ///    pairs are <b>backfire</b> — a negative value (e.g. Buckwheat→wealthy reads as
    ///    humiliation, Patriot→wealthy accelerates capital flight) so the wrong tool actively
    ///    worsens control instead of merely wasting the resource. Values span <c>[-1..1]</c>:
    ///    positive = added defence, negative = added pressure.
    ///
    /// Pure / Burst-safe (no <c>throw</c>, no allocation): every config magnitude is passed in
    /// by the caller so the same rule serves both <see cref="ExposureCalculator"/> (per-household
    /// infection) and the cognitive coverage projection (posture map). Shared so the two cannot
    /// drift.
    /// </summary>
#if ENABLE_BURST
    [BurstCompile]
#endif
    public static class StratumDefense
    {
        /// <summary>True when <paramref name="archetype"/> hard-counters <paramref name="type"/> (the RPS triangle).</summary>
        public static bool Counters(HeroArchetype archetype, PsyOpsAttackType type) => archetype switch
        {
            HeroArchetype.Voice => type == PsyOpsAttackType.Rumor,
            HeroArchetype.Arestovych => type == PsyOpsAttackType.FakeVideo,
            HeroArchetype.Patriot => type == PsyOpsAttackType.Propaganda,
            _ => false
        };

        /// <summary>
        /// Hero effectiveness against a discrete attack TYPE, in <c>[0..1]</c>. <c>counterStrong</c>
        /// for the countered type, <c>counterFloor</c> for the other two. Returns 0 when no hero is
        /// active (no damping). The caller passes <c>counterStrong = counterFloor</c> to express a
        /// temporary suppression (Arestovych trust-debt collapse — the exposed lie loses its bite) —
        /// always via <see cref="EffectiveHeroCounterStrong"/>, never a hand-rolled ternary.
        /// </summary>
        public static float HeroTypeEffectiveness(HeroArchetype archetype, PsyOpsAttackType type, bool active, float counterStrong, float counterFloor)
        {
            if (!active)
                return 0f;
            return math.saturate(Counters(archetype, type) ? counterStrong : counterFloor);
        }

        /// <summary>
        /// The effective "strong" counter magnitude under the temporary suppression latch:
        /// suppressed → <paramref name="counterFloor"/> (the contract <see cref="HeroTypeEffectiveness"/>
        /// documents — the collapsed counter loses its edge), else the configured
        /// <paramref name="counterStrong"/>. One implementation so the call sites (infection resolver,
        /// coverage posture, UI) cannot drift.
        /// </summary>
        public static float EffectiveHeroCounterStrong(float counterStrong, float counterFloor, bool suppressed)
            => suppressed ? counterFloor : counterStrong;

        /// <summary>
        /// Telemarathon (state media) per-stratum effectiveness, in <c>[0..1]</c>: <c>strong</c> on
        /// the middle stratum (the TV audience), <c>floor</c> elsewhere (blanket coverage, reduced).
        /// Ternary (not a switch) so the Unknown/unclassified case folds into the floor without a
        /// silent-discard arm — Burst-safe, no throw.
        /// </summary>
        public static float TelemarathonStratumDefense(SocialStratum stratum, float strong, float floor)
            => math.saturate(stratum == SocialStratum.Middle ? strong : floor);

        /// <summary>
        /// Buckwheat (food aid) per-stratum effectiveness, in <c>[-1..1]</c>: <c>poorStrong</c> on the
        /// poor (food calms the street), <c>floor</c> on the middle, <c>wealthyBackfire</c> (negative)
        /// on the wealthy — handing out aid parcels to the rich reads as humiliation and erodes control.
        /// Unknown (unclassified) → 0 (neutral). Ternary chain — Burst-safe, no silent-discard switch.
        /// </summary>
        public static float BuckwheatStratumDefense(SocialStratum stratum, float poorStrong, float floor, float wealthyBackfire)
        {
            float value;
            if (stratum == SocialStratum.Poor) value = poorStrong;
            else if (stratum == SocialStratum.Middle) value = floor;
            else if (stratum == SocialStratum.Wealthy) value = wealthyBackfire;
            else value = 0f; // Unknown / unclassified → neutral
            return math.clamp(value, -1f, 1f);
        }

        /// <summary>
        /// Hero per-stratum tilt, in <c>[-1..1]</c> — the archetype's side effect on a stratum,
        /// distinct from its type-counter. Patriot "presses the poor" (mild negative) and drives the
        /// wealthy to flee (<c>patriotWealthyBackfire</c>, negative — synergy with its exodus push);
        /// Voice and Arestovych carry no stratum tilt. Zero when no hero is active. Ternary —
        /// Burst-safe, no silent-discard switch.
        /// </summary>
        public static float HeroStratumTilt(HeroArchetype archetype, SocialStratum stratum, bool active, float patriotWealthyBackfire, float patriotPoorPress)
        {
            if (!active || archetype != HeroArchetype.Patriot)
                return 0f;
            float value;
            if (stratum == SocialStratum.Wealthy) value = patriotWealthyBackfire;
            else if (stratum == SocialStratum.Poor) value = patriotPoorPress;
            else value = 0f; // middle / unclassified → no tilt
            return math.clamp(value, -1f, 1f);
        }
    }
}
