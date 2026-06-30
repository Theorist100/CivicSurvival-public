using System;
using System.Collections.Generic;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces.Domain.Scenario;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;
using CivicSurvival.Localization;

namespace CivicSurvival.Core.Services
{
    /// <summary>
    /// Central registry for satire messages from all domains.
    ///
    /// DIP Pattern:
    /// - Domains register their ISatireProvider implementations
    /// - Consumers (Narrative, Notifications) use this registry
    /// - No direct domain-to-domain dependencies
    ///
    /// Usage:
    ///   SatireRegistry.Register(new BlackoutSatireProvider());
    ///   var config = SatireRegistry.GetConfig("BLACKOUT_STARTED");
    ///   var msg = SatireRegistry.GetMessage("SATIRE_BLACKOUT", districtName);
    /// </summary>
    public static class SatireRegistry
    {
        private static readonly LogContext Log = new("SatireRegistry");

        private static readonly object s_Lock = new();
        private static readonly Dictionary<string, SatireConfig> s_Configs = new();
        private static readonly Dictionary<string, string> s_KeyToDomain = new();
        private static readonly Dictionary<string, int> s_LastVariantIndex = new();
        private static readonly HashSet<string> s_MissingKeyWarnings = new();
        private static readonly List<ISatireProvider> s_Providers = new();
        private static readonly RequiredResolverKey[] s_RequiredResolverKeys =
        {
            new("SATIRE_KOTLETA_LAWYER", FeatureIds.CorruptionName),
            new("SATIRE_KOTLETA_FAKE_NEWS", FeatureIds.CorruptionName),
        };

        /// <summary>
        /// Register a satire provider from a domain.
        /// Called during mod initialization.
        /// Thread-safe via lock.
        /// </summary>
        public static void Register(ISatireProvider provider)
        {
            if (provider == null) return;

            int configCount;
            string domain = provider.Domain;
            List<string>? invalidConfigs = null;
            List<string>? overwrittenConfigs = null;
            lock (s_Lock)
            {
                s_Providers.Add(provider);
                var configs = provider.GetConfigs();
                configCount = configs.Count;

                foreach (var kvp in configs)
                {
                    var config = kvp.Value;
                    if (config.VariantCount > 0 && string.IsNullOrEmpty(config.BaseLocalizationKey))
                    {
                        invalidConfigs ??= new List<string>();
                        invalidConfigs.Add(kvp.Key);
                    }

                    if (s_Configs.ContainsKey(kvp.Key))
                    {
                        overwrittenConfigs ??= new List<string>();
                        overwrittenConfigs.Add($"{kvp.Key}:{s_KeyToDomain[kvp.Key]}");
                    }

                    s_Configs[kvp.Key] = config;
                    s_KeyToDomain[kvp.Key] = domain;
                }
            }
            if (invalidConfigs != null)
            {
                foreach (var trigger in invalidConfigs)
                    Log.Warn($"Provider {domain} registered satire trigger {trigger} without a base localization key");
            }
            if (overwrittenConfigs != null)
            {
                foreach (var entry in overwrittenConfigs)
                {
                    int split = entry.IndexOf(':', StringComparison.Ordinal);
                    string trigger = split >= 0 ? entry.Substring(0, split) : entry;
                    string previous = split >= 0 ? entry.Substring(split + 1) : "Unknown";
                    Log.Warn($"Provider {domain} overwrites satire trigger {trigger} previously registered by {previous}");
                }
            }

            if (Log.IsDebugEnabled) Log.Debug($"Registered {configCount} configs from {domain}");
        }

        /// <summary>
        /// Try to get config by trigger tag.
        /// Thread-safe via lock.
        /// </summary>
        public static bool TryGetConfig(string triggerTag, out SatireConfig config)
        {
            lock (s_Lock)
            {
                return s_Configs.TryGetValue(triggerTag, out config);
            }
        }

        /// <summary>
        /// Get config by trigger tag (returns Empty if not found).
        /// Thread-safe via lock.
        /// </summary>
        public static SatireConfig GetConfig(string triggerTag)
        {
            lock (s_Lock)
            {
                return s_Configs.TryGetValue(triggerTag, out var config) ? config : SatireConfig.Empty;
            }
        }

        /// <summary>
        /// Get a random message by key prefix.
        /// Thread-safe via lock.
        /// </summary>
        public static string GetMessage(string keyPrefix)
        {
            SatireConfig config;
            bool found;
            lock (s_Lock)
            {
                found = s_Configs.TryGetValue(keyPrefix, out config) && config.VariantCount > 0;
            }
            if (!found)
            {
                WarnMissingKeyOnce(keyPrefix);
                return keyPrefix;
            }
            int index = NextVariantIndex(keyPrefix, config.VariantCount);
            return LocalizationManager.Get($"{config.BaseLocalizationKey}_{index}");
        }

        /// <summary>
        /// Get a random message by key prefix with one argument.
        /// Thread-safe via lock.
        /// </summary>
        public static string GetMessage(string keyPrefix, object arg0)
        {
            SatireConfig config;
            bool found;
            lock (s_Lock)
            {
                found = s_Configs.TryGetValue(keyPrefix, out config) && config.VariantCount > 0;
            }
            if (!found)
            {
                WarnMissingKeyOnce(keyPrefix);
                return keyPrefix;
            }
            int index = NextVariantIndex(keyPrefix, config.VariantCount);
            return LocalizationManager.Get($"{config.BaseLocalizationKey}_{index}", arg0);
        }

