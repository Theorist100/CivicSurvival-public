using CivicSurvival.Core.Components.Lifecycle;
using Unity.Entities;

namespace CivicSurvival.Core.Components.Domain.Corruption
{
    /// <summary>
    /// Aggregated contract statistics as ECS singleton.
    /// Used for corruption calculations.
    ///
    /// Access: SystemAPI.GetSingleton&lt;ContractStatsSingleton&gt;()
    /// Modify: SystemAPI.GetSingletonRW&lt;ContractStatsSingleton&gt;()
    ///
    /// Writer: MaintenanceContractSystem
    /// Readers: CorruptionDataAdapter (via delegate). Maintenance contract UI
    /// counts live ContractData instead of this throttled aggregate.
    /// </summary>
    public struct ContractStatsSingleton : IComponentData
    {
        /// <summary>Count of active shady (corrupt) contracts.</summary>
        public int ShadyContractCount;

        /// <summary>Total count of all active contracts.</summary>
        public int TotalContractCount;

        public void SetDefaults() => this = Default;

        public static ContractStatsSingleton Default => new()
        {
            ShadyContractCount = 0,
            TotalContractCount = 0
        };

        public static void EnsureExists(EntityManager em)
        {
            CivicSingleton.Ensure(em, Default);
        }
    }
}


