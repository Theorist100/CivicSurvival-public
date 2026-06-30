using System.Collections.Generic;
using Game;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Services;
using CivicSurvival.Domains.Countermeasures.Systems;
using CivicSurvival.Domains.Countermeasures.UI;

namespace CivicSurvival.Domains.Countermeasures
{
    /// <summary>
    /// Countermeasures domain - journalists, police investigations, anti-corruption.
    /// Priority 2240 = Gameplay tier (after PowerBackup).
    ///
    /// ECS-Pure refactoring:
    /// - CountermeasuresCoreFsm + CmInvestigationState + CmPoliceState + CmProtestState singletons (state)
    /// - CountermeasuresUpdateSystem (tick logic + choice processing)
    /// Systems self-wire in OnStartRunning (no wiring system needed).
    /// </summary>
    public class CountermeasuresDomain : IFeatureModule, IContentFeatureModule, IUiFeatureModule, IDependentFeatureModule
    {
        public IReadOnlyList<string> Dependencies { get; } = new[] { "Corruption" };

        public void RegisterContent() => SatireRegistry.Register(new CountermeasuresSatireProvider());

        public void RegisterUI(UpdateSystem updateSystem)
        {
            updateSystem.RegisterAt<CountermeasuresUISystem>(SystemUpdatePhase.UIUpdate);
        }

        private const int PRIORITY = 2240;

        public string Name => "Countermeasures";
        public int Priority => PRIORITY;
        public void RegisterSystems(UpdateSystem updateSystem)
        {
            Mod.Log.Info("[CountermeasuresDomain] Registering systems...");

            // Countermeasures FSM — file in Core/Systems/Domain/Countermeasures for
            // Axiom-5 isolation, owned by Countermeasures lifecycle (Phase 2).
            updateSystem.RegisterBefore<CountermeasuresUpdateSystem, global::CivicSurvival.Core.Systems.Scheduling.CountermeasuresReadyMarker>(SystemUpdatePhase.GameSimulation);

            Mod.Log.Info("[CountermeasuresDomain] Systems registered (ECS-pure)");
        }
    }
}
