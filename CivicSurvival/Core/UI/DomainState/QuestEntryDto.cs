namespace CivicSurvival.Core.UI.DomainState
{
    /// <summary>
    /// One narrative-quest wire entry for the GlobalStatus quest chip binding.
    /// Mirrors the wire shape declared in ui-dto.contract.yaml; ui-dto codegen
    /// owns the WriteTo partial in DomainDtoWriters.g.cs. The producer
    /// (QuestUISystem) maps a runtime QuestSlot onto it before WriteTo.
    /// </summary>
    public partial struct QuestEntryDto
    {
        public int Id;
        public int Phase;
        public int Progress;
        public int Target;
        public double HoursLeft;
        public int Anchor;
    }
}
