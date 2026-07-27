namespace CivicSurvival.Core.UI.DomainState
{
    /// <summary>
    /// Narrative quest state for the GlobalStatus quest chip and its details modal.
    /// A single pre-serialized JSON list of QuestEntryDto rows ({Id, Phase, Progress,
    /// Target, HoursLeft, Anchor}) — adding a quest adds a row, not a DTO field.
    /// Built by QuestUISystem via JsonBuilder (the RadarThreats pattern).
    /// </summary>
    public partial struct QuestDto : IDomainDto
    {
        /// <summary>JSON array of quest entries; only non-Inactive quests are listed.</summary>
        public string QuestsJson;
    }
}
