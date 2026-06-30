using Colossal.Serialization.Entities;
using Unity.Entities;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Components.Threats;

namespace CivicSurvival.Domains.Attention.Data
{
    /// <summary>
    /// Singleton: tracks global attention to the city's suffering.
    /// The "Attention Economy" - blood buys weapons.
    /// </summary>
    public struct WorldShockState : IComponentData, IEmptySerializable
    {
        private const float DEFAULT_DECAY_PER_DAY = 5f;

        /// <summary>Current shock level 0-100%.</summary>
        public float ShockLevel;

        /// <summary>Calculated from ShockLevel using tier thresholds.</summary>
        public AidTier CurrentTier;

        /// <summary>For decay calculation.</summary>
        public double LastUpdateTime;

        /// <summary>Stats for current period (computed: 7-day rolling window).</summary>
        public int CasualtiesThisWeek;
        public int BuildingsDestroyedThisWeek;
        public int CriticalHitsThisWeek;

        /// <summary>Cumulative totals (never reset, persisted).</summary>
        public long TotalCasualties;
        public long TotalBuildingsDestroyed;
        /// <summary>Subset of TotalBuildingsDestroyed — only civilian (non-PP) buildings.</summary>
        public long TotalCivilianBuildingsDestroyed;
        public long TotalCriticalHits;

        /// <summary>Decay rate (default from Balance.Attention.DECAY_PER_DAY).</summary>
        public float DecayPerDay;

        /// <summary>Resets decay timer - no decay for 24h after tragedy.</summary>
        public double LastTragedyTime;

        public void SetDefaults()
        {
            this = default;
            ShockLevel = 0f;
            CurrentTier = AidTier.DeepConcern;
            DecayPerDay = DEFAULT_DECAY_PER_DAY;
        }

        // IEmptySerializable marker: WorldShockSystem is the canonical
        // serialization path; this singleton is a runtime mirror.
    }
}


