using Game;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.Utils;
using CivicSurvival.Domains.Attention.Systems;
using CivicSurvival.Domains.Attention.UI;

namespace CivicSurvival.Domains.Attention
{
    /// <summary>
    /// Attention domain - world shock, exodus.
    /// Priority 2400 = Gameplay tier (after Scenario).
    /// </summary>
    public class AttentionDomain : IFeatureModule, IContentFeatureModule, IUiFeatureModule
    {
        public void RegisterContent() => SatireRegistry.Register(new AttentionSatireProvider());

        public void RegisterUI(UpdateSystem updateSystem)
        {
            updateSystem.RegisterAt<AttentionUISystem>(SystemUpdatePhase.UIUpdate);
        }

        private const int PRIORITY = 2400;

        private static readonly LogContext Log = new("AttentionDomain");

        public string Name => "Attention";
        public int Priority => PRIORITY;

        public void RegisterSystems(UpdateSystem updateSystem)
        {
            Log.Info("Registering systems...");

            // World shock - split into 3 systems for performance:
            // 1. Reaction: event-driven (0ms when no events)
            // ORDER-INVARIANT: Reaction publishes per-frame shock deltas before WorldShock reads/applies them.
            updateSystem.RegisterBefore<WorldShockReactionSystem, WorldShockSystem>(SystemUpdatePhase.GameSimulation);
            // 2. Decay: throttled (once per game hour)
            updateSystem.RegisterBefore<WorldShockDecaySystem, global::CivicSurvival.Domains.Attention.Systems.WorldShockSystem>(SystemUpdatePhase.GameSimulation);
            // 3. Coordinator: tier calculation, UI singleton, API
            updateSystem.RegisterBefore<WorldShockSystem, global::CivicSurvival.Core.Systems.Scheduling.WorldShockReadyMarker>(SystemUpdatePhase.GameSimulation);

            // Exodus - population leaves based on shock level.
            // Orders after ResidentHouseholdReadyMarker (Core marker) instead of
            // ResidentPopulationModelSystem (Population) to preserve Axiom 5 — no
            // cross-domain type reference.
            updateSystem.RegisterAfter<ExodusSystem, global::CivicSurvival.Core.Systems.Scheduling.ResidentHouseholdReadyMarker>(SystemUpdatePhase.GameSimulation);

            Log.Info("Systems registered");
        }
    }
}
