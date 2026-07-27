using CivicSurvival.Core.Components.Lifecycle;
using CivicSurvival.Core.Types;
using Unity.Entities;

namespace CivicSurvival.Core.Components.CrossDomain
{
    /// <summary>
    /// Cross-domain singleton: commerce multiplier registry.
    /// Collects multipliers from Crisis, Integrity, Sanctions, Internet, PreWar
    /// and resolves with floor/ceiling enforcement.
    ///
    /// Written by: CrisisEconomicsSystem
    /// Read by: CrisisEconomicsAdapter (for Harmony patches), UI bindings
    ///
    /// Eliminates: S6-01 (near-zero commerce collapse), adds ceiling for panic buying.
    /// </summary>
    public struct CommerceRateRegistry : IComponentData
    {
        public RateModifiers Rate;

        public static class Source
        {
            public const byte Crisis = 0;
            public const byte Integrity = 1;
            public const byte Sanctions = 2;
            public const byte Internet = 3;
            public const byte PreWar = 4;
            // Rumor resource-run: the economy bite of a panic-buying run on Food.
            // A run reduces city-wide commerce demand (< 1) — the shortage's economic drag,
            // separate from the belief/infection vector (no double-count). Fed from the Core
            // ResourceRunAdapter by CrisisEconomicsSystem; 1 = no run.
            public const byte ResourceRun = 5;
        }

        public static CommerceRateRegistry Default => new()
        {
            Rate = RateModifiers.Create(floor: 0.01f, ceiling: 2.0f)
        };

        public static void EnsureExists(EntityManager em)
        {
            CivicSingleton.Ensure(em, Default);
        }
    }
}
