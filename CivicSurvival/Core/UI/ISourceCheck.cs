using Unity.Entities;

namespace CivicSurvival.Core.UI
{
    /// <summary>
    /// Per-frame readiness check for data sources used by UI bridge publishers.
    /// </summary>
    public interface ISourceCheck
    {
        bool IsAvailable(EntityManager entityManager);
    }
}
