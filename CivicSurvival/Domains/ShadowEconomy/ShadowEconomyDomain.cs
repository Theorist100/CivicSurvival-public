using Game;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Domains.ShadowEconomy.Systems;
using CivicSurvival.Domains.ShadowEconomy.UI;
using CivicSurvival.Core.Features.CrossDomain.DamageAccounting;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Domains.ShadowEconomy
{
    /// <summary>
    /// Shadow economy domain - shadow wallet, offshore account management,
    /// shadow import/export power-trading UI.
    /// Priority 2151 = Gameplay tier (after Engineering 2100 and Mobilization 2150, before Corruption 2220).
    /// </summary>
    public class ShadowEconomyDomain : IFeatureModule, IUiFeatureModule
    {
        private static readonly LogContext Log = new("ShadowEconomyDomain");

        private const int PRIORITY = 2151;

        public string Name => "ShadowEconomy";
        public int Priority => PRIORITY;
        public void RegisterSystems(UpdateSystem updateSystem)
        {
            Log.Info("Registering systems...");

            // Shadow wallet - centralized shadow money management (offshore account)
            updateSystem.RegisterBefore<ShadowWalletSystem, global::CivicSurvival.Services.City.BudgetResolutionSystem>(SystemUpdatePhase.GameSimulation);

            // Recurring shadow-trade settlement — file in Core/Systems/Domain/Economy
            // for Axiom-5 isolation, but ShadowEconomy owns its lifecycle (Phase 2).
            updateSystem.RegisterBefore<ShadowTradeDailySystem, global::CivicSurvival.Core.Systems.Scheduling.ShadowTradeReadyMarker>(SystemUpdatePhase.GameSimulation);

            Log.Info("Systems registered");
        }

        public void RegisterUI(UpdateSystem updateSystem)
        {
            updateSystem.RegisterAt<ShadowExportUISystem>(SystemUpdatePhase.UIUpdate);
            updateSystem.RegisterAt<ShadowImportUISystem>(SystemUpdatePhase.UIUpdate);
        }
    }
}
