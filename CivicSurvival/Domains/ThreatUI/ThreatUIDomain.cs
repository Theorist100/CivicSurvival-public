using Game;
using System.Collections.Generic;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.Utils;
using CivicSurvival.Domains.ThreatUI.Systems;
using CivicSurvival.Domains.ThreatUI.Audio;
using CivicSurvival.Domains.ThreatUI.UI;

namespace CivicSurvival.Domains.ThreatUI
{
    /// <summary>
    /// ThreatUI domain — identification, audio, UI for threat display.
    /// Priority 2500 = Gameplay tier (registered separately via Mod.cs in Phase 6).
    ///
    /// Decoupled from ThreatFlight/Waves via 1-frame latency.
    /// </summary>
    public class ThreatUIDomain : IFeatureModule, IContentFeatureModule, IUiFeatureModule, IDependentFeatureModule
    {
        public IReadOnlyList<string> Dependencies { get; } = new[] { "Effects" };

        public void RegisterContent() => SatireRegistry.Register(new ThreatsSatireProvider());

        public void RegisterUI(UpdateSystem updateSystem)
        {
            updateSystem.RegisterAt<ThreatUISystem>(SystemUpdatePhase.UIUpdate);
        }

        private static readonly LogContext Log = new("ThreatUIDomain");

        private const int PRIORITY = 2503;

        public string Name => "ThreatUI";
        public int Priority => PRIORITY;

        public void RegisterSystems(UpdateSystem updateSystem)
        {
            Log.Info("Registering systems...");

            // Audio orchestrator — manages threat sounds
            updateSystem.RegisterAfter<ThreatAudioOrchestrator, global::CivicSurvival.Core.Systems.Scheduling.ThreatMovementReadyMarker>(SystemUpdatePhase.GameSimulation);

            // Threat identify — camera track → identify → focus
            updateSystem.RegisterBefore<ThreatIdentifySystem, global::CivicSurvival.Core.Systems.Scheduling.ThreatIdentifyReadyMarker>(SystemUpdatePhase.GameSimulation);

            Log.Info("Systems registered");
        }
    }
}
