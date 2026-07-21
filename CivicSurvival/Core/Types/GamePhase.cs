namespace CivicSurvival.Core.Types
{
    /// <summary>
    /// Game phase for wave-based threat system.
    /// Calm -> Alert -> Attack -> PsyAttack -> Recovery -> Repeat
    ///
    /// PsyAttack is the info-war beat: it follows the kinetic Attack and only
    /// exists on waves that actually fire a PsyOps raid (see PsyOpsArming). Waves
    /// without psy (wave 1, pre-Crisis) skip it and go Attack -> Recovery.
    ///
    /// Byte values are append-only (serialized as byte). The logical order used for
    /// phase-rank comparisons is Calm &lt; Alert &lt; Attack &lt; PsyAttack &lt; Recovery
    /// and lives in an explicit rank map (WaveScheduler.GetPhaseOrder), NOT in these
    /// byte values — PsyAttack = 4 stays after Recovery = 3 in the enum.
    ///
    /// ADD-A-PHASE CHECKLIST. Every C# switch on GamePhase is flagged by CIVIC525
    /// (EnforceGamePhaseSwitchExhaustive) until the new member is listed, so a plain
    /// rebuild finds the switch sites. The non-switch sites the compiler CANNOT see —
    /// visit each by hand when a member is added:
    ///   • WaveScheduler.GetPhaseOrder — the logical-rank map;
    ///   • WaveForecast.PhaseDuration / CrisisSweepRunner.RunPacing — the phase-cycle
    ///     sum (arithmetic model, no switch);
    ///   • TS wire union WavePhase in UI/src/types/dtoSubTypes.ts (+ isWavePhase and
    ///     isActiveAssaultPhase beside it) and getPhaseColors in UI/src/themes/commonStyles.ts;
    ///   • UI_STATUS_* / THREAT_PHASE_* localization keys (en-US.json + uk-UA.json);
    ///   • ThreatUISystem phase → UI-string map.
    /// </summary>
    public enum GamePhase : byte
    {
        Calm = 0,       // Build, stockpile, trade - no threats
        Alert = 1,      // Sirens, evacuation - threats incoming
        Attack = 2,     // Threats in the air - observe and survive
        Recovery = 3,   // Assess damage, repair, mourn
        PsyAttack = 4   // Info-war raid on the population (after the kinetic strike)
    }
}
