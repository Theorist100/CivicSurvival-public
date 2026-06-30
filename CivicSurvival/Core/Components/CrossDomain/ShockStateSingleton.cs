using CivicSurvival.Core.Components.Lifecycle;
using Unity.Entities;
using CivicSurvival.Core.Interfaces;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Components.CrossDomain
{
    /// <summary>
    /// Singleton: UI-facing world shock/attention data.
    /// Written by: WorldShockSystem
    /// Read by: AttentionUIPanel, DonorConferenceUIPanel
    ///
    /// Note: Full state (decay timers, etc.) stays in WorldShockSystem.
    /// This singleton exposes only UI-relevant fields.
    /// </summary>
    [CivicSingleton]
    public struct ShockStateSingleton : IComponentData
    {
        /// <summary>Current shock level 0-100%.</summary>
        public float ShockLevel;

        /// <summary>Current aid tier (DeepConcern, Headlines, GlobalShock).</summary>
        public AidTier CurrentTier;

        /// <summary>Casualties this reporting period.</summary>
        public int CasualtiesThisWeek;

        /// <summary>Buildings destroyed this reporting period.</summary>
        public int BuildingsDestroyedThisWeek;

        /// <summary>Critical infrastructure hits this reporting period.</summary>
        public int CriticalHitsThisWeek;

        /// <summary>Cumulative totals (never reset).</summary>
        public long TotalCasualties;
        public long TotalBuildingsDestroyed;
        public long TotalCriticalHits;
        /// <summary>Subset of TotalBuildingsDestroyed — only civilian (non-PP) buildings.</summary>
        public long TotalCivilianBuildingsDestroyed;

        public static ShockStateSingleton Default => new()
        {
            ShockLevel = 0f,
            CurrentTier = AidTier.DeepConcern,
            CasualtiesThisWeek = 0,
            BuildingsDestroyedThisWeek = 0,
            CriticalHitsThisWeek = 0,
            TotalCasualties = 0,
            TotalBuildingsDestroyed = 0,
            TotalCriticalHits = 0,
            TotalCivilianBuildingsDestroyed = 0
        };

        public static void EnsureExists(EntityManager em)
        {
            CivicSingleton.Ensure(em, Default);
        }
    }
}
