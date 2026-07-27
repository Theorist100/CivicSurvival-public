using CivicSurvival.Core.UI;

namespace CivicSurvival.Core.UI.DomainState
{
    /// <summary>
    /// Power grid domain DTO — single JSON binding for all grid state.
    /// Pushed by PowerGridUISystem every 500ms via ThrottledUISystemBase.
    /// </summary>
    /// <remarks>
    /// <b>ShadowBalance duplication (F24-M1)</b>: this field is intentionally
    /// duplicated from <see cref="GridWarfareDto"/>. PlantsContent reads it to
    /// render the shadow wallet alongside grid stats. Pulling from GridWarfareDto
    /// would add a second JSON binding subscription to PlantsContent, doubling
    /// its re-render frequency (~250ms vs ~500ms) for a single int. The ≤500ms
    /// desync between panels is imperceptible.
    /// Source: both read <c>ShadowWalletSingleton.Balance</c>.
    /// </remarks>
    public partial struct PowerGridDto : IDomainDto
    {
        public string GridStatus;
        public int Production;
        public int Demand;
        public int Consumption;
        public float GameHour;
        public float GridFrequency;
        public string StressZone;
        public float StressPercent;
        public float RecoveryHours;
        public float CollapseThresholdHours;
        public bool ThresholdActive;
        public int BuildingsCutCount;
        // Power flow breakdown (MW, integer): Demand → Delivered ← ForcedOff
        // Delivered = Demand − ForcedOff. ForcedOff aggregates AutoCut + DistrictShed + AutoDispatchShed.
        public int DeliveredMW;
        public int ForcedOffMW;
        public int AutoCutMW;          // threshold cut (< 90% rule), MW
        public int DistrictShedMW;     // category/district manual toggle, MW
        public int AutoDispatchShedMW; // AutoDispatch automatic shedding, MW
        public int CitySchedule;
        public int EffectiveCityMode;
        // True when one or more non-VIP districts run a manual schedule/category override
        // that differs from the city preset. The city label stays "Default" (it honestly
        // reflects the city-wide setting), but the UI marks it so the player sees that
        // districts diverge from it.
        public bool DistrictsOverrideCity;
        public ActionAvailabilityField CityScheduleAvailability;
        public bool AutoDispatchEnabled;
        public int AutoDispatchSheddedCount;
        public bool AutoDispatchBlockedByVip;

        /// <summary>
        /// Shadow economy balance. Intentionally duplicated from
        /// <see cref="GridWarfareDto.ShadowBalance"/> — see struct remarks.
        /// </summary>
        public int ShadowBalance;
        public int AtRiskPlantCount;
        public string GenerationSourcesJson;

        // Civilian building damage data (JSON array from ICivilianDamageReader)
        public string CivilianDamageJson;
        public float PlantMunicipalRepairHours;
        public float PlantShadowOpsRepairHours;
        public float CivilianMunicipalRepairHours;
        public float CivilianShadowOpsRepairHours;
        public string PlantRepairRequestJson;
        public string CivilianRepairRequestJson;
        public string AutoDispatchToggleRequestJson;
        public string DistrictToggleRequestJson;
        public string CitySchedulePeriodRequestJson;
        public string DistrictInternetToggleRequestJson;

        /// <summary>
        /// Fleet surplus-saturation target factor ∈ [0,1] (1 = no fleet-wide surplus penalty).
        /// Mirrors <see cref="Core.Components.CrossDomain.PowerCapacitySnapshot.FleetTargetFactor"/>.
        /// Drives the "fleet load" aggregate in the city passport; hidden by the UI when ≈ 1.
        /// </summary>
        public float FleetSaturationFactor;

        /// <summary>
        /// Dispatchable potential of the city fleet, MW (GridProducer only; no import,
        /// no batteries). Source: <see cref="Core.Components.CrossDomain.PowerCapacitySnapshot.CityDispatchableMW"/>.
        /// </summary>
        public int CityDispatchableMW;

        /// <summary>
        /// SURPLUS: CityDispatchableMW − Consumption(MW). Can be &lt; 0 — a potential
        /// deficit (the load is held by import, or the grid is underbuilt).
        /// </summary>
        public int CapacityHeadroomMW;

        /// <summary>
        /// Legal export flow ≥ 0, MW: max(0, RawBalance − ExternalPower).
        /// ExternalPower is subtracted: the donor/import bonus sits in Production —
        /// without the subtraction the EXPORT row would show a phantom "export" at cap 0.
        /// The name ExportMW is taken by the shadow channel — do not rename.
        /// </summary>
        public int GridExportMW;

        /// <summary>
        /// Warning threshold for SURPLUS: min(LargestPlantKW/1000, GenerationSaturation.UnitBufferCapMW)
        /// — the N+1 buffer (a reserve below one largest plant is the yellow zone).
        /// </summary>
        public int HeadroomWarningMW;

        // Capacity-loss breakdown (MW, integer): WHY the city fleet serves less than its built
        // nameplate. Sequential decomposition over grid producers (PowerLossBreakdown, the same
        // chain as the [CapacityLoss] debug line): built = knockedOut + damage + saturation +
        // fuel + LossAllowedMW. Answers the player question "all my generators are under 50%"
        // without reading debug logs.

        /// <summary>Nameplate standing in ruins: grid collapse, an active repair window, or full damage (MW).</summary>
        public int LossKnockedOutMW;

        /// <summary>Lost to partial strike / disaster / explosion damage (MW).</summary>
        public int LossDamageMW;

        /// <summary>Lost to surplus saturation — the fleet КПД cut (MW).</summary>
        public int LossSaturationMW;

        /// <summary>Lost to thermal fuel starvation (MW).</summary>
        public int LossFuelMW;

        /// <summary>What the multiplier chain lets the fleet serve (MW) — the breakdown's remainder.</summary>
        public int LossAllowedMW;
    }
}
