using Colossal.Logging;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;

namespace CivicSurvival.Localization
{
    /// <summary>
    /// Immutable container for localization state.
    /// Enables lock-free atomic updates via reference swap.
    /// </summary>
    internal sealed class LocaleContext
    {
        public readonly IReadOnlyDictionary<string, string> Strings;
        public readonly string Locale;

        /// <summary>
        /// Count of consecutive numbered variants per prefix
        /// (e.g. "CHIRP_BLACKOUT" -> 3 when CHIRP_BLACKOUT_1..CHIRP_BLACKOUT_3 exist).
        /// Built once at locale load so <see cref="LocalizationManager.GetRandom"/> never
        /// probes interpolated keys per call.
        /// </summary>
        public readonly IReadOnlyDictionary<string, int> VariantCounts;

        public LocaleContext(Dictionary<string, string> strings, string locale)
        {
            Strings = strings;
            Locale = locale;
            VariantCounts = BuildVariantCounts(strings);
        }

        public bool IsUkrainian => Locale == "uk-UA";

        /// <summary>
        /// Group keys of the form &lt;prefix&gt;_&lt;n&gt; and record how many consecutive
        /// indices starting at 1 are present. Pure parsing of the already-loaded keys — no
        /// lookup key is constructed, so this never reintroduces the runtime-string-as-key
        /// allocation it exists to remove.
        /// </summary>
        private static IReadOnlyDictionary<string, int> BuildVariantCounts(Dictionary<string, string> strings)
        {
            // prefix -> set of present numeric suffixes
            var present = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
            foreach (var key in strings.Keys)
            {
                int underscore = key.LastIndexOf('_');
                if (underscore <= 0 || underscore == key.Length - 1)
                    continue;
                if (!TryParseSuffixIndex(key, underscore + 1, out int index) || index < 1)
                    continue;

                var prefix = key.Substring(0, underscore);
                if (!present.TryGetValue(prefix, out var indices))
                {
                    indices = new HashSet<int>();
                    present[prefix] = indices;
                }
                indices.Add(index);
            }

            var counts = new Dictionary<string, int>(present.Count, StringComparer.Ordinal);
            foreach (var pair in present)
            {
                int count = 0;
                while (pair.Value.Contains(count + 1))
                    count++;
                if (count > 0)
                    counts[pair.Key] = count;
            }
            return counts;
        }

        /// <summary>
        /// Parse a positive integer occupying the whole tail of <paramref name="key"/> from
        /// <paramref name="start"/>. Allocation-free (no Substring), digits only.
        /// </summary>
        private static bool TryParseSuffixIndex(string key, int start, out int value)
        {
            value = 0;
            if (start >= key.Length)
                return false;
            for (int i = start; i < key.Length; i++)
            {
                char c = key[i];
                if (c < '0' || c > '9')
                    return false;
                value = value * 10 + (c - '0');
            }
            return true;
        }
    }

    /// <summary>
    /// Localization manager for Systems Critical.
    /// Supports EN (neutral) and UA (realistic) experiences.
    /// Thread-safety: Lock-free reads via immutable LocaleContext + CAS pattern.
    /// </summary>
    public static class LocalizationManager
    {
        private static readonly LogContext Log = new("Localization");
        private static readonly object s_EventLock = new();

        private static LocaleContext s_Context = new(new Dictionary<string, string>(), "en-US");
        private static readonly HashSet<string> s_WarnedKeys = new();
#pragma warning disable CIVIC080 // Event cleaned in Cleanup() and UnsubscribeFromLocaleChange()
        private static event System.Action? OnLocaleChanged;
#pragma warning restore CIVIC080

        /// <summary>
        /// Available locales.
        /// </summary>
        public static readonly string[] SupportedLocales =
        {
            "en-US", "uk-UA"
        };

        /// <summary>
        /// Current locale code.
        /// Lock-free read.
        /// </summary>
        public static string CurrentLocale => s_Context.Locale;

        /// <summary>
        /// Is Ukrainian locale active?
        /// Lock-free read.
        /// </summary>
        public static bool IsUkrainian => s_Context.IsUkrainian;

