using Game;
using System.Collections.Generic;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.Utils;
using CivicSurvival.Domains.Narrative.Systems;

namespace CivicSurvival.Domains.Narrative
{
    /// <summary>
    /// Narrative domain - story characters and event resolvers.
    /// Priority 2600 = Gameplay tier (after Threats).
    /// Note: NotificationSystem is registered in NotificationsDomain.
    /// </summary>
    public class NarrativeDomain : IFeatureModule, IContentFeatureModule, IDependentFeatureModule
    {
        public IReadOnlyList<string> Dependencies { get; } = new[] { "Notifications" };

        public void RegisterContent() => SatireRegistry.Register(new NarrativeSatireProvider());

        private static readonly LogContext Log = new("NarrativeDomain");

        private const int PRIORITY = 2600;

        public string Name => "Narrative";
        public int Priority => PRIORITY;
        public void RegisterSystems(UpdateSystem updateSystem)
        {
            Log.Info("Registering systems...");

            // Narrative notification system - wires resolvers to events
            updateSystem.RegisterAt<NarrativeNotificationSystem>(SystemUpdatePhase.GameSimulation);

            // Narrative system - story characters react to city events
            updateSystem.RegisterAt<NarrativeSystem>(SystemUpdatePhase.GameSimulation);

            Log.Info("Systems registered");
        }
    }
}
