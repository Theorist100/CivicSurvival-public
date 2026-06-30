namespace CivicSurvival.Core.UI.DomainState
{
    /// <summary>
    /// One available native crash dump (.dmp) shown in the bug-report tab so the player can
    /// pick which one(s) to send. Mirrors the wire shape declared in ui-dto.contract.yaml;
    /// ui-dto codegen owns the WriteTo partial in DomainDtoWriters.g.cs.
    /// </summary>
    public partial struct CrashDumpEntry
    {
        public string Name;
        public float SizeMb;
        public string TimeText;
    }
}
