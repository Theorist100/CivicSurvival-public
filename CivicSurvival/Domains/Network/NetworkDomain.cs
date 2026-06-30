using System;
using Game;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Utils;
using CivicSurvival.Domains.Network.Services;
using CivicSurvival.Domains.Network.Systems;
using CivicSurvival.Domains.Network.UI;

namespace CivicSurvival.Domains.Network
{
    /// <summary>
    /// Network domain - global news, online stats, server communication.
    /// Priority 2850 = Gameplay tier (after GridWarfare).
    /// </summary>
    public sealed class NetworkDomain : IFeatureModule, IUiFeatureModule, IContentFeatureModule, IDisposable
    {
        private static readonly LogContext Log = new("NetworkDomain");

        private const int PRIORITY = 2850;

        public string Name => "Network";
        public int Priority => PRIORITY;
        public void RegisterContent()
        {
            if (!ServiceRegistry.IsInitialized)
            {
                Log.Error("ServiceRegistry not initialized — cannot register NewsFeedService");
                return;
            }

            var newsFeed = new NewsFeedService();
            try
            {
                newsFeed.Initialize();
                ServiceRegistry.Instance.Register(newsFeed);
                newsFeed = null!;
            }
            finally
            {
                newsFeed?.Dispose();
            }
        }

        public void Dispose()
        {
            if (!ServiceRegistry.IsInitialized) return;
            var newsFeed = ServiceRegistry.TryGet<NewsFeedService>();
            ServiceRegistry.Instance.Unregister<NewsFeedService>();
            newsFeed?.Dispose();
        }

        public void RegisterUI(UpdateSystem updateSystem)
        {
            // ORDER-INVARIANT: async completions must drain before GlobalNewsUISystem serializes UI state.
            updateSystem.RegisterBefore<NewsCompletionPumpSystem, GlobalNewsUISystem>(SystemUpdatePhase.UIUpdate);
            updateSystem.RegisterAt<GlobalNewsUISystem>(SystemUpdatePhase.UIUpdate);
        }

        public void RegisterSystems(UpdateSystem updateSystem)
        {
            Log.Info("Registering systems...");

            // Global news polling - fetches news from server, publishes to official news feed
            updateSystem.RegisterAfter<GlobalNewsSystem>(SystemUpdatePhase.GameSimulation);

            // Personal Chronicle polling (Mode A) - per-player digest, same feed, different Scope.
            // Polls in GameSimulation like GlobalNews; delivery is pumped in UIUpdate (pause-safe).
            updateSystem.RegisterAfter<PersonalChronicleSystem>(SystemUpdatePhase.GameSimulation);

            Log.Info("Systems registered");
        }
    }
}
