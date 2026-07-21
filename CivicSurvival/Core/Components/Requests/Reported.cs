using Unity.Entities;

namespace CivicSurvival.Core.Components.Requests
{
    /// <summary>Marker added after RequestResultCollectorSystem publishes a RequestResultEvent to UI state.</summary>
    public struct Reported : IComponentData { }
}
