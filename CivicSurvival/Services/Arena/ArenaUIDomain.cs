using System.Collections.Generic;
using Game;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Services.Arena
{
    /// <summary>
    /// UI feature module hosting <see cref="ArenaUISystem"/>.
    /// Depends on Arena because the panel resolves ArenaLeaderboardSystem.
    /// </summary>
    public sealed class ArenaUIDomain : IFeatureModule, IDependentFeatureModule
    {
        private static readonly LogContext Log = new("ArenaUIDomain");

        // After UIDomain (3000), after ArenaFeature (2860).
        private const int PRIORITY = 3010;

        public string Name => "ArenaUI";
        public int Priority => PRIORITY;
        public IReadOnlyList<string> Dependencies => new[] { "Arena" };

        public void RegisterSystems(UpdateSystem updateSystem)
        {
            Log.Info("Registering systems...");
            // ORDER-INVARIANT: async refresh completions must drain before ArenaUISystem serializes UI state.
            updateSystem.RegisterBefore<ArenaRefreshCompletionPumpSystem, ArenaUISystem>(SystemUpdatePhase.UIUpdate);
            updateSystem.RegisterAt<ArenaUISystem>(SystemUpdatePhase.UIUpdate);
            Log.Info("Systems registered");
        }
    }
}
