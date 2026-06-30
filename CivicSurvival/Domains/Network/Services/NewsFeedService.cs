using System;
using System.Collections.Generic;
using System.Text;
using Colossal.Logging;
using Colossal.UI.Binding;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Events;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.UI;
using CivicSurvival.Core.UI.DomainState;
using CivicSurvival.Core.Utils;
using CivicSurvival.Domains.Network.Data;
using CivicSurvival.Domains.Network.Events;

namespace CivicSurvival.Domains.Network.Services
{
    /// <summary>
    /// Owns official NEWS feed state for Herald. This is intentionally separate
    /// from SocialFeedService, which owns citizen/social satire posts.
    /// </summary>
    [OwnedByFeatureId(FeatureIds.NetworkName)]
    public sealed class NewsFeedService : IDisposable
    {
        private static readonly LogContext Log = new("NewsFeed");
        private const int MAX_NEWS_POSTS = 50;

        private readonly List<NewsFeedPost> m_Posts = new();
        private readonly HashSet<string> m_SeenIds = new(StringComparer.Ordinal);
        private readonly object m_Lock = new();

        private IEventBus? m_EventBus;
        private GetterValueBinding<string> m_Binding = null!;
        private volatile bool m_IsDisposed;
        private volatile bool m_IsSubscribed;

        public void Initialize()
        {
            if (m_IsSubscribed)
                return;

            lock (m_Lock)
            {
                if (m_IsSubscribed)
                    return;

                m_EventBus = ServiceRegistry.Instance.Require<IEventBus>();
                m_EventBus.Subscribe<OfficialNewsReceivedEvent>(OnOfficialNewsReceived);
                m_EventBus.Subscribe<NewsPostEvent>(OnNewsPost);
                m_EventBus.Subscribe<GameLoadedEvent>(OnGameLoaded);
                m_IsSubscribed = true;
            }
        }

        public bool TryAddPost(NewsFeedPost post)
        {
            if (m_IsDisposed)
                return false;
            if (string.IsNullOrEmpty(post.Id) || string.IsNullOrEmpty(post.Title))
                return false;

            lock (m_Lock)
            {
                if (!m_SeenIds.Add(post.Id))
                    return false;

                m_Posts.Insert(0, post);
                while (m_Posts.Count > MAX_NEWS_POSTS)
                {
                    var removed = m_Posts[m_Posts.Count - 1];
                    m_Posts.RemoveAt(m_Posts.Count - 1);
                    if (!string.IsNullOrEmpty(removed.Id))
                        m_SeenIds.Remove(removed.Id);
                }
            }

            if (Log.IsDebugEnabled)
                Log.Debug($"[NEWS] {post.Source}: {post.Title}");

            m_Binding?.TriggerUpdate();
            return true;
        }

        /// <summary>
        /// Drop only save-scoped posts (Scope == "global"), keeping durable
        /// personal/reactive posts (keyed on the player_id, not the save) across
        /// an in-game load. Their ids stay in <see cref="m_SeenIds"/> so a
        /// post-load re-pull can't re-add them as duplicates.
        /// </summary>
        public void ClearGlobalScope()
        {
            lock (m_Lock)
            {
                for (int i = m_Posts.Count - 1; i >= 0; i--)
                {
                    var post = m_Posts[i];
                    // Default/empty scope == "global" (see NewsFeedPost ctor).
                    bool isGlobal = string.IsNullOrEmpty(post.Scope) || post.Scope == "global";
                    if (!isGlobal)
                        continue;

                    m_Posts.RemoveAt(i);
                    if (!string.IsNullOrEmpty(post.Id))
                        m_SeenIds.Remove(post.Id);
                }
            }
            m_Binding?.TriggerUpdate();
        }

        public void RegisterBinding(Action<IUpdateBinding> addBinding)
        {
            m_Binding = new GetterValueBinding<string>(
                B.Group,
                B.NewsFeed,
                GetFeedJson);
            addBinding(m_Binding);

            Log.Info("NewsFeedService: Registered newsFeed binding");
        }

        public string GetFeedJson()
        {
            lock (m_Lock)
            {
                if (m_Posts.Count == 0)
                    return JsonBuilder.EmptyArray;

                var sb = new StringBuilder(512);
                sb.Append('[');
                for (int i = 0; i < m_Posts.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    var post = m_Posts[i];
                    var dto = new NewsPostDto
                    {
                        PostId = post.Id ?? string.Empty,
                        Source = post.Source ?? string.Empty,
                        Title = post.Title ?? string.Empty,
                        Body = post.Body ?? string.Empty,
                        Mood = post.Mood.ToString(),
                        Timestamp = post.Timestamp,
                        Category = post.Category ?? string.Empty,
                        Scope = post.Scope ?? "global",
                        IsAiGenerated = post.IsAiGenerated,
                    };
                    dto.WriteTo(sb);
                }
                sb.Append(']');
                return sb.ToString();
            }
        }

        public void Dispose()
        {
            if (m_IsDisposed)
                return;
            m_IsDisposed = true;

            if (m_IsSubscribed)
            {
#pragma warning disable CIVIC139 // Intentional: m_EventBus is local field, null = nothing subscribed
                m_EventBus?.Unsubscribe<OfficialNewsReceivedEvent>(OnOfficialNewsReceived);
                m_EventBus?.Unsubscribe<NewsPostEvent>(OnNewsPost);
                m_EventBus?.Unsubscribe<GameLoadedEvent>(OnGameLoaded);
#pragma warning restore CIVIC139
                m_EventBus = null;
                m_IsSubscribed = false;
            }

            lock (m_Lock)
            {
                m_Posts.Clear();
                m_SeenIds.Clear();
            }
            m_Binding = null!;

            Log.Info("[NewsFeedService] Disposed");
        }

        private void OnOfficialNewsReceived(OfficialNewsReceivedEvent evt)
        {
            if (!TryAddPost(evt.Post) && Log.IsDebugEnabled)
                Log.Debug($"[NEWS] Official news rejected: {evt.Post.Id}");
        }

        private void OnNewsPost(NewsPostEvent evt)
        {
            var post = new NewsFeedPost(
                evt.Id,
                evt.Source,
                evt.Title,
                evt.Body,
                evt.Mood,
                evt.Timestamp,
                evt.Category);

            if (!TryAddPost(post) && Log.IsDebugEnabled)
                Log.Debug($"[NEWS] News post rejected: {post.Id}");
        }

        private void OnGameLoaded(GameLoadedEvent evt)
        {
            // Scope-aware: global (server-wide, save-scoped) posts are re-pulled
            // last-N by GlobalNewsSystem after its cursor resets, so they must be
            // dropped here. Personal/reactive posts are durable (player_id-keyed)
            // and their since-cursor persists across load — clearing them would
            // leave the feed empty until the next digest (≤30 min), so they stay.
            ClearGlobalScope();
        }
    }
}
