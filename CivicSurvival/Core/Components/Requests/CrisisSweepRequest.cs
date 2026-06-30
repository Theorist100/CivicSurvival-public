using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Systems.Requests;
using CivicSurvival.Core.Types;
using Colossal.Serialization.Entities;
using Unity.Entities;

namespace CivicSurvival.Core.Components.Requests
{
    /// <summary>
    /// Request to run the in-game crisis sweep — the C# replacement for
    /// <c>Tools/crisis_model.py</c>, driven by the same Core balance formulas the runtime uses
    /// so the predicted crisis can never drift from the simulated one.
    ///
    /// Ephemeral entity pattern — created by the panel trigger (Phase 8), consumed by
    /// <c>CrisisSweepSystem</c> on the pause-safe <c>PostSimulation</c> route (Phase 6), then
    /// destroyed. Blittable (no managed refs) so it round-trips as <see cref="IEmptySerializable"/>.
    ///
    /// The payload carries the <see cref="Mode"/> and the player-/map-/engine-dependent
    /// assumption factors that the runtime does not own and that cannot be read from
    /// <c>balance_config.json</c> (the labelled MODELING ASSUMPTIONS block,
    /// <c>crisis_model.py:55-134</c>). These stay explicit on the request, never baked into the
    /// formula, so a future fuel-crisis / alternate-archetype probe reuses the same call path.
    /// </summary>
    [RequestPersistence(RequestPersistenceKind.TransientInput, typeof(CommandRequestCleanupSystem))]
    public struct CrisisSweepRequest : IComponentData, ICommandRequest, IEmptySerializable
    {
        /// <summary>Which sweep model runs (Invariant / Pacing / Severity).</summary>
        public CrisisSweepMode Mode;

        /// <summary>Severity Monte-Carlo horizon in simulated game-days (<c>--days</c>, default 180).</summary>
        public int Days;

        /// <summary>Severity Monte-Carlo sample count per cell (<c>--runs</c>, default 30).</summary>
        public int Runs;

        /// <summary>RNG seed for the severity run (reproducible parity vs the Python reference).</summary>
        public uint Seed;

        /// <summary>VIP/critical fraction shedding can never drop (<c>UNSHEDDABLE_FRAC</c>, 0.05). No code constant — explicit.</summary>
        public float UnsheddableFrac;

        /// <summary>AA shots fired at one drone crossing the coverage disc (rate-of-fire × dwell, <c>SHOTS_PER_DRONE</c>=3).</summary>
        public int ShotsPerDrone;

        /// <summary>Real minutes per game-day at normal speed (engine/user setting, <c>GAME_DAY_REAL_MINUTES</c>=4.0).</summary>
        public float GameDayRealMinutes;

        /// <summary>Max plants under repair at once — the manual cash-gate stand-in. Used only in the
        /// archetype fallback (no city loaded): once a city is live the sweep derives the concurrent-repair
        /// cap from the real funding pot the chosen <see cref="RepairTier"/> draws on (Phase F), and this
        /// value is ignored.</summary>
        public int MaxConcurrentRepairs;

        /// <summary>Built spare margin above demand for the severity timeline (<c>reserve_frac</c>).</summary>
        public float ReserveFrac;

        /// <summary>Manpower patriotism factor — assumption for live corruption-export % (1.0 clean … 0.5 max corruption).</summary>
        public float Patriotism;

        /// <summary>Manpower morale factor — assumption for live happiness penalty (1.0 content … lower = unrest).</summary>
        public float Morale;

        /// <summary>Thermal stockpile fraction fed to the fuel sigmoid (≥ <c>FuelCurve.BufferThreshold</c> ⇒ no penalty). Explicit so a fuel-crisis probe reuses the call.</summary>
        public float FuelFraction;

        /// <summary>Whether the severity policy applies conscription bonus to the manpower pool.</summary>
        public bool IsConscription;

        /// <summary>Whether the severity policy runs AutoDispatch load-shedding.</summary>
        public bool Shed;

        /// <summary>
        /// Archetype preset selector (index into the Core archetype table — see
        /// <c>CrisisSweepSystem</c>): 0 = dense_urban, 1 = balanced_town, 2 = sprawling_agri.
        /// A small byte selector rather than expanding mw/area/plant sub-fields inline.
        /// </summary>
        public byte ArchetypePreset;

        /// <summary>
        /// Repair-tier selector (severity): 0 = none, 1 = municipal (24h, city money),
        /// 2 = shadow (2h, shadow cash). Drives the per-tier repair duration read from config.
        /// </summary>
        public byte RepairTier;
    }
}
