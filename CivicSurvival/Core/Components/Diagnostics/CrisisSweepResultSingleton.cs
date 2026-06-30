using Colossal.Serialization.Entities;
using Unity.Entities;

namespace CivicSurvival.Core.Components.Diagnostics
{
    /// <summary>
    /// Output of the in-game crisis sweep, re-populated on every run by <c>CrisisSweepSystem</c>
    /// (Phase 7 fills the fields). Read by the UI DTO writer (Phase 8) and grep-able via the
    /// <c>[SWEEP]</c> log line.
    ///
    /// <see cref="IEmptySerializable"/> is mandatory — a serializer-less singleton
    /// <see cref="IComponentData"/> is stripped entirely on load (the type vanishes from the
    /// restored entity, decompile-proven), and an empty <c>ISerializable.Serialize</c> body
    /// crashes the save. This is transient output, not saved state, so the no-op marker is
    /// correct and <see cref="HasResult"/> is false after load until a sweep runs.
    /// </summary>
    public struct CrisisSweepResultSingleton : IComponentData, IEmptySerializable
    {
        // ===== Meta (always written) =====

        /// <summary>Which mode produced this verdict (0 = Invariant, 1 = Pacing, 2 = Severity).</summary>
        public byte Mode;

        /// <summary>True once a sweep has run this session; meaningless (false) after load.</summary>
        public bool HasResult;

        /// <summary>Game-hours at verdict (<c>GameRate.TotalGameHours</c>, never <c>ElapsedTime</c> which resets on load).</summary>
        public double ComputedAtGameHours;

        /// <summary>Archetype preset id the sweep verdict reports on (int id, not a prefab ref — Axiom 11).</summary>
        public int ArchetypeId;

        /// <summary>Population peak the verdict reports on.</summary>
        public int PopulationPeak;

        /// <summary>War-day the verdict reports on (-1 before war).</summary>
        public int WarDay;

        // ===== Invariant mode (Mode == 0) =====

        /// <summary>Worst-case recoverability for branch A — "Patriot reserved for ballistics"
        /// (PatriotInterceptsDrones=false): post-strike production as a % of pre-strike production,
        /// after BOTH the branch-A drone leak AND the branch-A leaked-ballistic plant damage. The sweep
        /// is a DECISION tool — it computes recovery for BOTH branches every run (no gate on the live
        /// toggle), so the verdict no longer flips when the player toggles. Branch A has the weaker drone
        /// layer but the full ballistic shield, so its ballistic damage term is small.</summary>
        public float WorstCaseRecoveryBallisticOnly;

        /// <summary>Worst-case recoverability for branch B — "Patriot mixed" (PatriotInterceptsDrones=
        /// true): the same post-strike % but with the branch-B drone leak (lower) AND the branch-B
        /// leaked-ballistic damage (HIGHER — the Patriot magazine left the ballistic line). Mixed is no
        /// longer always the better verdict: its stronger drone layer is paid for by the ballistic hole
        /// the recovery now folds in.</summary>
        public float WorstCaseRecoveryMixed;

        /// <summary>Whether branch A (Patriot reserved for ballistics) is recoverable (verdict ≠ FORCED /
        /// ONE-SHOT): after_A &gt; 0 AND after_A ≥ unsheddable floor.</summary>
        public bool IsRecoverableBallisticOnly;

        /// <summary>Whether branch B (Patriot mixed) is recoverable: after_B &gt; 0 AND after_B ≥
        /// unsheddable floor. Can be false while branch A is recoverable when the Mixed ballistic hole
        /// drops production below the floor.</summary>
        public bool IsRecoverableMixed;

        /// <summary>Population-scaled grid collapse grace window (hours). Branch-INDEPENDENT: the grace
        /// window is <c>GridStressLogic.ScaledCollapseThreshold</c> of population alone, with no
        /// dependence on after-strike production / deficit, so it is a single value for both branches
        /// (not paired like recovery).</summary>
        public float GraceWindowHours;

        /// <summary>Drone two-layer intercept % for branch A — "Patriot reserved for ballistics"
        /// (PatriotInterceptsDrones=false): the drone layer is Heritage + Bofors + Gepard only (no
        /// Patriot 0.70 on Shaheds). The sweep computes BOTH branches every run so the player can weigh
        /// the trade-off without flipping the live toggle.</summary>
        public float DroneInterceptBallisticOnly;

        /// <summary>Drone two-layer intercept % for branch B — "Patriot mixed" (PatriotInterceptsDrones=
        /// true): the drone layer adds the Patriot's 0.70 on Shaheds, so it is HIGHER than branch A — at
        /// the cost of the ballistic line (see <see cref="BallisticInterceptMixed"/>).</summary>
        public float DroneInterceptMixed;

        /// <summary>Free Heritage AA grant count for the worst-case production.</summary>
        public int FreeHeritageGrant;

        /// <summary>Manned (operational) AA at verdict after the crew gate + war fatigue.</summary>
        public int OperationalAaAtVerdict;