        /// <summary>
        /// Get a random message by key prefix with two arguments.
        /// Thread-safe via lock.
        /// </summary>
        public static string GetMessage(string keyPrefix, object arg0, object arg1)
        {
            SatireConfig config;
            bool found;
            lock (s_Lock)
            {
                found = s_Configs.TryGetValue(keyPrefix, out config) && config.VariantCount > 0;
            }
            if (!found)
            {
                WarnMissingKeyOnce(keyPrefix);
                return keyPrefix;
            }
            int index = NextVariantIndex(keyPrefix, config.VariantCount);
            return LocalizationManager.Get($"{config.BaseLocalizationKey}_{index}", arg0, arg1);
        }

        /// <summary>
        /// Generate message from config with variable args.
        /// Thread-safe via lock.
        /// </summary>
        public static string GetMessageFromConfig(SatireConfig config, params object[] args)
        {
            if (config.VariantCount == 0)
                return config.BaseLocalizationKey;

            int index = NextVariantIndex(config.BaseLocalizationKey, config.VariantCount);
            string key = $"{config.BaseLocalizationKey}_{index}";

            return args.Length switch
            {
                0 => LocalizationManager.Get(key),
                1 => LocalizationManager.Get(key, args[0]),
                2 => LocalizationManager.Get(key, args[0], args[1]),
                _ => LocalizationManager.Get(key, args[0], args[1]) // Max 2 args supported
            };
        }

        /// <summary>
        /// Check if a key is registered.
        /// Thread-safe via lock.
        /// </summary>
        public static bool HasKey(string keyPrefix)
        {
            lock (s_Lock)
            {
                return s_Configs.ContainsKey(keyPrefix);
            }
        }

        /// <summary>
        /// Get all registered keys (for debugging).
        /// Thread-safe via lock (returns copy).
        /// </summary>
        public static IReadOnlyCollection<string> GetAllKeys()
        {
            lock (s_Lock)
            {
                return new List<string>(s_Configs.Keys);
            }
        }

        /// <summary>
        /// Get domain that owns a key.
        /// Thread-safe via lock.
        /// </summary>
        public static string GetDomain(string keyPrefix)
        {
            lock (s_Lock)
            {
                return s_KeyToDomain.TryGetValue(keyPrefix, out var domain) ? domain : "Unknown";
            }
        }

        /// <summary>
        /// Startup content gate for literal resolver keys and loaded-locale variants.
        /// </summary>
        public static bool ValidateStartupContent(FeatureManifest? manifest = null)
        {
            var failures = new List<string>();
            Dictionary<string, SatireConfig> configs;
            lock (s_Lock)
            {
                configs = new Dictionary<string, SatireConfig>(s_Configs);
            }

            for (int i = 0; i < s_RequiredResolverKeys.Length; i++)
            {
                var required = s_RequiredResolverKeys[i];
                if (manifest != null && !manifest.IsWaveReached(required.OwnerFeatureId))
                    continue;

                string key = required.Key;
                if (!configs.TryGetValue(key, out var config) || config.VariantCount <= 0)
                    failures.Add($"resolver key {key} is not registered with a positive variant count");
            }

            foreach (var pair in configs)
            {
                var config = pair.Value;
                if (config.VariantCount <= 0)
                    continue;

                for (int variant = 1; variant <= config.VariantCount; variant++)
                {
                    string localeKey = $"{config.BaseLocalizationKey}_{variant}";
                    if (!LocalizationManager.HasKey(localeKey))
                        failures.Add($"locale {LocalizationManager.CurrentLocale} missing {localeKey} for {pair.Key}");
                }
            }

            if (failures.Count == 0)
            {
                Log.Info($"Startup content validation OK ({configs.Count} satire keys, locale {LocalizationManager.CurrentLocale})");
                return true;
            }

            foreach (var failure in failures)
                Log.Error($"Startup content validation failed: {failure}");

            return false;
        }

        /// <summary>
        /// Clear all registrations (for testing).
        /// Thread-safe via lock.
        /// </summary>
        public static void Clear()
        {
            lock (s_Lock)
            {
                s_Configs.Clear();
                s_KeyToDomain.Clear();
                s_LastVariantIndex.Clear();
                s_MissingKeyWarnings.Clear();
                s_Providers.Clear();
            }
        }

        private static int NextVariantIndex(string key, int variantCount)
        {
            if (variantCount <= 1)
                return 1;

            lock (s_Lock)
            {
                int next;
                if (s_LastVariantIndex.TryGetValue(key, out int last))
                {
                    int zeroBasedLast = last - 1;
                    int offset = ThreadSafeRandom.Next(1, variantCount);
                    next = 1 + ((zeroBasedLast + offset) % variantCount);
                }
                else
                {
                    next = ThreadSafeRandom.Next(1, variantCount + 1);
                }

                s_LastVariantIndex[key] = next;
                return next;
            }
        }

        private static void WarnMissingKeyOnce(string keyPrefix)
        {
            lock (s_Lock)
            {
                if (!s_MissingKeyWarnings.Add(keyPrefix))
                    return;
            }

            Log.Warn($"Unknown satire key: {keyPrefix}");
        }

        private readonly struct RequiredResolverKey
        {
            public RequiredResolverKey(string key, string ownerFeatureId)
            {
                Key = key;
                OwnerFeatureId = ownerFeatureId;
            }

            public readonly string Key;
            public readonly string OwnerFeatureId;
        }
    }
}
