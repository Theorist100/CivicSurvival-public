using Unity.Entities;

namespace CivicSurvival.Core.Components.Domain.Power
{
    /// <summary>
    /// Written exclusively by IsolatedGridPatch (Harmony).
    /// Present ONLY on OutsideConnection entities — regular plants never have this component.
    /// </summary>
    public struct ImportCapModifier : IComponentData
    {
        /// <summary>Maximum allowed import capacity in kW (0 = no limit).</summary>
        public int ImportCapLimitKW;
    }
}
