namespace CivicSurvival.Domains.Engineering.Pipelines.PowerCapacity
{
    internal readonly struct CapacityModifierState
    {
        public CapacityModifierState(
            bool isCollapsed,
            bool isUnderConstruction,
            float constructionProgress,
            bool isUnderRepair,
            float explosionDamagePercent,
            float operationalDamagePercent,
            float disasterDamagePercent,
            bool hasImportCapLimit = false,
            int importCapLimitKW = 0,
            bool hasConstructionModifier = true,
            bool hasWearModifier = true,
            bool hasOperationalDamageModifier = true,
            bool hasDisasterDamageModifier = true,
            int baseConstructionCapacityKW = 0,
            int constructionTargetNameplateKW = 0,
            float saturationFactor = 1f,
            float fuelFactor = 1f,
            float fuelAvailability = 1f)
        {
            IsCollapsed = isCollapsed;
            IsUnderConstruction = isUnderConstruction;
            ConstructionProgress = constructionProgress;
            BaseConstructionCapacityKW = baseConstructionCapacityKW;
            ConstructionTargetNameplateKW = constructionTargetNameplateKW;
            IsUnderRepair = isUnderRepair;
            ExplosionDamagePercent = explosionDamagePercent;
            OperationalDamagePercent = operationalDamagePercent;
            DisasterDamagePercent = disasterDamagePercent;
            HasImportCapLimit = hasImportCapLimit;
            ImportCapLimitKW = importCapLimitKW;
            HasConstructionModifier = hasConstructionModifier;
            HasWearModifier = hasWearModifier;
            HasOperationalDamageModifier = hasOperationalDamageModifier;
            HasDisasterDamageModifier = hasDisasterDamageModifier;
            SaturationFactor = saturationFactor;
            FuelFactor = fuelFactor;
            FuelAvailability = fuelAvailability;
        }

        /// <summary>
        /// Copy with the fuel pair replaced. The modifier slice is built on the main thread
        /// (mod-owned sidecar components) while the fuel pair comes from the vanilla
        /// <c>ResourceConsumer</c> read inside <c>PlantResolveJob</c> — the only field of this
        /// state that must be read under the job-graph ordering rather than at snapshot time.
        /// </summary>
        public CapacityModifierState WithFuel(float fuelFactor, float fuelAvailability)
            => new(
                IsCollapsed,
                IsUnderConstruction,
                ConstructionProgress,
                IsUnderRepair,
                ExplosionDamagePercent,
                OperationalDamagePercent,
                DisasterDamagePercent,
                HasImportCapLimit,
                ImportCapLimitKW,
                HasConstructionModifier,
                HasWearModifier,
                HasOperationalDamageModifier,
                HasDisasterDamageModifier,
                BaseConstructionCapacityKW,
                ConstructionTargetNameplateKW,
                SaturationFactor,
                fuelFactor,
                fuelAvailability);

        public readonly bool IsCollapsed;
        public readonly bool IsUnderConstruction;
        public readonly float ConstructionProgress;

        /// <summary>
        /// Pre-upgrade base capacity in kW that is not ramped during the build window (produces
        /// full MW the whole time). 0 for a brand-new plant — the delta-aware ramp then collapses
        /// to the legacy progress-only behaviour, byte-identical to the prior implementation.
        /// </summary>
        public readonly int BaseConstructionCapacityKW;

        /// <summary>
        /// Target nameplate in kW the construction ramp converges to, taken from the
        /// UnderConstruction sidecar so served/target stays self-consistent with
        /// <see cref="BaseConstructionCapacityKW"/> regardless of index-system PlantBaseCapacity lag.
        /// 0 when not under construction — the resolver then divides by PlantBaseCapacity.OriginalCapacity.
        /// </summary>
        public readonly int ConstructionTargetNameplateKW;
        public readonly bool IsUnderRepair;
        public readonly float ExplosionDamagePercent;
        public readonly float OperationalDamagePercent;
        public readonly float DisasterDamagePercent;
        public readonly bool HasImportCapLimit;
        public readonly int ImportCapLimitKW;
        public readonly bool HasConstructionModifier;
        public readonly bool HasWearModifier;
        public readonly bool HasOperationalDamageModifier;
        public readonly bool HasDisasterDamageModifier;

        /// <summary>
        /// Effective surplus-saturation factor ∈ [SaturationFloor, 1] from the per-plant
        /// <c>SaturationModifier</c> (1 when the component is absent / feature off). The third
        /// multiplier folded into the Efficiency factor by <c>PowerCapacityMath.ComputeEffectiveFactor</c>.
        /// </summary>
        public readonly float SaturationFactor;

        /// <summary>
        /// Fuel-stockpile output factor ∈ [MinOutputAtZero, 1] for thermal plants (1 when the
        /// plant is non-thermal / has no <c>ResourceConsumer</c> / feature off). Read on the fly
        /// each tick from <c>ResourceConsumer.m_ResourceAvailability</c> — NOT inertial, NOT
        /// persisted (raw stockpile is instantaneous). The fourth multiplier folded into the
        /// Efficiency factor by <c>PowerCapacityMath.ComputeEffectiveFactor</c>.
        /// </summary>
        public readonly float FuelFactor;

        /// <summary>
        /// Raw fuel-stockpile fraction ∈ [0,1] (<c>m_ResourceAvailability / 255</c>; 1 when the
        /// plant is non-thermal / has no <c>ResourceConsumer</c>). The pre-sigmoid value surfaced
        /// to the UI per-station badge (Фаза 4 DTO pass-through). Distinct from
        /// <see cref="FuelFactor"/> (the post-sigmoid output multiplier).
        /// </summary>
        public readonly float FuelAvailability;
    }
}
