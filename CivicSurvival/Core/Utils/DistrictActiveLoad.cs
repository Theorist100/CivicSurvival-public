using System.Linq;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Utils
{
    /// <summary>
    /// Single source of truth for "how much KW a district actually draws" given its
    /// schedule / category blackout state. Used by both the city-level consumption
    /// aggregate (<c>PowerGridSingleton.Consumption</c> via
    /// <c>PowerGridDataSystem.CalculateActiveConsumption</c>) and the per-district UI DEL
    /// column (<see cref="UI.DomainState.DistrictDtoFactory"/>), so the two can never
    /// diverge. Without this, a scheduled/blacked-out district shows its full raw load as
    /// "delivered" in the district table while the power-balance panel correctly counts it
    /// as off — the two readouts contradict each other by exactly the shed load.
    /// </summary>
    public static class DistrictActiveLoad
    {
        /// <summary>
        /// KW the district actually draws after schedule / category blackout gating.
        /// Mirrors the gating in <c>PowerGridDataSystem.CalculateActiveConsumption</c>.
        /// </summary>
        /// <param name="districtIndex">
        /// Logical district id. Unzoned / No-District is
        /// <see cref="Engine.Districts.NO_DISTRICT_INDEX"/> (0), matching the key the
        /// blackout/snapshot layer uses.
        /// </param>
        public static int ComputeActiveKW(
            in DistrictStateSnapshot snapshot,
            int districtIndex,
            in DistrictPowerData data)
        {
            // Schedule blackout = whole district offline (window currently active).
            if (snapshot.IsBlackoutActiveForSchedule(districtIndex))
                return 0;

            bool hasPartialBlackout = snapshot.DistrictBlackouts.TryGetValue(districtIndex, out var categories)
                && categories.Count > 0;

            if (!hasPartialBlackout)
                return data.TotalKW;

            if (categories.Count >= Engine.Districts.TOTAL_BUILDING_CATEGORIES)
                return 0;

            int kw = 0;
            if (!categories.Contains(BuildingCategory.Residential)) kw += data.ResidentialKW;
            if (!categories.Contains(BuildingCategory.Commercial))  kw += data.CommercialKW;
            if (!categories.Contains(BuildingCategory.Industrial))  kw += data.IndustrialKW;
            if (!categories.Contains(BuildingCategory.Office))      kw += data.OfficeKW;
            if (!categories.Contains(BuildingCategory.Services))    kw += data.ServicesKW;
            return kw;
        }
    }
}