        public static bool IsUncensoredBuild
        {
            get
            {
#if UNCENSORED
                return true;
#else
                return false;
#endif
            }
        }

        /// <summary>
        /// Initialize localization system.
        /// </summary>
        public static void Initialize()
        {
            // Determine locale from settings and load
            var locale = GetLocaleFromSettings();
            LoadLocale(locale);
        }

        /// <summary>
        /// Set language and reload strings.
        /// Call this when player changes language preference.
        /// Thread-safe via CAS.
        /// </summary>
        public static void SetLanguage(ModLanguage language)
        {
            var newLocale = ModLanguageToLocale(language);
            var oldLocale = s_Context.Locale;

            if (newLocale == oldLocale)
                return;

            Log.Info($"Changing locale: {oldLocale} -> {newLocale}");
            LoadLocale(newLocale);

            lock (s_EventLock)
            {
                OnLocaleChanged?.Invoke();
            }
        }

        /// <summary>
        /// Subscribe to locale changes (for UI updates).
        /// Thread-safe via lock.
        /// </summary>
        public static void SubscribeToLocaleChange(System.Action callback)
        {
            lock (s_EventLock)
            {
                OnLocaleChanged += callback;
            }
        }

        /// <summary>
        /// Unsubscribe from locale changes.
        /// Thread-safe via lock.
        /// </summary>
        public static void UnsubscribeFromLocaleChange(System.Action callback)
        {
            lock (s_EventLock)
            {
                OnLocaleChanged -= callback;
            }
        }

        /// <summary>
        /// Cleanup static state on mod unload (V6-002).
        /// Prevents stale event subscribers across game sessions.
        /// Thread-safe via atomic assignment.
        /// </summary>
        public static void Cleanup()
        {
            Interlocked.Exchange(ref s_Context, new LocaleContext(new Dictionary<string, string>(), "en-US"));
            s_WarnedKeys.Clear();
            lock (s_EventLock)
            {
                OnLocaleChanged = null;
            }
        }

        /// <summary>
        /// Get locale from ModSettings.
        /// </summary>
        private static string GetLocaleFromSettings()
        {
            var settings = ServiceRegistry.IsInitialized
                ? ServiceRegistry.Instance.Get<ModSettings>()
                : null;
            var preference = settings?.LanguagePreference ?? ModLanguage.GameDefault;
            return ModLanguageToLocale(preference);
        }

        /// <summary>
        /// Convert ModLanguage enum to locale string.
        /// </summary>
        private static string ModLanguageToLocale(ModLanguage language)
        {
            var locale = language switch
            {
                ModLanguage.GameDefault => DetectGameLocale(),
                ModLanguage.English => "en-US",
                ModLanguage.Ukrainian => "uk-UA",
                ModLanguage.German => "de-DE",
                ModLanguage.Spanish => "es-ES",
                ModLanguage.French => "fr-FR",
                ModLanguage.Polish => "pl-PL",
                ModLanguage.Chinese => "zh-CN",
                _ => throw new System.ArgumentOutOfRangeException(nameof(language), language, null)
            };

            return ClampLocaleForBuild(locale);
        }

        /// <summary>
        /// Detect current game locale.
        /// </summary>
        private static string DetectGameLocale()
        {
            try
            {
                // Try to get game locale from Colossal.Localization
                var locManager = Game.SceneFlow.GameManager.instance?.localizationManager;
                if (locManager != null)
                {
                    var activeLocale = locManager.activeLocaleId;
                    Log.Info($"Detected game locale: {activeLocale}");

                    // Map game locale to our supported locales (EN + UA only)
                    if (!string.IsNullOrEmpty(activeLocale))
                    {
                        if (activeLocale.Contains("uk", System.StringComparison.OrdinalIgnoreCase) ||
                            activeLocale.Contains("UA", System.StringComparison.Ordinal))
                            return ClampLocaleForBuild("uk-UA");
                    }
                }
            }
            catch (System.Exception ex)
            {
                Log.WarnException("Failed to detect game locale", ex);
            }

            return "en-US";
        }

