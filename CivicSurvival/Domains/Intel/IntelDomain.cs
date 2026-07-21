using Game;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Utils;
using CivicSurvival.Domains.Intel.Systems;
using CivicSurvival.Domains.Intel.UI;

namespace CivicSurvival.Domains.Intel
{
    /// <summary>
    /// Intel domain - tension level, price multiplier, insider purchase.
    /// Priority 2512 = after AirDefense core (2510).
    /// Optional consumers must use FeatureSlot/useOptionalBinding rather than
    /// inventing IDependentFeatureModule.Dependencies for soft enrichment.
    /// </summary>
    public class IntelDomain : IFeatureModule, IUiFeatureModule
    {
        private const int PRIORITY = 2512;

        private static readonly LogContext Log = new("IntelDomain");
        public string Name => "Intel";
        public int Priority => PRIORITY;

        public void RegisterUI(UpdateSystem updateSystem)
        {
            updateSystem.RegisterAt<IntelUISystem>(SystemUpdatePhase.UIUpdate);
        }

        public void RegisterSystems(UpdateSystem updateSystem)
        {
            Log.Info("Registering systems...");

            // Intel state - calculates and publishes intel predictions
            updateSystem.RegisterBefore<IntelStateSystem, global::CivicSurvival.Core.Systems.Scheduling.IntelStateReadyMarker>(SystemUpdatePhase.GameSimulation);

            // Intel purchase - processes purchase requests (Data-Driven Commands).
            // ModificationEnd so the buttons work while paused (AXIOM 14): payment is a
            // synchronous wallet deduct, state reads come from the managed IntelStateSystem.
            updateSystem.RegisterAt<IntelPurchaseSystem>(SystemUpdatePhase.ModificationEnd);

            Log.Info("Systems registered");
        }
    }
}
