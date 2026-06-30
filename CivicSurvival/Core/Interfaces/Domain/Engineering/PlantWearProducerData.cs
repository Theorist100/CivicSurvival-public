using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Interfaces.Domain.Engineering
{
    /// <summary>
    /// Producer-side power plant wear record returned by
    /// <see cref="IEquipmentUIService.GetPlantsSnapshot"/>. Carries the 14
    /// fields the equipment service has direct access to. The PowerGrid UI
    /// system enriches each entry with repair-eligibility/cost fields and
    /// maps it onto the wire-shape <c>PlantWearData</c> contract subtype
    /// before serialization.
    ///
    /// This is intentionally not a "Dto" — it's an internal producer
    /// record, not a wire payload. Renamed from <c>PlantWearDto</c> in
    /// C4b1 to disambiguate from the contract DTO of the same role.
    /// </summary>
    public struct PlantWearProducerData
    {
        public int PlantId;  // Stable ID (survives save/load)
        public string Name;
        public int CapacityMW;
        public int CurrentOutputMW;
        public float WearPercent;
        public float RepairBillablePercent;
        public bool IsRepairable;
        public bool IsDestroyed;  // Vanilla Destroyed on the building (knocked-out ruin); not mod-repairable
        public bool IsRepairing;
        public float RepairHoursLeft;
        public bool HasExploded;
        public bool IsUnderConstruction;
        public float ConstructionDaysLeft;
        public float OperationalDamagePercent;  // Missile strike damage (0-1)
        public int OperationalHitCount;  // Discrete missile hits sustained (opDamage / per-plant loss, PlantHitMath)
        public int OperationalHitMax;  // Hits to destruction — nameplate-scaled (PlantHitMath.HitsToDestroy)
        public float DisasterDamagePercent;  // Disaster damage (0-1, Minor=0.5, Major=1.0)
        public PlantState State;  // Computed state for UI display
        public float SaturationFactor;          // 0..1 effective surplus-saturation (with inertia); 1 = no degradation
        public float FuelAvailabilityPercent;   // 0..1 stockpile-derived fuel availability; 1 for renewables/non-thermal
        public float FuelFactor;                // 0..1 post-sigmoid fuel OUTPUT factor; 1 = no penalty (badge gates on this)
        public float RecoveryHours;             // est. game-hours for saturation to climb back to fleet target (0 = no up-ramp)
    }
}
