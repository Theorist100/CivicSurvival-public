using System.Collections.Generic;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Core.Infrastructure
{
    /// <summary>
    /// Enumerates all pressure channels in HouseholdPsyState.
    /// Each channel must have at least one producer (writes the value)
    /// and one consumer (reads the value for gameplay effect).
    /// </summary>
    public enum PressureChannel
    {
        None = 0,
        Blackout,
        Envy,
        Impact
    }

    /// <summary>
    /// Tracks producer→consumer wiring for pressure channels.
    /// Validates on first frame that every channel has both a producer and a consumer.
    /// Catches orphaned pipelines (entire mechanics silently dead) at startup.
    ///
    /// Pattern: same as RegistrationValidator — static registry, fail-fast validation.
    /// Registration: systems call RegisterProducer/RegisterConsumer in OnCreate.
    /// Validation: MentalHealthResolverSystem calls Validate() on first fire frame.
    /// Reset: Mod.OnDispose() clears stale state between sessions.
    /// </summary>
    internal static class PressureRegistry
    {
        private static readonly LogContext Log = new("PressureRegistry");
        private static readonly Dictionary<PressureChannel, HashSet<string>> s_Producers = new();
        private static readonly Dictionary<PressureChannel, HashSet<string>> s_Consumers = new();
        // Blackout is produced inline by BlackoutCalculator inside ResolveHouseholdPsyJob,
        // not by a registrable pressure producer pipeline.
        private static readonly PressureChannel[] s_PipelineChannels = { PressureChannel.Envy, PressureChannel.Impact };
        private static readonly object s_Lock = new();
        [CivicSurvival.Core.Attributes.RegistryGenerationCursor("Static registration generation for pressure wiring validation; no snapshot payload is published.")]
        private static int s_Generation;
        [CivicSurvival.Core.Attributes.RegistryGenerationCursor("Last validated pressure registry generation; suppresses duplicate validation logs without publishing a snapshot.")]
        private static int s_ValidatedGeneration = -1;

        internal static void RegisterProducer(PressureChannel channel, string systemName)
        {
            lock (s_Lock)
            {
                if (!s_Producers.TryGetValue(channel, out var set))
                {
                    set = new HashSet<string>();
                    s_Producers[channel] = set;
                }
                if (set.Add(systemName))
                    s_Generation++;
            }
        }

        internal static void DeregisterProducer(PressureChannel channel, string systemName)
        {
            lock (s_Lock)
            {
                if (s_Producers.TryGetValue(channel, out var set) && set.Remove(systemName))
                    s_Generation++;
            }
        }

        // FIX M19: Symmetric deregister for consumers (was missing — only DeregisterProducer existed)
        internal static void DeregisterConsumer(PressureChannel channel, string systemName)
        {
            lock (s_Lock)
            {
                if (s_Consumers.TryGetValue(channel, out var set) && set.Remove(systemName))
                    s_Generation++;
            }
        }

        internal static void RegisterConsumer(PressureChannel channel, string systemName)
        {
            lock (s_Lock)
            {
                if (!s_Consumers.TryGetValue(channel, out var set))
                {
                    set = new HashSet<string>();
                    s_Consumers[channel] = set;
                }
                if (set.Add(systemName))
                    s_Generation++;
            }
        }

        /// <summary>
        /// Validates all pressure channels have both producer(s) and consumer(s).
        /// Self-guarding: re-runs whenever registrations or feature state change.
        /// </summary>
        internal static void Validate()
        {
            List<(int Level, string Message)> logLines;
            int issueCount = 0;
            lock (s_Lock)
            {
                if (s_ValidatedGeneration == s_Generation) return;
                s_ValidatedGeneration = s_Generation;

                logLines = new List<(int, string)>();
                foreach (var channel in s_PipelineChannels)
                {
                    bool hasProducer = s_Producers.TryGetValue(channel, out var producers) && producers.Count > 0;
                    bool hasConsumer = s_Consumers.TryGetValue(channel, out var consumers) && consumers.Count > 0;

                    if (hasProducer && !hasConsumer)
                    {
                        logLines.Add((2, $"ORPHANED PIPELINE: {channel} has producer(s) [{string.Join(", ", producers)}] but ZERO consumers"));
                        issueCount++;
                    }
                    else if (!hasProducer && hasConsumer)
                    {
                        logLines.Add((1, $"DANGLING CONSUMER: {channel} has consumer(s) [{string.Join(", ", consumers)}] but ZERO producers"));
                        issueCount++;
                    }
                    else if (!hasProducer && !hasConsumer)
                    {
                        logLines.Add((1, $"UNUSED CHANNEL: {channel} has no producers and no consumers"));
                        issueCount++;
                    }
                    else if (Log.IsDebugEnabled)
                    {
                        logLines.Add((0, $"{channel}: [{string.Join(", ", producers)}] → [{string.Join(", ", consumers)}]"));
                    }
                }
            }

            foreach (var (level, message) in logLines)
            {
                if (level == 2) Log.Error(message);
                else if (level == 1) Log.Warn(message);
                else if (Log.IsDebugEnabled) Log.Debug(message);
            }

            if (issueCount == 0)
                Log.Info($"All {s_PipelineChannels.Length} pressure pipeline channels wired correctly");
            else
                Log.Warn($"{issueCount}/{s_PipelineChannels.Length} pressure pipeline channel(s) have wiring issues");
        }

        internal static void InvalidateValidation()
        {
            lock (s_Lock)
            {
                s_Generation++;
                s_ValidatedGeneration = -1;
            }
        }

        /// <summary>
        /// Clear all registrations and reset validation flag.
        /// Called from Mod.OnDispose() to prevent stale state across game sessions.
        /// </summary>
        internal static void Reset()
        {
            lock (s_Lock)
            {
                s_Producers.Clear();
                s_Consumers.Clear();
                s_Generation = 0;
                s_ValidatedGeneration = -1;
            }
        }
    }
}
