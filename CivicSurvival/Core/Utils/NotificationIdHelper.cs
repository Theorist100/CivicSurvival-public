using System;
using System.Threading;
using CivicSurvival.Core.Systems;

namespace CivicSurvival.Core.Utils
{
    /// <summary>
    /// Utility for generating unique notification IDs with game-time suffix.
    /// Prevents cooldown-based dedup from silently dropping repeat notifications.
    /// Moved from NarrativeEmitter to Core so all domains can use it.
    /// </summary>
    public static class NotificationIdHelper
    {
        private const double MillisecondsPerSecond = 1000.0;

        private static long s_BootSequence;

        /// <summary>
        /// Append game-time milliseconds plus a process sequence to notification ID for cooldown dedup uniqueness.
        /// </summary>
        public static string TimedId(string id)
        {
            long sequence = Interlocked.Increment(ref s_BootSequence);
            // GameTimeSystem may be null during boot / before OnGameLoaded —
            // callers (e.g. NotificationDispatchService) can dispatch from
            // OnCreate / SetDefaults paths. Boot suffix keeps IDs unique without
            // a real game-time stamp; the original finite-positive guard below
            // also stays so the first-tick "zero or NaN" case still degrades
            // gracefully.
            if (!GameTimeSystem.TryGetTotalGameSeconds(out var seconds))
            {
                return $"{id}_boot_{sequence}";
            }
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds <= 0.0)
            {
                return $"{id}_boot_{sequence}";
            }

            long milliseconds = (long)Math.Floor(seconds * MillisecondsPerSecond);
            return $"{id}_{milliseconds}_{sequence}";
        }

        /// <summary>
        /// Content-stable id for feed posts (News/Herald). Unlike <see cref="TimedId"/>,
        /// this id is identical whenever (sourceKey + title + body) repeats inside the same
        /// game-time bucket, so the consumer's id-dedup (NewsFeedService.m_SeenIds) actually
        /// collapses duplicates. The opposite requirement of toasts — TimedId is always
        /// unique so the cooldown never swallows a repeat alert — which is why feed posts
        /// must NOT reuse TimedId.
        /// </summary>
        /// <param name="sourceKey">Stable channel key (e.g. author handle). Part of the hash.</param>
        /// <param name="title">Post title — part of the hash.</param>
        /// <param name="body">Post body — part of the hash (empty is fine).</param>
        /// <param name="bucketSeconds">Game-time bucket width. Two posts of identical content
        /// within the same bucket share an id; a later bucket produces a fresh id so a
        /// legitimately re-issued same-type news is not swallowed forever.</param>
        public static string ContentId(string sourceKey, string title, string body, int bucketSeconds)
        {
            sourceKey ??= string.Empty;
            title ??= string.Empty;
            body ??= string.Empty;

            uint contentHash = Fnv1a(sourceKey, title, body);

            // Game-time bucket. During boot / before OnGameLoaded the clock is unavailable;
            // bucket 0 keeps the id content-stable (no sequence) so dedup still works in
            // that window — the whole point of ContentId vs TimedId.
            long bucket = 0;
            if (bucketSeconds > 0
                && GameTimeSystem.TryGetTotalGameSeconds(out var seconds)
                && !double.IsNaN(seconds) && !double.IsInfinity(seconds) && seconds > 0.0)
            {
                int safeBucketSeconds = Math.Max(1, bucketSeconds);
                bucket = (long)Math.Floor(seconds / safeBucketSeconds);
            }

            return $"news_{sourceKey}_{contentHash:x8}_{bucket}";
        }

        /// <summary>
        /// Deterministic FNV-1a over (sourceKey + ' ' + title + ' ' + body). Not
        /// string.GetHashCode() — that is randomized per process, so ids would not match
        /// across a save/load boundary and feed dedup would break exactly when it matters.
        /// </summary>
        private static uint Fnv1a(string sourceKey, string title, string body)
        {
            const uint FnvOffsetBasis = 2166136261u;
            const uint FnvPrime = 16777619u;

            uint hash = FnvOffsetBasis;
            hash = HashSegment(hash, sourceKey, FnvPrime);
            hash = HashChar(hash, ' ', FnvPrime);
            hash = HashSegment(hash, title, FnvPrime);
            hash = HashChar(hash, ' ', FnvPrime);
            hash = HashSegment(hash, body, FnvPrime);
            return hash;
        }

        private static uint HashSegment(uint hash, string segment, uint prime)
        {
            for (int i = 0; i < segment.Length; i++)
                hash = HashChar(hash, segment[i], prime);
            return hash;
        }

        private static uint HashChar(uint hash, char c, uint prime)
        {
            hash ^= c;
            hash *= prime;
            return hash;
        }
    }
}
