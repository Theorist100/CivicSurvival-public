using CivicSurvival.Core.Components.Lifecycle;
using Unity.Entities;

namespace CivicSurvival.Core.Components.CrossDomain
{
    /// <summary>
    /// Population state based on cognitive integrity (The Gradient).
    /// Shared across domains: CrisisEconomics writes, PowerBackup reads.
    /// </summary>
    public enum PopulationState
    {
        Loyal = 0,   // 80-100% integrity - normal
        Anxious,     // 50-80% integrity - panic buying
        Rebellious,  // 30-50% integrity - tax strike
        Brainwashed, // 10-30% integrity - collapse
        Zombie       // <10% integrity - failed state
    }

    /// <summary>
    /// Cross-domain singleton: economy state readable by other domains.
    /// Written by: CrisisEconomicsSystem
    /// Read by: BackupPowerRuntimeSystem (charge rate modulation)
    /// </summary>
    public struct EconomySingleton : IComponentData
    {
        public PopulationState State;
        public float CityIntegrity;

        public static EconomySingleton Default => new()
        {
            State = PopulationState.Loyal,
            CityIntegrity = 1f
        };

        public static void EnsureExists(EntityManager em)
        {
            CivicSingleton.Ensure(em, Default);
        }
    }
}
