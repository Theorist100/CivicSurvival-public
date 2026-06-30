using Game;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Services.Arena
{
    /// <summary>
    /// Server-backed competitive leaderboard feature (D7).
    /// Decoupled from GridWarfare — depends on Network because the leaderboard
    /// service is server-backed. Closes if Network closes.
    /// </summary>
    public sealed class ArenaFeature : IFeatureModule, IGatedFeatureModule
    {
        private static readonly LogContext Log = new("ArenaFeature");

        private const int PRIORITY = 2860;

        public string Name => "Arena";
        public int Priority => PRIORITY;

        public FeatureGate Gate { get; } = new FeatureGate.RequiresFeature("Network");

        public void RegisterSystems(UpdateSystem updateSystem)
        {
            Log.Info("Registering systems...");

            updateSystem.RegisterAfter<ArenaLeaderboardSystem>(SystemUpdatePhase.GameSimulation);

            Log.Info("Systems registered");
        }
    }
}
