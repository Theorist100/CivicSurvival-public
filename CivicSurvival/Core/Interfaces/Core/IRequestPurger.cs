namespace CivicSurvival.Core.Interfaces.Core
{
    /// <summary>
    /// Marker for systems that own post-load purging of transient request entities.
    /// </summary>
#pragma warning disable CA1040 // Analyzer-facing marker interface for request purge ownership.
    public interface IRequestPurger
    {
    }
#pragma warning restore CA1040
}
