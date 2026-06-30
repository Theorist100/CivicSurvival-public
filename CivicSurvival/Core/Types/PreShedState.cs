using System.Collections.Generic;

namespace CivicSurvival.Core.Types
{
    /// <summary>
    /// Captures player's district state before AutoDispatch shed.
    /// Used to restore player intent after recovery (not hardcoded Manual/AllON).
    ///
    /// Rules:
    /// - Created on FIRST shed only (cascade shed does NOT overwrite)
    /// - Player changes during shed UPDATE this state (new intent)
    /// - Cleared on final restore step
    /// </summary>
    public struct PreShedState
    {
        private HashSet<BuildingCategory> m_CategoriesOff;

        /// <summary>Player's schedule before AutoDispatch modified it.</summary>
        public SchedulePreset Schedule;

        /// <summary>True when Schedule came from an explicit district override, false when inheriting city schedule.</summary>
        public bool HadExplicitSchedule;

        /// <summary>
        /// Categories the player had manually turned off before shed.
        /// Intentionally reference-typed; callers that copy the struct must write back after mutation.
        /// </summary>
        public HashSet<BuildingCategory> CategoriesOff
        {
            get
            {
                m_CategoriesOff ??= new HashSet<BuildingCategory>();
                return m_CategoriesOff;
            }
        }

        /// <summary>Whether district had VIP status before CRITICAL shed stripped it.</summary>
        public bool WasVip;

        /// <summary>Whether district had VIP bypass status before CRITICAL shed stripped it.</summary>
        public bool WasVipBypass;

        public PreShedState(
            SchedulePreset schedule,
            HashSet<BuildingCategory> categoriesOff,
            bool wasVip = false,
            bool hadExplicitSchedule = true,
            bool wasVipBypass = false)
        {
            Schedule = schedule;
            m_CategoriesOff = categoriesOff ?? new HashSet<BuildingCategory>();
            WasVip = wasVip;
            HadExplicitSchedule = hadExplicitSchedule;
            WasVipBypass = wasVipBypass;
        }

        /// <summary>
        /// Capture current district state as pre-shed snapshot.
        /// </summary>
        public static PreShedState Capture(
            SchedulePreset currentSchedule,
            IEnumerable<BuildingCategory> allCategories,
            System.Func<BuildingCategory, bool> isCategoryOff,
            bool isVip = false,
            bool isVipBypass = false,
            bool hadExplicitSchedule = true)
        {
            var catsOff = new HashSet<BuildingCategory>();
            foreach (var cat in allCategories)
            {
                if (isCategoryOff(cat))
                    catsOff.Add(cat);
            }
            return new PreShedState(currentSchedule, catsOff, isVip, hadExplicitSchedule, isVipBypass);
        }
    }
}
