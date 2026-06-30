using System;
using System.Collections.Generic;

namespace CivicSurvival.Core.Types
{
    /// <summary>
    /// Building type categories for blackout control.
    /// Shared type used across multiple domains.
    /// </summary>
    public enum BuildingCategory : byte
    {
        None = 0,
        Residential = 1,
        Commercial = 2,
        Industrial = 3,
        Office = 4,
        /// <summary>
        /// Non-critical city services: Schools, Parks, Postal, Transport depots.
        /// Critical infrastructure (Hospital, Water, Fire, Police) is handled separately.
        /// </summary>
        Services = 5
    }

    /// <summary>
    /// Schedule presets for automated blackouts.
    /// Each preset defines ON/OFF cycle with automatic phase offset per district.
    /// </summary>
    public enum SchedulePreset
    {
        /// <summary>Always ON until manual intervention or global blackout.</summary>
        Manual = 0,

        /// <summary>4h ON / 2h OFF (33% saving) - Yellow zone response.</summary>
        MildRestriction = 1,

        /// <summary>4h ON / 4h OFF (50% saving) - Standard rolling blackout.</summary>
        Balanced = 2,

        /// <summary>2h ON / 4h OFF (66% saving) - Survival mode.</summary>
        SevereCrisis = 3,

        /// <summary>ON 08:00-20:00, OFF 20:00-08:00 (50% saving) - For offices/commerce.</summary>
        DayShift = 4
    }

    /// <summary>
    /// Helper class for BuildingCategory operations.
    /// </summary>
    public static class BuildingCategories
    {
        /// <summary>
        /// All available building categories (excludes None).
        /// </summary>
        public static readonly IReadOnlyList<BuildingCategory> All = Array.AsReadOnly(new[]
        {
            BuildingCategory.Residential,
            BuildingCategory.Commercial,
            BuildingCategory.Industrial,
            BuildingCategory.Office,
            BuildingCategory.Services
        });
    }

    /// <summary>
    /// FIX DS-03: Helper class for SchedulePreset display names.
    /// Single source of truth - UI reads this instead of maintaining its own mapping.
    /// </summary>
    public static class SchedulePresets
    {
        /// <summary>
        /// Get display name for schedule preset.
        /// Names match UI expectations for consistency.
        /// </summary>
        public static string GetDisplayName(SchedulePreset preset) => preset switch
        {
            SchedulePreset.Manual => "Manual",
            SchedulePreset.MildRestriction => "Mild",
            SchedulePreset.Balanced => "Balanced",
            SchedulePreset.SevereCrisis => "Severe",
            SchedulePreset.DayShift => "Day Shift",
            _ => "Manual"
        };
    }
}
