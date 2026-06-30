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
        PatriotSAM = 3
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
        /// autocannon firing tracer rounds. Currently only <see cref="AAType.PatriotSAM"/> is a missile.
        /// </summary>
        public static bool FiresInterceptorMissile(this AAType type) => type == AAType.PatriotSAM;
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
