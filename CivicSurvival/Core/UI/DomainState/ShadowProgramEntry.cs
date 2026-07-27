namespace CivicSurvival.Core.UI.DomainState
{
    /// <summary>
    /// One district modernization program entry for the backup-power UI
    /// panel. Mirrors the wire shape declared in ui-dto.contract.yaml;
    /// ui-dto codegen owns the WriteTo partial in DomainDtoWriters.g.cs.
    /// </summary>
    public partial struct ShadowProgramEntry
    {
        public int DistrictIndex;
        public string DistrictName;
        public bool HasProgram;
        public string Contractor;
        public int EstimatedCost;
        public bool CanModernizeHonest;
        public string ModernizeHonestLockedReasonId;
        public bool CanModernizeCorrupt;
        public string ModernizeCorruptLockedReasonId;
        public int KickbackEarned;
        public int FireCount;
    }
}
