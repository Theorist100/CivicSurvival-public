using System;
using Game;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Utils;
using CivicSurvival.Domains.Notifications.Services;
using CivicSurvival.Domains.Notifications.Systems;

namespace CivicSurvival.Domains.Notifications
{
    /// <summary>
    /// Notifications domain - notification rendering and display.
    /// Priority 2590 = Gameplay tier (before Narrative).
    /// </summary>
    public sealed class NotificationsDomain : IFeatureModule, IContentFeatureModule, IDisposable
    {
        private static readonly LogContext Log = new("NotificationsDomain");

        private const int PRIORITY = 2590;

        public string Name => "Notifications";
        public int Priority => PRIORITY;

        public void RegisterContent()
        {
            // SocialFeedService is owned by Notifications domain — register here
            // so Mod.cs doesn't instantiate domain types directly (CIVIC402).
            if (!ServiceRegistry.IsInitialized)
            {
                Log.Error("ServiceRegistry not initialized — cannot register SocialFeedService");
                return;
            }

            var socialFeed = new SocialFeedService();
            try
            {
                socialFeed.Initialize();
                ServiceRegistry.Instance.Register(socialFeed);
                socialFeed = null!;
            }
            finally
            {
                socialFeed?.Dispose();
            }
        }

        public void Dispose()
        {
            // Symmetric teardown for the SocialFeedService registered in RegisterContent.
            // FeatureRegistry.Dispose() calls feature.Dispose() for IDisposable features.
            if (!ServiceRegistry.IsInitialized) return;
            var socialFeed = ServiceRegistry.TryGet<SocialFeedService>();
            ServiceRegistry.Instance.Unregister<SocialFeedService>();
            socialFeed?.Dispose();
        }

        public void RegisterSystems(UpdateSystem updateSystem)
        {
            Log.Info("Registering systems...");

            // Notification system - dumb printer for narrative DTOs
            updateSystem.RegisterAt<NotificationSystem>(SystemUpdatePhase.GameSimulation);

            Log.Info("Systems registered");
        }
    }
}
