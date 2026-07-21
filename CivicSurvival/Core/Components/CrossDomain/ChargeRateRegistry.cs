using CivicSurvival.Core.Components.Lifecycle;
using CivicSurvival.Core.Types;
using Unity.Entities;

namespace CivicSurvival.Core.Components.CrossDomain
{
    /// <summary>
    /// Cross-domain singleton: backup power charge rate modifier registry.
    /// Collects multipliers from economy state (PopulationState) and resolves
    /// with floor enforcement — prevents death spiral from near-zero charge rates.
    ///
    /// Written by: BackupPowerRuntimeSystem
    /// Read by: BackupPowerJob (indirectly — system passes resolved float)
    ///
    /// Eliminates: S3-02 (Zombie 400hr recharge — floor enforcement).
    /// </summary>
    public struct ChargeRateRegistry : IComponentData
    {
        public RateModifiers Rate;

        public static class Source
        {
            public const byte EconomyState = 0;
        }

        public static ChargeRateRegistry Default => new()
        {
            Rate = RateModifiers.Create(floor: 0.10f, ceiling: 2.0f)
        };

        public static void EnsureExists(EntityManager em)
        {
            CivicSingleton.Ensure(em, Default);
        }
    }
}
