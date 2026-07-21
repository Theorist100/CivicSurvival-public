using CivicSurvival.Core.Attributes;

namespace CivicSurvival.Core.Forecast
{
    /// <summary>
    /// One-shot snapshot of every live-or-archetype input the crisis sweep reads, gathered ONCE by
    /// the orchestrating <c>CrisisSweepSystem</c> before it hands control to the pure
    /// <see cref="CrisisSweepRunner"/>. The runner is non-ECS and reads ONLY from this struct (plus
    /// the request, the per-plant scratch and <c>BalanceConfig.Current</c>), so the verdict it
    /// produces is byte-identical to the pre-split inline-reader model: every reader the Run methods
    /// used to call inline is a one-shot snapshot, so pre-gathering the value and reading it here is
    /// equivalent to calling the reader at the same point.
    ///
    /// Each axis carries a presence flag plus its value(s); when the flag is false the runner falls
    /// back to the archetype preset exactly as the inline reader's "returned no data" branch did. The
    /// per-plant sizes are NOT in this struct — they live in the system's reused <c>m_PlantCapMW</c>
    /// scratch (handed to the runner by ref via <see cref="CrisisSweepScratch"/>), filled by the same
    /// power read that sets the power-live flag.
    ///
    /// ENFORCED SHAPE (CIVIC514): every gated VALUE field is <c>private</c> and annotated
    /// <see cref="PresenceGatedAttribute"/> — its value is reachable only through a <c>TryGet*</c>
    /// method that returns the presence flag alongside the value, so a consumer cannot read the raw
    /// value without observing the flag and silently take the zero-init value for the archetype
    /// fallback. The presence flags themselves stay public (they carry no gated value). The struct is
    /// a <c>readonly struct</c> and every TryGet is non-allocating (CIVIC050): the sweep gathers it
    /// once per run with no per-update allocation.
    /// </summary>
    public readonly struct LiveInputs
    {
        // ===== MANPOWER =====
        /// <summary>True when the live mobilization snapshot was available (city loaded, war started).</summary>
        public readonly bool ManpowerLive;
        [PresenceGated(nameof(ManpowerLive))] private readonly int m_ManpowerTotal;
        [PresenceGated(nameof(ManpowerLive))] private readonly int m_ManpowerUsed;
        [PresenceGated(nameof(ManpowerLive))] private readonly int m_ManpowerCasualties;
        /// <summary>Live available pool (already nets crew/used/casualties — do NOT re-subtract).</summary>
        [PresenceGated(nameof(ManpowerLive))] private readonly int m_ManpowerAvailable;
        /// <summary>Live war-day the pool was snapshotted at; -1 before the war starts.</summary>
        [PresenceGated(nameof(ManpowerLive))] private readonly int m_ManpowerWarDay;

        // ===== POWER =====
        /// <summary>True when the live power-capacity snapshot was available with nameplate &gt; 0.</summary>
        public readonly bool PowerLive;
        [PresenceGated(nameof(PowerLive))] private readonly float m_ProductionMW;
        [PresenceGated(nameof(PowerLive))] private readonly float m_NameplateMW;
        [PresenceGated(nameof(PowerLive))] private readonly float m_LargestPlantMW;
        [PresenceGated(nameof(PowerLive))] private readonly int m_IntermittentTypes;
        /// <summary>Live producing-plant count (clamped to MAX_PLANTS); 0 ⇒ no per-plant sizes.</summary>
        [PresenceGated(nameof(PowerLive))] private readonly int m_NPlants;

        // ===== DEMAND =====
        // Demand is sentinel-gated (0 ⇒ none → archetype demand), not flag-gated, so it carries no
        // companion bool. It is still private + TryGet-only so the consumer cannot read the raw value
        // without observing the sentinel TryGetDemand returns.
        private readonly float m_DemandMW;

        // ===== WAVE =====
        /// <summary>True once a city is loaded (scenario singleton present); gates the wave-axis live values.</summary>
        public readonly bool CityLoaded;
        /// <summary>Live season modifier (gathered only when CityLoaded; else the runner uses the neutral default).</summary>
        [PresenceGated(nameof(CityLoaded))] private readonly float m_SeasonMod;
        /// <summary>Live wave-frequency modifier (gathered only when CityLoaded; else DefaultFrequencyMod).</summary>
        [PresenceGated(nameof(CityLoaded))] private readonly float m_FrequencyMod;

        // ===== FLEET =====
        /// <summary>True when a non-empty placed AA fleet was reported (else archetype Heritage-grant model).</summary>
        public readonly bool FleetLive;
        [PresenceGated(nameof(FleetLive))] private readonly AirDefenseForecast.FleetComposition m_Fleet;

        /// <summary>Global player toggle "do Patriot SAMs engage drones?" (default false — fail-closed,
        /// matching the runtime null-object). NOT presence-gated: it is a plain player choice readable
        /// even before a city loads, and false is the correct fallback (the runtime reserves Patriot for
        /// ballistics when AirDefense is unavailable). The forecast uses it to (a) gate the Patriot out
        /// of the drone-leak math and (b) decide whether the Patriot magazine is reserved for the
        /// ballistic line or spent on drones.</summary>
        public readonly bool PatriotInterceptsDrones;

        // ===== AREA =====
        // Area is sentinel-gated (0 ⇒ none → archetype population-derived area), not flag-gated.
        // Same rationale as demand: private + TryGet-only, the sentinel is the flag TryGetArea returns.
        private readonly float m_AreaKm2;

        // ===== REPAIR BUDGET (Phase F) =====
        /// <summary>True once a city is loaded: the two repair-funding pots were snapshotted (else the
        /// runner keeps the request's manual <c>MaxConcurrentRepairs</c> cash-gate stand-in).</summary>
        public readonly bool RepairBudgetLive;
        /// <summary>Live municipal (vanilla City Budget) balance — funds <c>RepairType.Municipal</c>.
        /// A closed/boot-window read snaps 0 (no funds → no municipal-funded repair slots).</summary>
        [PresenceGated(nameof(RepairBudgetLive))] private readonly long m_MunicipalCash;
        /// <summary>Live Shadow Cash balance — funds <c>RepairType.ShadowOps</c>. 0 when the shadow
        /// wallet is closed (pre-Crisis act) → no shadow-funded repair slots (honest fail-closed).</summary>
        [PresenceGated(nameof(RepairBudgetLive))] private readonly long m_ShadowCash;

        public LiveInputs(
            bool manpowerLive, int manpowerTotal, int manpowerUsed, int manpowerCasualties,
            int manpowerAvailable, int manpowerWarDay,
            bool powerLive, float productionMW, float nameplateMW, float largestPlantMW,
            int intermittentTypes, int nPlants,
            float demandMW,
            bool cityLoaded, float seasonMod, float frequencyMod,
            bool fleetLive, AirDefenseForecast.FleetComposition fleet,
            bool patriotInterceptsDrones,
            float areaKm2,
            bool repairBudgetLive, long municipalCash, long shadowCash)
        {
            ManpowerLive = manpowerLive;
            m_ManpowerTotal = manpowerTotal;
            m_ManpowerUsed = manpowerUsed;
            m_ManpowerCasualties = manpowerCasualties;
            m_ManpowerAvailable = manpowerAvailable;
            m_ManpowerWarDay = manpowerWarDay;

            PowerLive = powerLive;
            m_ProductionMW = productionMW;
            m_NameplateMW = nameplateMW;
            m_LargestPlantMW = largestPlantMW;
            m_IntermittentTypes = intermittentTypes;
            m_NPlants = nPlants;

            m_DemandMW = demandMW;

            CityLoaded = cityLoaded;
            m_SeasonMod = seasonMod;
            m_FrequencyMod = frequencyMod;

            FleetLive = fleetLive;
            m_Fleet = fleet;
            PatriotInterceptsDrones = patriotInterceptsDrones;

            m_AreaKm2 = areaKm2;

            RepairBudgetLive = repairBudgetLive;
            m_MunicipalCash = municipalCash;
            m_ShadowCash = shadowCash;
        }

        // ════════════════════════════════════════════════════════════════════
        // Gated accessors — each returns the presence flag and outs the value(s).
        // The gated VALUE fields are private + [PresenceGated], so these methods are the ONLY read
        // path (CIVIC514); the consumer must observe the flag, never the raw zero-init value.
        // ════════════════════════════════════════════════════════════════════

        /// <summary>Live mobilization snapshot; returns <see cref="ManpowerLive"/>.</summary>
        public bool TryGetManpower(out int total, out int used, out int casualties, out int available, out int warDay)
        {
            total = m_ManpowerTotal;
            used = m_ManpowerUsed;
            casualties = m_ManpowerCasualties;
            available = m_ManpowerAvailable;
            warDay = m_ManpowerWarDay;
            return ManpowerLive;
        }

        /// <summary>Live power-capacity snapshot; returns <see cref="PowerLive"/>.</summary>
        public bool TryGetPower(out float productionMW, out float nameplateMW, out float largestPlantMW, out int intermittentTypes, out int nPlants)
        {
            productionMW = m_ProductionMW;
            nameplateMW = m_NameplateMW;
            largestPlantMW = m_LargestPlantMW;
            intermittentTypes = m_IntermittentTypes;
            nPlants = m_NPlants;
            return PowerLive;
        }

        /// <summary>Live placed-AA fleet; returns <see cref="FleetLive"/>.</summary>
        public bool TryGetFleet(out AirDefenseForecast.FleetComposition fleet)
        {
            fleet = m_Fleet;
            return FleetLive;
        }

        /// <summary>Live wave axis (season + frequency modifiers); returns <see cref="CityLoaded"/>.</summary>
        public bool TryGetWave(out float seasonMod, out float frequencyMod)
        {
            seasonMod = m_SeasonMod;
            frequencyMod = m_FrequencyMod;
            return CityLoaded;
        }

        /// <summary>
        /// Live defendable city area (km²); the value's presence is the 0-sentinel itself, so the
        /// returned bool is <c>areaKm2 &gt; 0f</c> (0 ⇒ none → archetype area). Callers may use the
        /// bool or re-test the out value with the same <c>&gt; 0f</c> guard — both are identical.
        /// </summary>
        public bool TryGetArea(out float areaKm2)
        {
            areaKm2 = m_AreaKm2;
            return areaKm2 > 0f;
        }

        /// <summary>
        /// Live demand (MW): 24h rolling peak, else instantaneous. Sentinel-gated like area — the
        /// returned bool is <c>demandMW &gt; 0f</c> (0 ⇒ none → archetype demand).
        /// </summary>
        public bool TryGetDemand(out float demandMW)
        {
            demandMW = m_DemandMW;
            return demandMW > 0f;
        }

        /// <summary>
        /// Live repair-funding pots (municipal City Budget + Shadow Cash); returns
        /// <see cref="RepairBudgetLive"/>. The runner picks the pot the request's <c>RepairTier</c> draws
        /// on and derives the concurrent-repair cash gate from it; false ⇒ no city loaded ⇒ keep the
        /// request's manual <c>MaxConcurrentRepairs</c> stand-in (archetype fallback).
        /// </summary>
        public bool TryGetRepairBudget(out long municipalCash, out long shadowCash)
        {
            municipalCash = m_MunicipalCash;
            shadowCash = m_ShadowCash;
            return RepairBudgetLive;
        }
    }

