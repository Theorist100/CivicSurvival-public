namespace CivicSurvival.Core.UI.DomainState
{
    /// <summary>
    /// One civilian damage entry for the PowerGrid Infrastructure UI panel.
    /// Mirrors the wire shape declared in ui-dto.contract.yaml; ui-dto
    /// codegen owns the WriteTo partial in DomainDtoWriters.g.cs.
    /// </summary>
    public partial struct CivilianDamageData
    {
        public EntityRefDto Building;
        public string Name;
        public int HitCount;
        public int MaxHits;
        public float DamagePercent;
        public bool IsRepairing;
        public float RepairHoursLeft;
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
    }
}
