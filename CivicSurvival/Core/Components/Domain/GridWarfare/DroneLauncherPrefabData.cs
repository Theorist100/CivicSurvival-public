using Unity.Entities;

namespace CivicSurvival.Core.Components.Domain.GridWarfare
{
    /// <summary>
    /// Derived marker stamped on the drone-launcher prefab entity by
    /// <c>CivicPrefabInitSystem</c>. Presence of this component identifies a prefab as a
    /// valid drone-launcher target for the generic building-placement pipeline
    /// (<c>DroneLauncherPlacementHandler</c>).
    ///
    /// The launcher is a field emplacement (like an AA installation), not a road-side
    /// building, so the setup adds only this marker — no PlaceableObjectData/BuildingData
    /// road-snap wiring.
    /// </summary>
    public struct DroneLauncherPrefabData : IComponentData
    {
    }
}
