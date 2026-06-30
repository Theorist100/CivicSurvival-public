using System;
using System.Collections.Generic;

namespace CivicSurvival.Core.Utils
{
    /// <summary>
    /// Lightweight diagnostic tracker for memory, state, and error monitoring.
    /// Usage:
    ///
    /// // Memory tracking (call periodically)
    /// DiagnosticTracker.TrackMemory("EventBus.Subscribers", m_Subscribers.Count);
    /// DiagnosticTracker.TrackMemory("ThreatQueue", m_Queue.Length);
    ///
    /// // State tracking (call on change or periodically)
    /// DiagnosticTracker.TrackState("GridStatus", status.ToString());
    /// DiagnosticTracker.TrackState("ActiveThreats", count.ToString());
    ///
    /// // Error counting (call on error)
    /// DiagnosticTracker.IncrementError("NullService");
    /// DiagnosticTracker.IncrementError("FailedAttack");
    ///
    /// Reports are logged every REPORT_INTERVAL by DiagnosticReportSystem.
    /// </summary>
    public static class DiagnosticTracker
    {
        private static readonly object s_Lock = new();

        // Memory: name -> (current, peak)
        private static readonly Dictionary<string, (int Current, int Peak)> s_Memory = new();

        // State: name -> value
        private static readonly Dictionary<string, string> s_State = new();

        // Errors: name -> count (since last report)
        private static readonly Dictionary<string, int> s_Errors = new();

        // Errors: name -> total count (lifetime)
        private static readonly Dictionary<string, int> s_ErrorsTotal = new();

        /// <summary>
        /// Track a collection/array size for memory monitoring.
        /// Call periodically (e.g., every frame or every N seconds).
        /// </summary>
        public static void TrackMemory(string name, int count)
        {
            lock (s_Lock)
            {
                if (s_Memory.TryGetValue(name, out var existing))
                {
                    int peak = Math.Max(existing.Peak, count);
                    s_Memory[name] = (count, peak);
                }
                else
                {
                    s_Memory[name] = (count, count);
                }
            }
        }

        /// <summary>
        /// Track a state value (string representation).
        /// Call on state change or periodically.
        /// </summary>
        public static void TrackState(string name, string value)
        {
            lock (s_Lock)
            {
                s_State[name] = value ?? "null";
            }
        }

        /// <summary>
        /// Track a numeric state value.
        /// </summary>
        public static void TrackState(string name, int value)
        {
            TrackState(name, value.ToString());
        }

        /// <summary>
        /// Track a float state value.
        /// </summary>
        public static void TrackState(string name, float value)
        {
            TrackState(name, value.ToString("F1", System.Globalization.CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// Increment an error counter. Use for silent failures, retries, etc.
        /// </summary>
        public static void IncrementError(string name)
        {
            lock (s_Lock)
            {
                s_Errors.TryGetValue(name, out int count);
                s_Errors[name] = count + 1;

                s_ErrorsTotal.TryGetValue(name, out int total);
                s_ErrorsTotal[name] = total + 1;
            }
        }

        /// <summary>
        /// Get snapshot of all metrics and reset period counters.
        /// Called by DiagnosticReportSystem.
        /// </summary>
        public static DiagnosticSnapshot GetSnapshotAndReset()
        {
            lock (s_Lock)
            {
                var snapshot = new DiagnosticSnapshot
                {
                    Memory = new Dictionary<string, (int Current, int Peak)>(s_Memory),
                    State = new Dictionary<string, string>(s_State),
                    ErrorsPeriod = new Dictionary<string, int>(s_Errors),
                    ErrorsTotal = new Dictionary<string, int>(s_ErrorsTotal)
                };

                // Reset peak tracking for next period
                var keys = new List<string>(s_Memory.Keys);
                foreach (var key in keys)
                {
                    var (current, _) = s_Memory[key];
                    s_Memory[key] = (current, current);
                }

                // Reset period error counters (keep totals)
                s_Errors.Clear();

                return snapshot;
            }
        }

        /// <summary>
        /// Clear all tracked data. Call on game reset/load.
        /// </summary>
        public static void Reset()
        {
            lock (s_Lock)
            {
                s_Memory.Clear();
                s_State.Clear();
                s_Errors.Clear();
                s_ErrorsTotal.Clear();
            }
        }
    }

    /// <summary>
    /// Immutable snapshot of diagnostic data at a point in time.
    /// </summary>
    public sealed class DiagnosticSnapshot : IEquatable<DiagnosticSnapshot>
    {
        public Dictionary<string, (int Current, int Peak)> Memory { get; init; } = null!;
        public Dictionary<string, string> State { get; init; } = null!;
        public Dictionary<string, int> ErrorsPeriod { get; init; } = null!;
        public Dictionary<string, int> ErrorsTotal { get; init; } = null!;

        public bool HasData =>
            Memory.Count > 0 || State.Count > 0 || ErrorsPeriod.Count > 0;

        public bool Equals(DiagnosticSnapshot? other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;
            return DictionaryEquals(Memory, other.Memory)
                && DictionaryEquals(State, other.State)
                && DictionaryEquals(ErrorsPeriod, other.ErrorsPeriod)
                && DictionaryEquals(ErrorsTotal, other.ErrorsTotal);
        }

        public override bool Equals(object? obj)
            => obj is DiagnosticSnapshot other && Equals(other);

        public override int GetHashCode()
            => HashCode.Combine(
                Memory is null ? 0 : Memory.Count,
                State is null ? 0 : State.Count,
                ErrorsPeriod is null ? 0 : ErrorsPeriod.Count,
                ErrorsTotal is null ? 0 : ErrorsTotal.Count);

        private static bool DictionaryEquals<TKey, TValue>(
            Dictionary<TKey, TValue>? left,
            Dictionary<TKey, TValue>? right)
            where TKey : notnull
        {
            if (ReferenceEquals(left, right)) return true;
            if (left is null || right is null) return false;
            if (left.Count != right.Count) return false;
            var comparer = EqualityComparer<TValue>.Default;
            foreach (var pair in left)
            {
                if (!right.TryGetValue(pair.Key, out var otherValue))
                    return false;
                if (!comparer.Equals(pair.Value, otherValue))
                    return false;
            }
            return true;
        }
    }
}