        /// <summary>
        /// Is this language visible/selectable for the current build?
        /// GameDefault stays available; the resolved output is clamped by ModLanguageToLocale.
        /// </summary>
        public static bool IsLanguageAvailable(ModLanguage language)
        {
            if (language == ModLanguage.GameDefault || language == ModLanguage.English)
                return true;

#if UNCENSORED
            return language == ModLanguage.Ukrainian;
#else
            return false;
#endif
        }

        /// <summary>
        /// JSON array of language ids available to UI settings for the current build.
        /// </summary>
        public static string AvailableLanguageIdsJson
        {
            get
            {
#if UNCENSORED
                return "[0,1,2]";
#else
                return "[0,1]";
#endif
            }
        }

        private static string ClampLocaleForBuild(string locale)
        {
#if !UNCENSORED
            if (locale == "uk-UA")
                return "en-US";
#endif

            return locale;
        }

        /// <summary>
        /// Load strings for a specific locale.
        /// Thread-safe via CAS.
        /// </summary>
        public static void LoadLocale(string locale)
        {
            var newStrings = new Dictionary<string, string>();
            var actualLocale = locale;

            try
            {
                // Try to load the requested locale
                if (!TryLoadLocaleFile(locale, newStrings))
                {
                    // Fall back to English
                    Log.Warn($"Locale {locale} not found, falling back to en-US");
                    actualLocale = "en-US";
                    _ = TryLoadLocaleFile("en-US", newStrings);
                }
            }
            catch (Exception ex)
            {
                Log.Exception($"Failed to load locale {locale}", ex);
            }

            // Atomic swap via CAS
            var newContext = new LocaleContext(newStrings, actualLocale);
            LocaleContext oldContext;
            do
            {
                oldContext = s_Context;
            }
            while (Interlocked.CompareExchange(ref s_Context, newContext, oldContext) != oldContext);
            s_WarnedKeys.Clear();

            Log.Info($"Loaded {newStrings.Count} strings for locale {actualLocale}");
        }

        /// <summary>
        /// Try to load a locale file from embedded resources into the provided dictionary.
        /// </summary>
        private static bool TryLoadLocaleFile(string locale, Dictionary<string, string> target)
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = $"CivicSurvival.Localization.{locale}.json";

            using (var stream = assembly.GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                    return false;

                using (var reader = new StreamReader(stream))
                {
                    var json = reader.ReadToEnd();
                    var dict = JsonStream.ParseFlatStringDict(json);
                    foreach (var kvp in dict)
                        target[kvp.Key] = kvp.Value;
                    return true;
                }
            }
        }

        /// <summary>
        /// Get localized string by key.
        /// Lock-free read.
        /// </summary>
        public static string Get(string key)
        {
            var ctx = s_Context; // Atomic read
            if (ctx.Strings.TryGetValue(key, out var value))
                return value;

            if (s_WarnedKeys.Add(key))
                Log.Warn($"Missing localization key: {key}");
            return $"[{key}]";
        }

        /// <summary>
        /// Get localized string with format arguments.
        /// Lock-free read.
        /// </summary>
        public static string Get(string key, params object[] args)
        {
            var template = Get(key);
            try
            {
                return string.Format(template, args);
            }
            catch (FormatException ex)
            {
                Log.Warn($"[LocalizationManager] Format error for key '{key}': {ex}");
                return template;
            }
        }

        /// <summary>
        /// Check whether the currently loaded locale contains a key without logging.
        /// </summary>
        public static bool HasKey(string key)
        {
            var ctx = s_Context;
            return !string.IsNullOrEmpty(key) && ctx.Strings.ContainsKey(key);
        }

        /// <summary>
        /// Get a positive integer localization metadata value.
        /// </summary>
        public static int GetPositiveInt(string key, int fallback)
        {
            return int.TryParse(Get(key), out int value) && value > 0 ? value : fallback;
        }

