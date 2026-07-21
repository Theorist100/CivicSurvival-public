using CivicSurvival.Core.Components.Lifecycle;
using Unity.Entities;

namespace CivicSurvival.Core.Components.Domain.Engineering
{
    /// <summary>
    /// A2 FIX 2b: Winter climate state as ECS singleton.
    /// Replaces PowerGridSingleton.IsWinterActive (was multi-writer violation).
    ///
    /// Access: SystemAPI.GetSingleton&lt;WinterStateSingleton&gt;()
    ///
    /// Writer: WinterMultiplierSystem (Engineering domain)
    /// Readers: DistrictPenaltySystem (Core)
    /// </summary>
    public struct WinterStateSingleton : IComponentData
    {
        /// <summary>Whether winter climate multiplier is currently active.</summary>
        public bool IsWinterActive;

        public static WinterStateSingleton Default => new();

        public static void EnsureExists(EntityManager em)
        {
            CivicSingleton.Ensure(em, Default);
        }
    }
}
