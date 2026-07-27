using CivicSurvival.Core.Components.Lifecycle;
using CivicSurvival.Core.Types;
using Unity.Entities;

namespace CivicSurvival.Core.Components.CrossDomain
{
    /// <summary>
    /// Cross-domain singleton: exodus rate modifier registry.
    /// Collects multipliers from CitySize and Integrity (active writers).
    /// Sanctions and Scenario slots are reserved for future use.
    /// Resolves compound rate with floor/ceiling enforcement.
    ///
    /// Written by: ExodusSystem
    /// Read by: ExodusStateSingleton consumers (indirectly — ExodusSystem writes the resolved rate)
    ///
    /// Eliminates: S2-01 (override × multiplier confusion), unbounded compound stacking.
    /// </summary>
    public struct ExodusRateRegistry : IComponentData
    {
        public RateModifiers Rate;

        public static class Source
        {
            public const byte CitySize = 0;
            public const byte Integrity = 1;
            public const byte Sanctions = 2;
            public const byte Scenario = 3;

            /// <summary>Patriot hero archetype "stand and die" exodus push. Folded by ExodusSystem (pull).</summary>
            public const byte HeroArchetype = 4;
        }

        /// <summary>
        /// Max compound exodus rate (%/day). Crisis 4% × City 1.5x × Zombie 3x = 18%. The Phase-04
        /// Patriot push (~1.4x) applies in normal play (the stack is below the cap); only in the
        /// extreme triple-stack corner does it clamp here — accepted, since exodus is already
        /// catastrophic at 20% and the clamped difference is not gameplay-meaningful. The cap stays
        /// at its tested value rather than loosening for every exodus source.
        /// </summary>
        private const float MAX_COMPOUND_RATE = 20.0f;

        public static ExodusRateRegistry Default => new()
        {
            Rate = RateModifiers.Create(floor: 0f, ceiling: MAX_COMPOUND_RATE)
        };

        public static void EnsureExists(EntityManager em)
        {
            CivicSingleton.Ensure(em, Default);
        }
    }
}
