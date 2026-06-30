namespace CivicSurvival.Core.UI.DomainState
{
    /// <summary>
    /// ModalCoordinator wire snapshot. ActiveDataJson and QueueJson are
    /// pre-serialized JSON (object/null and array literal respectively) so
    /// the coordinator can splice in its event-time payload and pending
    /// queue without exposing internal types to the writer.
    /// </summary>
    public partial struct ModalSnapshotDto : IDomainDto
    {
        public string ActiveId;
        public int ActivePriority;
        public string ActiveDataJson;
        public string QueueJson;
        public int Version;
    }
}
