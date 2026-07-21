namespace CivicSurvival.Core.Interfaces.Core
{
    public interface IVanillaDependencyStatus
    {
        bool IsReady { get; }

        string? UnavailableReason { get; }
    }
}