        /// <summary>Live manpower pool size (Total) the crew gate saw at verdict, or 0 in archetype-fallback mode.</summary>
        public int ManpowerTotal;

        /// <summary>Live committed manpower (Used) at verdict, or 0 in archetype-fallback mode.</summary>
        public int ManpowerUsed;

        /// <summary>Live casualties at verdict, or 0 in archetype-fallback mode.</summary>
        public int ManpowerCasualties;

        /// <summary>Live available manpower (already nets Used + Casualties) at verdict, or 0 in archetype-fallback mode.</summary>
        public int ManpowerAvailable;

        /// <summary>Live placed Heritage-Bofors launchers at verdict, or 0 in archetype-fallback mode.</summary>
        public int AaHeritage;

        /// <summary>Live placed Bofors 40mm launchers at verdict, or 0 in archetype-fallback mode.</summary>
        public int AaBofors;

        /// <summary>Live placed Gepard launchers at verdict, or 0 in archetype-fallback mode.</summary>
        public int AaGepard;

        /// <summary>Live placed Patriot launchers at verdict, or 0 in archetype-fallback mode.</summary>
        public int AaPatriot;

        /// <summary>AA coverage the invariant verdict used (% of the defended area), live or archetype.</summary>
        public float CoveragePct;

        /// <summary>Defendable footprint (km²) the invariant verdict used, live or archetype (0 if none).</summary>
        public float AreaKm2;

        /// <summary>Ballistic-intercept % for branch A — "Patriot reserved for ballistics": the Patriot
        /// magazine is on the ballistic line (0.40, near-full coverage) + the Gepard backstop (0.10, small
        /// coverage). HIGH — the cost of branch A is the lower drone layer
        /// (<see cref="DroneInterceptBallisticOnly"/>).</summary>
        public float BallisticInterceptBallisticOnly;

        /// <summary>Ballistic-intercept % for branch B — "Patriot mixed": the Patriot magazine went to
        /// drones, so only the Gepard backstop (0.10 × its small coverage) remains. LOW — the cost of the
        /// higher drone layer (<see cref="DroneInterceptMixed"/>). The stark A↔B gap here is the
        /// trade-off the player weighs.</summary>
        public float BallisticInterceptMixed;

        /// <summary>Ballistic missiles the worst-case wave spawns at this production / wave number (the
        /// ballistic demand both branches defend against). 0 below the production / wave thresholds.</summary>
        public int BallisticTargets;

        /// <summary>Patriot interceptor rounds branch B diverts to drones (the whole operational Patriot
        /// magazine) — the rounds branch A keeps on the ballistic line. Indicative of the trade size.</summary>
        public int MissilesSpentOnDrones;

        /// <summary>The player's CURRENT live toggle — a marker only, NOT a gate on the computation (both
        /// branches are always computed). The UI uses it to mark which branch is the player's active
        /// choice. False = Patriot reserved for ballistics (default, branch A active).</summary>
        public bool PatriotInterceptsDrones;

        // ===== Pacing mode (Mode == 1) =====

        /// <summary>Calm phase duration (hours) at the reported MW × season.</summary>
        public float CalmHours;

        /// <summary>Wave pressure (calm fraction of the full cycle) at peak — lower = harsher pacing.</summary>
        public float WavePressureAtPeak;

        // ===== Severity mode (Mode == 2) =====

        /// <summary>Monte-Carlo run count (number of timeline replays); the blackout/median stats aggregate over all ticks of these runs.</summary>
        public int SampleCount;

        /// <summary>Time-in-Blackout probability (% of simulated time the city was collapsed).</summary>
        public float BlackoutProbabilityPct;

        /// <summary>Median game-day of first collapse across runs (0 = no collapse observed).</summary>
        public int MedianCollapseDay;

        /// <summary>Unsheddable critical load floor (MW) below which shedding can no longer prevent deficit.</summary>
        public int UnsheddableFloorMW;

        /// <summary>Phase F — concurrent repair slots the timeline ran on: the live cash-gate
        /// (<c>floor(pot / full-repair cost)</c>, clamped to the fleet) when a city is loaded, else the
        /// request's manual <c>MaxConcurrentRepairs</c> stand-in.</summary>
        public int RepairSlots;

        /// <summary>Phase F — the funding-pot balance the repair slots were derived from (the pot the
        /// chosen <see cref="RepairTier"/> draws on). 0 in the archetype fallback (no city loaded).</summary>
        public long RepairFundingCash;

        /// <summary>Phase F — which pot funded the repairs: 0 = none, 1 = municipal (City Budget),
        /// 2 = shadow (Shadow Cash). Mirrors the request's <c>RepairTier</c>.</summary>
        public byte RepairTier;

        /// <summary>Phase F — true when the slots came from the live city cash-gate; false ⇒ archetype
        /// fallback on the manual <see cref="RepairSlots"/> stand-in.</summary>
        public bool RepairBudgetLive;
    }
}