    /// <summary>
    /// References to the orchestrator's preallocated severity scratch, bundled so the pure
    /// <see cref="CrisisSweepRunner"/> can drive the timeline without owning any ECS-side buffers.
    /// Arrays and the list are reference types, so constructing this struct copies only references —
    /// no heap allocation (CIVIC050). The system allocates the backing storage ONCE in
    /// <c>OnCreate</c> (sized to <see cref="CrisisSweepRunner.MAX_PLANTS"/>) and the per-run reset
    /// lives in <c>RepairForecast.Reset</c>; this struct only forwards the references.
    /// </summary>
    public readonly struct CrisisSweepScratch
    {
        // Fields are internal, not public: they hand mutable backing arrays straight to the runner's
        // timeline (which writes into them by ref via ForecastState), so they must stay assembly-private
        // — exposing mutable shared scratch as public state is exactly what S3887 guards against. The
        // runner and the orchestrator that builds this struct are both in CivicSurvival.dll, so internal
        // is sufficient.

        /// <summary>Per-plant damage fraction 0..1 (reused, reset per run).</summary>
        internal readonly float[] PlantDamage;
        /// <summary>Per-plant repair-completion game-hour (reused).</summary>
        internal readonly float[] RepairDone;
        /// <summary>Per-plant nameplate (MW), filled by the live power read; null/zero in archetype mode.</summary>
        internal readonly float[] PlantCapMW;
        /// <summary>Fixed-capacity FIFO ring of damaged plant ids (reused).</summary>
        internal readonly int[] RepairQueue;
        /// <summary>Per-plant "already queued" membership flag (reused).</summary>
        internal readonly bool[] RepairQueued;
        /// <summary>First-collapse days across runs (reused; Clear() per run-set).</summary>
        internal readonly System.Collections.Generic.List<int> CollapseDays;

        public CrisisSweepScratch(
            float[] plantDamage, float[] repairDone, float[] plantCapMW,
            int[] repairQueue, bool[] repairQueued,
            System.Collections.Generic.List<int> collapseDays)
        {
            PlantDamage = plantDamage;
            RepairDone = repairDone;
            PlantCapMW = plantCapMW;
            RepairQueue = repairQueue;
            RepairQueued = repairQueued;
            CollapseDays = collapseDays;
        }
    }
}
