namespace CivicSurvival.Domains.Engineering.Pipelines.PowerCapacity
{
    internal enum FlowEdgeUpdateResult
    {
        None = 0,
        Updated = 1,
        AlreadyCurrent = 2,
        Unresolved = 3
    }
}
