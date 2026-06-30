namespace CivicSurvival.Core.UI.DomainState
{
    /// <summary>
    /// One power-plant wear entry for the Generation Sources UI panel.
    /// Mirrors the wire shape declared in ui-dto.contract.yaml; ui-dto
    /// codegen owns the WriteTo partial in DomainDtoWriters.g.cs.
    /// </summary>
    public partial struct PlantWearData
    {
        public int PlantId;
        public string Name;
        public int CapacityMW;
        public int CurrentOutputMW;
        public float WearPercent;
        public float RepairBillablePercent;
        public bool IsRepairable;
        public bool IsDestroyed;
        public bool IsRepairing;
        public float RepairHoursLeft;
        public bool HasExploded;
        public bool IsUnderConstruction;
        public float ConstructionDaysLeft;
        public float OperationalDamagePercent;
        public int OperationalHitCount;
        public int OperationalHitMax;
        public float DisasterDamagePercent;
        public bool IsAtRisk;
        public int MunicipalRepairCharge;
        public int MunicipalKickbackRepairCharge;
        public int KickbackRepairAmount;
        public bool CanMunicipalRepair;
        public string MunicipalRepairLockedReasonId;
        public bool CanKickbackRepair;
        public string KickbackRepairLockedReasonId;
        public int ShadowOpsRepairCharge;
        public bool CanShadowRepair;
        public string ShadowRepairLockedReasonId;
        public Core.Types.PlantState State;
        public float SaturationFactor;
        public float FuelAvailabilityPercent;
        public float FuelFactor;
        public float RecoveryHours;
    }
}
