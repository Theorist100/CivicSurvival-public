using CivicSurvival.Core.UI;

namespace CivicSurvival.Core.UI.DomainState
{
    /// <summary>
    /// Backup power domain DTO.
    /// ShadowProgramsJson is pre-serialized JSON array.
    /// </summary>
    public partial struct BackupPowerDto : IDomainDto
    {
        public int BackupCharge;
        public int GeneratorsRunning;
        public int NoiseLevel;
        public int ProtectedBuildings;
        public int BackupCapacity;
        public int DischargingCount;
        public string ShadowProgramsJson;
        public int ProcurementCooldown;
        public int BackupPolicy;
        public int HospitalsPowered;
        public int HospitalsTotal;
        public int SchoolsPowered;
        public int SchoolsTotal;
        [Attributes.DtoEligibility(typeof(BackupPowerEligibility), nameof(BackupPowerEligibility.CanSetBackupPolicy), "SetBackupPolicyLockedReasonId")]
        public bool CanSetBackupPolicy;
        public string ModernizationRequestJson;
        public string BackupPolicyRequestJson;

        partial void WriteEligibility(DomainJsonHelper.JsonWriter w);
    }
}
