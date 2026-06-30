using Unity.Entities;

namespace CivicSurvival.Core.Components.Domain.Power
{
    /// <summary>
    /// Written exclusively by GridStressSystem.
    /// When true, plant is shut down due to grid collapse (capacity = 0).
    /// Present on regular power plant entities (NOT OutsideConnection).
    /// </summary>
    public struct GridStressModifier : IComponentData
    {
        /// <summary>Grid has collapsed, this plant is shutdown.</summary>
        public bool IsCollapsed;
    }
}
