using Unity.Burst;
using Unity.Entities;
using Unity.Jobs;
using CivicSurvival.Core.Components.Domain.Economy;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Domains.Economics.Jobs
{
    /// <summary>
    /// Publishes the Economics-owned singleton from a cached entity without a main-thread write.
    /// </summary>
    #if ENABLE_BURST
    [BurstCompile]
    #endif
    public struct WriteEconomyStateJob : IJob
    {
        public ComponentLookup<EconomySingleton> Lookup;
        public Entity SingletonEntity;
        public PopulationState State;
        public float CityIntegrity;

        public void Execute()
        {
            if (!Lookup.HasComponent(SingletonEntity))
                return;

            Lookup[SingletonEntity] = new EconomySingleton
            {
                State = State,
                CityIntegrity = CityIntegrity
            };
        }
    }
}
