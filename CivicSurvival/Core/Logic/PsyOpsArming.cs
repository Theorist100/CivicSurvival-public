using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Logic
{
    /// <summary>
    /// Single source of truth for "does this wave fire a PsyOps raid" — a pure,
    /// domain-neutral predicate over the act and wave number. Lives in Core so both
    /// the wave clock (WaveExecutor, to decide whether to insert the PsyAttack phase)
    /// and the Cognitive raid launcher (PsyOpsLaunchSystem, to gate the raid) read the
    /// same rule without a cross-domain import (Axiom 5). The PsyAttack phase exists
    /// on a wave iff this returns true, keeping the phase and the raid in lock-step.
    /// </summary>
    public static class PsyOpsArming
    {
        /// <summary>
        /// First wave that carries a PsyOps raid. Wave 1 is always drones-only (a soft
        /// intro), so discrete info-war raids begin on wave 2.
        /// </summary>
        public const int MIN_RAID_WAVE = 2;

        /// <summary>
        /// True when the given wave fires a PsyOps raid: psy is armed from Crisis act
        /// onward AND the wave is at or past <see cref="MIN_RAID_WAVE"/>.
        /// </summary>
        public static bool WillFire(Act act, int waveNumber)
            => act >= Act.Crisis && waveNumber >= MIN_RAID_WAVE;
    }
}
