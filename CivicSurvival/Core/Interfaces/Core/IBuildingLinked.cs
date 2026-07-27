using Unity.Entities;

namespace CivicSurvival.Core.Interfaces.Core
{
    /// <summary>
    /// Marker interface for mod components that reference a vanilla building entity
    /// via BuildingIndex/BuildingVersion fields.
    /// Used by CleanupBuildingOrphanJob&lt;T&gt; for generic orphan detection.
    /// </summary>
    public interface IBuildingLinked
    {
        Entity GetBuildingEntity();
    }
}
