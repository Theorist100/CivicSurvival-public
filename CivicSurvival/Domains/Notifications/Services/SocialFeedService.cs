using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Colossal.Logging;
using Colossal.UI.Binding;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Events;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.UI;
using CivicSurvival.Core.UI.DomainState;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Domains.Notifications.Services
{
    // SocialMood and SocialPost moved to Core/Types/ for DIP compliance

    /// <summary>
    /// Manages social feed state for Situation Room UI.
    /// Implements ISocialFeedService.
    ///
    /// Responsibility: Store, trim, serialize social posts.
    /// This is STATE, not fire-and-forget like system alerts.
    ///
    /// Access via ServiceRegistry.Instance.Get&lt;SocialFeedService&gt;()
    ///
    /// NOTE (BUG-NE-003 by-design): No serialization implemented.
    /// Social posts are ephemeral UI notifications, not persistent game state.
    /// On load, feed starts empty — this is intentional for clean UX.
    /// Anti-spam cooldowns use a monotonic wall-shaped clock to prevent
    /// real-time abuse regardless of game time manipulation or OS clock jumps.
    /// </summary>
    [OwnedByFeatureId(FeatureIds.NotificationsName)]
    public sealed class SocialFeedService : IDisposable
    {
        private static readonly LogContext Log = new("SocialFeed");

        // State
        private readonly List<SocialPost> m_Posts = new List<SocialPost>();
        private readonly object m_PostsLock = new();  // #62 FIX: Dedicated lock (don't lock on collection)
        private IEventBus? m_EventBus;
        private volatile bool m_IsDisposed;
        private static int MaxPosts => Math.Max(0, BalanceConfig.Current.Notifications.MaxSocialPosts);

        // Anti-spam: cooldown per author (seconds)
        private readonly Dictionary<string, AuthorCooldown> m_AuthorCooldowns = new Dictionary<string, AuthorCooldown>();
        private static int AuthorCooldownSeconds => Math.Max(0, (int)BalanceConfig.Current.Notifications.CooldownSocialPost);
        private static int DuplicateWindowSeconds => Math.Max(0, (int)BalanceConfig.Current.Notifications.DuplicateWindowSocialPost);

        // PERF: Periodic cleanup of stale cooldowns to prevent memory leak
        private readonly long m_ClockBaseTimestamp = Stopwatch.GetTimestamp();
        private readonly long m_ClockBaseUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        private long m_LastCooldownCleanup;
        private const long COOLDOWN_CLEANUP_INTERVAL = 300; // 5 minutes
        private const long COOLDOWN_STALE_THRESHOLD = 600;  // 10 minutes
        private readonly List<string> m_StaleKeys = new List<string>(); // Reusable list for cleanup

        // UI Binding
        private GetterValueBinding<string> m_Binding = null!;
        private volatile bool m_IsSubscribed;

        public void Initialize()
        {
            // FIX NOTIF-004: Thread-safe initialization check
            if (m_IsSubscribed)
                return;

            lock (m_PostsLock)
            {
                // Double-check after acquiring lock
                if (m_IsSubscribed)
                    return;

                m_EventBus = ServiceRegistry.Instance.Require<IEventBus>();

                m_EventBus.Subscribe<SocialPostEvent>(OnSocialPost);
                m_EventBus.Subscribe<GameLoadedEvent>(OnGameLoaded);
                m_IsSubscribed = true;
            }
        }

        /// <summary>
        /// Add a new post to the feed (newest first).
        /// Anti-spam: cooldown per author, plus recent duplicate suppression.
        /// Thread-safe via lock on the dedicated m_PostsLock.
        /// </summary>
        public void AddPost(string author, string message, SocialMood mood)
            => TryAddPost(author, message, mood);

        /// <summary>
        /// Add a new post and report whether it was accepted into the feed.
        /// </summary>
        public bool TryAddPost(string author, string message, SocialMood mood)
        {
            if (m_IsDisposed) return false;

            // Null safety - avoid NullReferenceException in JSON serialization
            if (string.IsNullOrEmpty(author) || string.IsNullOrEmpty(message))
            {
                Log.Debug("[SOCIAL] Blocked: null/empty author or message");
                return false;
            }

            long now = GetMonotonicUnixSeconds();
            int staleCooldownsCleaned = 0;
            bool duplicateBlocked = false;
            int authorCooldownSeconds = AuthorCooldownSeconds;
            int duplicateWindowSeconds = DuplicateWindowSeconds;

            lock (m_PostsLock)
            {
                // PERF: Periodic cleanup of stale cooldowns (prevent memory leak)
                if (now - m_LastCooldownCleanup > COOLDOWN_CLEANUP_INTERVAL)
                {
                    m_LastCooldownCleanup = now;
                    m_StaleKeys.Clear();
                    foreach (var kvp in m_AuthorCooldowns)
                    {
                        if (now - kvp.Value.Timestamp > COOLDOWN_STALE_THRESHOLD)
                            m_StaleKeys.Add(kvp.Key);
                    }
                    foreach (var key in m_StaleKeys)
                    {
                        m_AuthorCooldowns.Remove(key);
                    }
                    staleCooldownsCleaned = m_StaleKeys.Count;
                }

                // Anti-spam: check cooldown for this author
                if (m_AuthorCooldowns.TryGetValue(author, out var lastPost))
                {
                    long elapsed = now - lastPost.Timestamp;
                    if (elapsed >= 0 && elapsed < lastPost.WindowSeconds)
                        return false;
                }

                // Feed is bounded; scan all posts so per-post duplicate windows remain valid
                // even if config changes after older posts were inserted.
                foreach (var existingPost in m_Posts)
                {
                    long elapsed = now - existingPost.Timestamp;
                    if (elapsed < 0 || elapsed > existingPost.DuplicateWindowSeconds)
                        continue;

                    if (existingPost.Author == author && existingPost.Message == message)
                    {
                        duplicateBlocked = true;
                        break;
                    }
                }

                if (!duplicateBlocked)
                {
                    if (MaxPosts == 0)
                        return false;

                    // FIX NOTIF-005: Use constructor for immutable struct
                    var post = new SocialPost(
                        author: author,
                        authorName: GetAuthorDisplayName(author),
                        message: message,
                        mood: mood,
                        timestamp: now,
                        duplicateWindowSeconds: duplicateWindowSeconds,
                        isOfficial: IsOfficialAuthor(author)
                    );

                    m_Posts.Insert(0, post);
                    m_AuthorCooldowns[author] = new AuthorCooldown(now, authorCooldownSeconds);

                    // Trim old posts
                    while (m_Posts.Count > MaxPosts)
                    {
                        m_Posts.RemoveAt(m_Posts.Count - 1);
                    }
                }
            }

            if (duplicateBlocked)
            {
                if (Log.IsDebugEnabled) Log.Debug($"[SOCIAL] Duplicate blocked: {author} - message already in feed");
                return false;
            }

            Log.Info($"[SOCIAL] {author}: {message}");

            if (staleCooldownsCleaned > 0 && Log.IsDebugEnabled)
                Log.Debug($"[SOCIAL] Cleaned {staleCooldownsCleaned} stale cooldowns");

            // Trigger UI update (outside lock to avoid deadlock)
            m_Binding?.TriggerUpdate();
            return true;
        }

        /// <summary>
        /// Get all posts (for iteration/queries).
        /// Returns a copy for thread safety.
        /// </summary>
        public IReadOnlyList<SocialPost> Posts
        {
            get
            {
                lock (m_PostsLock)
                {
                    return m_Posts.ToArray();
                }
            }
        }

        /// <summary>
        /// Get recent posts.
        /// </summary>
        public IReadOnlyList<SocialPost> GetRecentPosts(int count)
        {
            lock (m_PostsLock)
            {
                if (count <= 0 || m_Posts.Count == 0) return Array.Empty<SocialPost>();
                if (count >= m_Posts.Count) return m_Posts.ToArray();
                return m_Posts.GetRange(0, count).ToArray();
            }
        }

        /// <summary>
        /// Clear all posts.
        /// </summary>
        public void Clear()
        {
            lock (m_PostsLock)
            {
                m_Posts.Clear();
                m_AuthorCooldowns.Clear();
                m_LastCooldownCleanup = 0;
            }
            m_Binding?.TriggerUpdate();
        }

        /// <summary>
        /// Dispose resources.
        /// </summary>
        public void Dispose()
        {
            if (m_IsDisposed) return;
            m_IsDisposed = true;

            if (m_IsSubscribed)
            {
#pragma warning disable CIVIC139 // Intentional: m_EventBus is local field, null = nothing subscribed
                m_EventBus?.Unsubscribe<SocialPostEvent>(OnSocialPost);
                m_EventBus?.Unsubscribe<GameLoadedEvent>(OnGameLoaded);
#pragma warning restore CIVIC139
                m_EventBus = null;
                m_IsSubscribed = false;
            }

            lock (m_PostsLock)
            {
                m_Posts.Clear();
                m_AuthorCooldowns.Clear();
            }
            m_Binding = null!;

            Log.Info("[SocialFeedService] Disposed");
        }

        /// <summary>
        /// Register UI binding. Called by PowerGridUISystem.
        /// </summary>
        public void RegisterBinding(Action<IUpdateBinding> addBinding)
        {
            m_Binding = new GetterValueBinding<string>(
                "CivicSurvival",
                B.SocialFeed,
                GetFeedJson
            );
            addBinding(m_Binding);

            Log.Info("SocialFeedService: Registered socialFeed binding");
        }

        /// <summary>
        /// Get feed as JSON for UI. Thread-safe.
        /// </summary>
        public string GetFeedJson()
        {
            lock (m_PostsLock)
            {
                if (m_Posts.Count == 0)
                    return JsonBuilder.EmptyArray;

                var sb = new StringBuilder(512);
                sb.Append('[');
                for (int i = 0; i < m_Posts.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    var post = m_Posts[i];
                    var dto = new SocialPostDto
                    {
                        Author = post.Author ?? string.Empty,
                        AuthorName = post.AuthorName ?? string.Empty,
                        Message = post.Message ?? string.Empty,
                        Mood = post.Mood.ToString(),
                        Timestamp = post.Timestamp,
                        IsOfficial = post.IsOfficial,
                    };
                    dto.WriteTo(sb);
                }
                sb.Append(']');
                return sb.ToString();
            }
        }

        /// <summary>
        /// Check if author is an official source (NEWS feed) vs citizen (CHIPPER feed).
        /// Delegates to <see cref="NewsAuthorRegistry"/> — the single source of truth for
        /// handle→official classification shared with the narrative emitters.
        /// </summary>
        public static bool IsOfficialAuthor(string handle) => NewsAuthorRegistry.IsOfficial(handle);

        /// <summary>
        /// Human-readable display name for a handle. Delegates to
        /// <see cref="NewsAuthorRegistry"/> — the single source of truth for handle→display.
        /// </summary>
        public static string GetAuthorDisplayName(string handle) => NewsAuthorRegistry.GetDisplayName(handle);

        private void OnSocialPost(SocialPostEvent evt)
        {
            using var _ = PerformanceProfiler.Measure("SocialFeed.OnPost");
            AddPost(evt.Author ?? "", evt.Message ?? "", evt.Mood);
        }

        private void OnGameLoaded(GameLoadedEvent evt)
        {
            Clear();
        }

        private long GetMonotonicUnixSeconds()
        {
            double frequency = Stopwatch.Frequency;
            if (frequency <= 0.0)
                return m_ClockBaseUnixSeconds;

            double elapsedSeconds = (Stopwatch.GetTimestamp() - m_ClockBaseTimestamp) / frequency;
            if (double.IsNaN(elapsedSeconds) || double.IsInfinity(elapsedSeconds) || elapsedSeconds <= 0.0)
                return m_ClockBaseUnixSeconds;

            return m_ClockBaseUnixSeconds + (long)elapsedSeconds;
        }

        private readonly struct AuthorCooldown
        {
            public readonly long Timestamp;
            public readonly int WindowSeconds;

            public AuthorCooldown(long timestamp, int windowSeconds)
            {
                Timestamp = timestamp;
                WindowSeconds = windowSeconds;
            }
        }
    }
}
