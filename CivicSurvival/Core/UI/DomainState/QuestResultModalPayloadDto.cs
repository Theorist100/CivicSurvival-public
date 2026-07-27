
namespace CivicSurvival.Core.UI.DomainState
{
    /// <summary>
    /// Event-time snapshot behind the QuestCompleted / QuestFailed modals: which quest
    /// resolved and where its counter stood. A snapshot rather than a read of the live
    /// QuestDto because the slot clears itself a few game hours later — the modal must
    /// still say "3 of 4" after the chip is gone.
    /// </summary>
    public partial struct QuestResultModalPayloadDto : IDomainDto
    {
        /// <summary>C# QuestId value; the UI maps it to the quest's text keys.</summary>
        public int Id;
        /// <summary>Counter reached at resolution (0 for quests that are not counted).</summary>
        public int Progress;
        /// <summary>Counter that was asked for (0 for quests that are not counted).</summary>
        public int Target;
    }
}
