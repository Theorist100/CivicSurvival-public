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
            "@CityAlert", "@CityEmergency", "@EnergyMinistry", "@RescueService_Official",
            "@MilitaryCommand", "@DefenseMinistry", "@WaterCompany", "@UN_Refugee",
            "@UN_Aid", "@UN_Council", "@Coalition_Support", "@Coalition_Defense",
            "@FreePress_Live", "@TerritorialDefense",
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
            ["@evacuee_city"] = "City Evacuee",
            ["@city_citizen"] = "City Citizen",
            ["@ordinary_person"] = "Ordinary Citizen",

            // The hero speaker (CHIRPER) — one handle per archetype, so the feed shows WHO is
            // on air and the avatar map below can give each their own face.
            ["@holos_live"] = "The Voice",
            ["@calm_analysis"] = "Arestovych",
            ["@patriot_stands"] = "The Patriot",

            // Officials (NEWS)
            ["@CityAlert"] = "City Emergency Services",
            ["@CityEmergency"] = "Emergency Alert",
            ["@MilitaryCommand"] = "Military Command",
            ["@TerritorialDefense"] = "Territorial Defense Command",
            ["@DefenseMinistry"] = "Ministry of Defense",
            ["@EnergyMinistry"] = "Ministry of Energy",
            ["@RescueService_Official"] = "State Emergency Service",
            ["@WaterCompany"] = "Water Utility Company",
            ["@UN_Refugee"] = "UN Refugee Agency",
            ["@UN_Aid"] = "UN Humanitarian Affairs",
            ["@UN_Council"] = "UN Security Council",
            ["@Coalition_Support"] = "Coalition Support",
            ["@Coalition_Defense"] = "Coalition Defense",
            ["@FreePress_Live"] = "Free Press Live",
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

            if (handle!.StartsWith("@GlobalChronicle_", StringComparison.Ordinal))
            {
                var nick = handle.Substring("@GlobalChronicle_".Length);
                return $"Chronicle: {nick}";
            }

            if (handle.StartsWith("@GlobalNews_", StringComparison.Ordinal))
            {
                var nick = handle.Substring("@GlobalNews_".Length);
                return $"Global: {nick}";
            }

            return s_DisplayNames.TryGetValue(handle, out var displayName)
                ? displayName
                : handle.TrimStart('@');
        }

        /// <summary>
        /// Avatar id for a handle — the face the feed draws for a speaker who matters. Empty
        /// for the ordinary crowd (they wear the CHIPPER bird). The id is an icon name the UI
        /// resolves against its own icon set (see Icons.tsx ICON_MAP), so the C# side names a
        /// face, not a file path.
        /// </summary>
        public static string GetAvatarId(string? handle)
        {
            if (string.IsNullOrEmpty(handle))
                return "";

            return s_Avatars.TryGetValue(handle!, out var avatar) ? avatar : "";
        }

#pragma warning disable CIVIC148 // Immutable hardcoded avatar registry — no runtime accumulation, identical every reload
        private static readonly Dictionary<string, string> s_Avatars = new(StringComparer.Ordinal)
        {
            // The speaker the player fielded — his work is otherwise invisible in the feed.
            ["@holos_live"] = "mic",
            ["@calm_analysis"] = "brain",
            ["@patriot_stands"] = "shield",

            // The enemy's carriers (PsyOpsLaunchSystem raid chatter + IPSOBotMessageSystem bots).
            // Three faces, not one: the player should read WHICH weapon is talking, not just
            // that "a bot" is. Loudspeaker = capitulation broadcast, torn tabloid = fabricated
            // media, peeper = the anonymous rumour-monger.
            ["@FreedomVoice_City"] = "megaphone",
            ["@TruthPatriot_City"] = "megaphone",
            ["@RealNews24"] = "fakenews",
            ["@NarodnaGazeta"] = "fakenews",
            ["@LocalInsider"] = "insider",

            // The city's own voices, by trade. The bird is the PLATFORM's mark, so a feed where
            // everyone wears it reads as a wall of logos; a citizen with a trade wears the trade,
            // and the bird is left to the nameless crowd below (no entry = bird).
            ["@InzhenerPetrenko"] = "wrench",
            ["@GridEngineer"] = "wrench",
            ["@HospitalStaff"] = "medic",
            ["@AngryDoctor"] = "medic",
            ["@ITSupport"] = "monitor",
            ["@ITSysAdmin"] = "monitor",
            ["@TaxiDriver_Mykola"] = "taxi",
            ["@Volunteer_Oksana"] = "handshake",
            ["@VolunteerBus"] = "handshake",
            ["@EmergencyServices"] = "alert",

            // The displaced — a life carried in one bag.
            ["@refugee_family"] = "suitcase",
            ["@displaced_person"] = "suitcase",
            ["@evacuee_city"] = "suitcase",
            ["@local_mother"] = "suitcase",

            // Watchers of the sky and of the authorities.
            ["@Valera_Spotter"] = "radar",
            ["@Valera_Expert"] = "radar",
            ["@MarianaPravda"] = "news",
            ["@LocalNewsDaily"] = "news",
            ["@BBCWorld"] = "news",
            ["@OppositionWatchdog"] = "news",
            ["@WarHistorian"] = "news",

            // City hall and the money.
            ["@MayorOfficial"] = "city",
            ["@LocalMayor"] = "city",
            ["@LocalAdministration"] = "city",
            ["@DeputatKotleta"] = "city",
            ["@CityBank"] = "money",
        };
#pragma warning restore CIVIC148
    }
}
