namespace CivicSurvival.Core.UI.DomainState
{
    /// <summary>
    /// One active toast notification for the Toast UI binding. Mirrors the
    /// wire shape declared in ui-dto.contract.yaml; ui-dto codegen owns the
    /// WriteTo partial in DomainDtoWriters.g.cs. Type is the
    /// Core.Types.ToastType enum serialized as its string name; Priority is
    /// the ToastPriority enum as its int ordinal.
    /// </summary>
    public partial struct ToastDataDto
    {
        public int Id;
        public string Type;
        public int Priority;
        public string Title;
        public string Message;
        public string AcceptLabel;
        public string RejectLabel;
        public float RemainingSeconds;
        public float Progress;
        public int ContextData;
    }
}