        /// <summary>
        /// Get a random localized string from a numbered set (e.g., KEY_1, KEY_2, KEY_3).
        /// Lock-free read with centralized ThreadSafeRandom.
        /// Variant counts are precomputed at locale load (<see cref="LocaleContext.VariantCounts"/>),
        /// so this builds at most one lookup key per call instead of probing up to ten
        /// interpolated keys against the strings dictionary.
        /// </summary>
        public static string GetRandom(string keyPrefix, params object[] args)
        {
            var ctx = s_Context; // Atomic read - consistent snapshot

            if (!ctx.VariantCounts.TryGetValue(keyPrefix, out int count) || count == 0)
            {
                // No numbered variants — fall back to the base key
                return Get(keyPrefix, args);
            }

            int index = ThreadSafeRandom.Next(count) + 1;
            return Get($"{keyPrefix}_{index}", args);
        }

        /// <summary>
        /// Shorthand for Get().
        /// </summary>
        public static string T(string key) => Get(key);

        /// <summary>
        /// Shorthand for Get() with format.
        /// </summary>
        public static string T(string key, params object[] args) => Get(key, args);

        /// <summary>
        /// Get all loaded strings as JSON for UI binding.
        /// Includes __locale__ key for UI to detect current locale.
        /// Lock-free read.
        /// </summary>
        public static string GetAllStringsAsJson()
        {
            var ctx = s_Context; // Atomic read
            var builder = JsonBuilder.Object();
            foreach (var kvp in ctx.Strings)
            {
                builder.Add(kvp.Key, kvp.Value);
            }
            builder.Add("__locale__", ctx.Locale);
            return builder.Build();
        }
    }

    /// <summary>
    /// Localization keys for type-safe access.
    /// </summary>
    public static class L
    {
        // === UI ===
        public const string MOD_NAME = "MOD_NAME";
        public const string MOD_DESCRIPTION = "MOD_DESCRIPTION";
        public const string UI_BETA_TRAINING_MODE = "UI_BETA_TRAINING_MODE";

        // === Notifications ===
        public const string BLACKOUT_STARTED = "BLACKOUT_STARTED";
        public const string BLACKOUT_ENDED = "BLACKOUT_ENDED";
        public const string POWER_RESTORED = "POWER_RESTORED";
        public const string GENERATOR_STARTED = "GENERATOR_STARTED";
        public const string GENERATOR_NO_FUEL = "GENERATOR_NO_FUEL";

        // === Attacks (Stage 4) ===
        public const string ATTACK_INCOMING = "ATTACK_INCOMING";
        public const string ATTACK_INTERCEPTED = "ATTACK_INTERCEPTED";
        public const string ATTACK_HIT = "ATTACK_HIT";
        public const string AIR_DEFENSE_ACTIVE = "AIR_DEFENSE_ACTIVE";

        // === Chirper ===
        public const string CHIRP_BLACKOUT_1 = "CHIRP_BLACKOUT_1";
        public const string CHIRP_BLACKOUT_2 = "CHIRP_BLACKOUT_2";
        public const string CHIRP_BLACKOUT_3 = "CHIRP_BLACKOUT_3";
        public const string CHIRP_GENERATOR_1 = "CHIRP_GENERATOR_1";
        public const string CHIRP_SHELTER_1 = "CHIRP_SHELTER_1";

        // === Buildings ===
        public const string BUILDING_SHELTER = "BUILDING_SHELTER";
        public const string BUILDING_GENERATOR = "BUILDING_GENERATOR";
        public const string BUILDING_SUBSTATION = "BUILDING_SUBSTATION";

        // === Policies ===
        public const string POLICY_CONSERVATION = "POLICY_CONSERVATION";
        public const string POLICY_EMERGENCY = "POLICY_EMERGENCY";
        public const string POLICY_NORMAL = "POLICY_NORMAL";

        // === Grid Operator ===
        public const string GRID_OPERATOR = "GRID_OPERATOR";
        public const string SCHEDULE_TITLE = "SCHEDULE_TITLE";

        // === Spotter/Valera ===
        public const string SPOTTER_SPAWN = "SPOTTER_SPAWN";
        public const string SPOTTER_REACTIVATE = "SPOTTER_REACTIVATE";
        public const string SPOTTER_SBU_VISIT = "SPOTTER_SBU_VISIT";
        public const string SPOTTER_IMPACT = "SPOTTER_IMPACT";
        public const string MARIANA_SBU_ARTICLE = "MARIANA_SBU_ARTICLE";
        public const string MARIANA_INTERNET_DISABLED = "MARIANA_INTERNET_DISABLED";
        public const string INTERNET_DISABLED = "INTERNET_DISABLED";

        // === Evacuation & Counter-OSINT ===
        public const string SPOTTER_EVACUATED = "SPOTTER_EVACUATED";
        public const string SPOTTER_EVACUATION_RETURN = "SPOTTER_EVACUATION_RETURN";
        public const string COUNTER_OSINT_STARTED = "COUNTER_OSINT_STARTED";
        public const string COUNTER_OSINT_STOPPED = "COUNTER_OSINT_STOPPED";
        public const string COUNTER_OSINT_CANCELLED = "COUNTER_OSINT_CANCELLED";

        // === Threat UI ===
        public const string THREAT_EARLY_WARNING = "THREAT_EARLY_WARNING";
        public const string THREAT_INTEL_REPORT = "THREAT_INTEL_REPORT";
        public const string THREAT_DETECTED = "THREAT_DETECTED";
        public const string THREAT_NO_ACTIVE = "THREAT_NO_ACTIVE";

        // === Defense Policy ("The Hard Choice") ===
        public const string DEFENSE_POLICY_HUMANITARIAN = "DEFENSE_POLICY_HUMANITARIAN";
        public const string DEFENSE_POLICY_HUMANITARIAN_DESC = "DEFENSE_POLICY_HUMANITARIAN_DESC";
        public const string DEFENSE_POLICY_GRID = "DEFENSE_POLICY_GRID";
        public const string DEFENSE_POLICY_GRID_DESC = "DEFENSE_POLICY_GRID_DESC";
        public const string NOTIFY_TITLE_SCANDAL = "NOTIFY_TITLE_SCANDAL";
        public const string NOTIFY_SCANDAL_HOSPITAL_HIT = "NOTIFY_SCANDAL_HOSPITAL_HIT";
        public const string SCANDAL_MARIANNA_HOSPITAL_1 = "SCANDAL_MARIANNA_HOSPITAL_1";
        public const string SCANDAL_MARIANNA_HOSPITAL_2 = "SCANDAL_MARIANNA_HOSPITAL_2";
        public const string SCANDAL_DOCTOR_REACT = "SCANDAL_DOCTOR_REACT";

        // === Mobilization ===
        public const string NOTIFY_TITLE_MANPOWER = "NOTIFY_TITLE_MANPOWER";
        public const string NOTIFY_MANPOWER_CRITICAL_MSG = "NOTIFY_MANPOWER_CRITICAL_MSG";
        public const string NOTIFY_TITLE_CONSCRIPTION = "NOTIFY_TITLE_CONSCRIPTION";
        public const string NOTIFY_CONSCRIPTION_ACTIVE_MSG = "NOTIFY_CONSCRIPTION_ACTIVE_MSG";
        public const string NOTIFY_CONSCRIPTION_ENDED_MSG = "NOTIFY_CONSCRIPTION_ENDED_MSG";
        public const string NOTIFY_INSUFFICIENT_MANPOWER_MSG = "NOTIFY_INSUFFICIENT_MANPOWER_MSG";
        public const string NEWS_MANPOWER_CRITICAL = "NEWS_MANPOWER_CRITICAL";
        public const string CHIRP_MANPOWER_CRITICAL = "CHIRP_MANPOWER_CRITICAL";
        public const string NEWS_CONSCRIPTION_ACTIVATED = "NEWS_CONSCRIPTION_ACTIVATED";
        public const string CHIRP_CONSCRIPTION = "CHIRP_CONSCRIPTION";
    }
}
