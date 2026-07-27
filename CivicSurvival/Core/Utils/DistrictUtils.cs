using CivicSurvival.Localization;

namespace CivicSurvival.Core.Utils
{
    /// <summary>
    /// Utility methods for working with districts.
    ///
    /// IMPORTANT: District index 0 = unzoned area (localized via UI_DISTRICT_UNZONED).
    /// This is a VIRTUAL bucket for buildings without CurrentDistrict.m_District.
    /// There is NO actual District entity with index 0 - it's created artificially
    /// to match PowerGrid behavior and provide consistent tracking across systems.
    ///
    /// </summary>
    public static class DistrictUtils
    {
        /// <summary>
        /// Virtual district index for buildings without a district assignment.
        /// </summary>
        public const int UNZONED_AREA_INDEX = 0;

        /// <summary>
        /// Get display name for a district index.
        /// Returns the localized unzoned label for index 0, otherwise the localized
        /// numbered-district template. For proper localized names, use NameSystemFacade.
        /// </summary>
        public static string GetFallbackName(int districtIndex)
        {
            return districtIndex == UNZONED_AREA_INDEX
                ? LocalizationManager.T("UI_DISTRICT_UNZONED")
                : LocalizationManager.T("UI_DISTRICT_FALLBACK", districtIndex);
        }
    }
}
