using System;
using System.Collections.Generic;

namespace CivicSurvival.Core.Services
{
    /// <summary>
    /// Canonical handle classification for the news/social channels: which handles are
    /// official sources (Herald/News) versus citizens (CHIPPER), and their display names.
    ///
    /// Lives in Core so the narrative emitters (which choose the channel explicitly at emit
    /// time) and the feed services (which render the author badge) read ONE source of truth
    /// for the handle→official and handle→display mappings, instead of each domain carrying
    /// its own copy. SocialFeedService.IsOfficialAuthor / GetAuthorDisplayName forward here.
    /// </summary>
    public static class NewsAuthorRegistry
    {
#pragma warning disable CIVIC148 // Immutable hardcoded handle registry — no runtime accumulation, identical every reload
        private static readonly HashSet<string> s_OfficialHandles = new(StringComparer.Ordinal)
        {
            "@CityAlert", "@CityEmergency", "@EnergyMinistry", "@DSNS_Official",
            "@MilitaryCommand", "@DefenseMinistry", "@WaterCompany", "@UN_Refugee",
            "@UN_Aid", "@UN_Council", "@NATO_Support", "@NATO_Defense",
            "@NEXTA_Live", "@TRO_Commander",
        };

        private static readonly Dictionary<string, string> s_DisplayNames = new(StringComparer.Ordinal)
        {
            // Citizens (CHIRPER)
            ["@DeputatKotleta"] = "Mykola Kotleta",
            ["@BabcyaZina"] = "Zinaida Petrovna",
            ["@InzhenerPetrenko"] = "Vasyl Petrenko",
            ["@MarianaPravda"] = "Mariana Pravda",
            ["@Valera_Expert"] = "Valera Zhdun",
            ["@Valera_Spotter"] = "Valera Spotterenko",
            ["@ITSysAdmin"] = "Serhiy Serverov",
            ["@Volunteer_Oksana"] = "Oksana Volunteer",
            ["@TaxiDriver_Mykola"] = "Mykola TaxiMan",
            ["@local_resident"] = "Local Resident",
            ["@local_mother"] = "Worried Mother",
            ["@AngryDoctor"] = "Dr. Angry",
            ["@refugee_family"] = "Refugee Family",
            ["@displaced_person"] = "Displaced Person",
            ["@evacuee_kyiv"] = "Kyiv Evacuee",
            ["@kyiv_citizen"] = "Kyiv Citizen",
            ["@ordinary_person"] = "Ordinary Citizen",

            // Officials (NEWS)
            ["@CityAlert"] = "City Emergency Services",
            ["@CityEmergency"] = "Emergency Alert",
            ["@DefenseMinistry"] = "Ministry of Defense",
            ["@EnergyMinistry"] = "Ministry of Energy",
            ["@DSNS_Official"] = "State Emergency Service",
            ["@WaterCompany"] = "Water Utility Company",
            ["@UN_Refugee"] = "UN Refugee Agency",
            ["@UN_Aid"] = "UN Humanitarian Affairs",
            ["@UN_Council"] = "UN Security Council",
            ["@NATO_Support"] = "NATO Support",
            ["@NATO_Defense"] = "NATO Defense",
            ["@NEXTA_Live"] = "NEXTA Live",
        };
#pragma warning restore CIVIC148

        /// <summary>
        /// True when the handle is an official source whose posts belong on the NEWS/Herald
        /// feed rather than the citizen CHIPPER feed.
        /// </summary>
        public static bool IsOfficial(string? handle)
        {
            if (string.IsNullOrEmpty(handle))
                return false;

            // Global News handles (from GlobalNewsSystem)
            if (handle!.StartsWith("@GlobalNews_", StringComparison.Ordinal)
                || handle == "@GlobalChronicle"
                || handle.StartsWith("@GlobalChronicle_", StringComparison.Ordinal))
                return true;

            return s_OfficialHandles.Contains(handle);
        }

        /// <summary>
        /// Human-readable display name for a handle (author/source shown in the feed).
        /// </summary>
        public static string GetDisplayName(string? handle)
        {
            if (string.IsNullOrEmpty(handle))
                return "Unknown";

            // Global News handles (from GlobalNewsSystem)
            if (handle == "@GlobalChronicle")
                return "The Chronicle";

            if (handle!.StartsWith("@GlobalNews_", StringComparison.Ordinal))
            {
                var nick = handle.Substring("@GlobalNews_".Length);
                return $"Global: {nick}";
            }

            return s_DisplayNames.TryGetValue(handle, out var displayName)
                ? displayName
                : handle.TrimStart('@');
        }
    }
}
