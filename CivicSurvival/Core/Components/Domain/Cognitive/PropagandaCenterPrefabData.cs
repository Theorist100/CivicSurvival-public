using Unity.Entities;

namespace CivicSurvival.Core.Components.Domain.Cognitive
{
    /// <summary>
    /// Derived marker stamped on the Propaganda Center prefab entity by
    /// <c>CivicPrefabInitSystem</c>. Presence of this component identifies a
    /// prefab as a valid Propaganda Center target for the generic building-placement
    /// pipeline (<c>PropagandaCenterPlacementHandler</c>).
    ///
    /// The Center currently reuses the Patriot SAM visual (placeholder model), so the same
    /// prefab entity also carries <c>AirDefensePrefabData</c>. The two markers coexist and
    /// never conflict: the placement pipeline dispatches by the <c>BuildingPlacementKind</c>
    /// carried on the (singleton) pending record chosen at command time, not by which marker
    /// a prefab happens to have.
    /// </summary>
    public struct PropagandaCenterPrefabData : IComponentData
    {
    }
}
