namespace CivicSurvival.Core.Types
{
    /// <summary>
    /// Why a threat that reached its target got through air defense — the leak-attribution
    /// funnel for the interception-ceiling investigation. Each bucket points at a different
    /// fix: coverage → placement/range, throughput → targeting slots/prioritization,
    /// lethality → damage/engagement-window tuning. Values are wire-stable: they ship in
    /// threat.impact telemetry (LeakReason int) — never renumber.
    /// </summary>
    public enum ThreatLeakReason
    {
        /// <summary>Sent when the impacting threat carries no combat-state component.</summary>
        Unknown = -1,
        /// <summary>Never inside any operational AA umbrella during the whole flight.</summary>
        NeverCovered = 0,
        /// <summary>In range of an AA at least once, but no shot was ever resolved against it.</summary>
        CoveredNotEngaged = 1,
        /// <summary>Shot at (hit-roll taken) at least once, yet still reached the target.</summary>
        EngagedSurvived = 2,
        /// <summary>
        /// RETIRED — no longer emitted. Was "deliberately let through by the per-wave leak floor",
        /// back when the floor exempted whole targets from interception. The floor is now the wave
        /// leak regulator, which scales intercept chance and exempts nobody, so such a threat
        /// classifies as <see cref="EngagedSurvived"/>. The value stays reserved: it ships in
        /// historical threat.impact telemetry rows and must never be reused for a new bucket.
        /// </summary>
        LeakFloor = 3,
    }
}
