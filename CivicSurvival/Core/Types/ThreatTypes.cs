using System;
using System.Collections.Generic;

namespace CivicSurvival.Core.Types
{
    /// <summary>
    /// Air Defense installation type.
    /// Determines range, intercept chance, and what threats it can engage.
    ///
    /// Progression:
    /// - HeritageBofors: FREE starter AA from city reserves (weak but free)
    /// - Bofors40mm: Standard purchased AA (balanced)
    /// - Gepard: Modern purchased/shadow AA (expensive but effective)
    /// - PatriotSAM: Late game SAM vs ballistic missiles
    /// </summary>
    public enum AAType : byte
    {
        /// <summary>
        /// Heritage Bofors — FREE starter AA from city reserves.
        /// Old equipment decommissioned from storage.
        /// Weak stats incentivize upgrading via procurement/shadow economy.
        /// Range: 500m, Intercept: 35%, Cooldown: 10s
        /// </summary>
        HeritageBofors = 0,

        /// <summary>
        /// Bofors 40mm autocannon — standard purchased AA.
        /// Effective vs Shahed drones, useless vs ballistic.
        /// Range: 700m, Intercept: 50%, Cooldown: 2.5s
        /// </summary>
        Bofors40mm = 1,

        /// <summary>
        /// Flakpanzer Gepard — modern AA system.
        /// Purchased via clean funding or shadow procurement.
        /// High effectiveness justifies high cost.
        /// Range: 900m, Intercept: 75%, Cooldown: 4s
        /// </summary>
        Gepard = 2,

        /// <summary>
        /// MIM-104 Patriot SAM — late game AA.
        /// Effective vs all threat types including ballistic.
        /// Range: 2000m, Intercept: 70% Shahed, 40% Ballistic
        /// </summary>
        PatriotSAM = 3,

        /// <summary>
        /// MIM-23 Hawk SAM (on a KrAZ tractor) — mid/long-range missile anti-drone.
        /// Specialist interceptor: high kill vs Shahed drones and cruise missiles, ZERO vs
        /// ballistic (that stays Patriot's identity). Cheaper than Patriot, missile-armed.
        /// Range: 2000m, Intercept: ~55% Shahed, 0% Ballistic.
        /// </summary>
        HawkSAM = 4
    }

    /// <summary>
    /// Single source of truth for the weapon class of an <see cref="AAType"/>: a guided-interceptor
    /// launcher (missile) versus an autocannon (gun tracers). The two visual producers read this
    /// instead of each hardcoding the enum with inverse logic — <c>InterceptorSpawnSystem</c> spawns
    /// a missile only when this is true, <c>TracerSpawnSystem</c> skips tracers when it is true. A new
    /// AA type's weapon class is declared here once and both producers follow.
    /// </summary>
    public static class AATypeWeapon
    {
        /// <summary>
        /// True if the AA launches a guided interceptor missile (no gun tracers); false if it is an
        /// autocannon firing tracer rounds. Both the Patriot and the Hawk are missile launchers.
        /// </summary>
        public static bool FiresInterceptorMissile(this AAType type) =>
            type == AAType.PatriotSAM || type == AAType.HawkSAM;

        /// <summary>
        /// Munition kind an AA type consumes — the axis ammo is stocked, priced and displayed on.
        /// Deliberately the same predicate as the weapon class: what a launcher fires IS what it
        /// eats, so a separate mapping would be a second truth to drift out of sync.
        ///
        /// This is what the player decides on. Rockets are dear and gate-limited; shells are cheap
        /// and bought in bulk — a real choice. Between a 40mm Bofors and a 35mm Gepard there is no
        /// choice at all, so both are shells. A new kind is warranted only by a genuinely different
        /// economy, never by a new gun.
        /// </summary>
        public static AmmoKind AmmoKind(this AAType type) =>
            type.FiresInterceptorMissile() ? Types.AmmoKind.Rockets : Types.AmmoKind.Shells;
    }

    /// <summary>
    /// The <see cref="AAType"/> members, enumerated once. Anything that must cover "every AA type"
    /// walks this instead of restating the roster — a hand-kept list is how a new type silently
    /// misses a system that was supposed to include it.
    /// </summary>
    public static class AATypes
    {
        public static readonly IReadOnlyList<AAType> All =
            Array.AsReadOnly((AAType[])Enum.GetValues(typeof(AAType)));
    }

    /// <summary>
    /// What an AA installation's magazine holds. The stocking/pricing/display axis — NOT a store:
    /// ammo itself stays per-installation (<c>AirDefenseInstallation.CurrentAmmo</c>); this only says
    /// which counter an installation's magazine is summed into and which button restocks it.
    /// </summary>
    public enum AmmoKind : byte
    {
        /// <summary>Autocannon rounds — cheap, bulk-bought, restocked by the one "guns" button.</summary>
        Shells = 0,

        /// <summary>Interceptor missiles — dear, flat-priced, own button, excluded from calm auto-refill.</summary>
        Rockets = 1
    }

    /// <summary>
    /// Target category for threat targeting and AA prioritization.
    /// </summary>
    public enum TargetCategory : byte
    {
        Energy = 0,      // PowerPlant, Transformer — 60% of attacks
        Critical = 1,    // Hospital, WaterPump — 15% of attacks
        Service = 2,     // FireStation, PoliceStation — 15% of attacks
        Civilian = 3     // Residential — 10% of attacks (terror)
    }

    /// <summary>
    /// Wave type determines attack intensity.
    /// Harassment: frequent small probes, tests AA
    /// MassiveStrike: rare overwhelming attack, "boss fight"
    /// </summary>
    public enum WaveType : byte
    {
        /// <summary>
        /// Frequent small attack (3-8 drones).
        /// Tests AA coverage, depletes ammo.
        /// If AA handles it - player barely notices.
        /// </summary>
        Harassment = 0,

        /// <summary>
        /// Rare massive attack (20-50 targets).
        /// Mix of drones and missiles.
        /// Goal: overwhelm AA, destroy generation.
        /// Player drops everything to respond.
        /// </summary>
        MassiveStrike = 1
    }
}
