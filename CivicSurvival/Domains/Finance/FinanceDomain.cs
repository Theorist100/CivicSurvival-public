using Game;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.Utils;
using CivicSurvival.Domains.Finance.Systems;
using CivicSurvival.Domains.Finance.UI;

namespace CivicSurvival.Domains.Finance
{
    /// <summary>
    /// Finance domain - war damage costs, debt management.
    /// Priority 2210 = Gameplay tier (after Economy).
    /// </summary>
    public class FinanceDomain : IFeatureModule, IUiFeatureModule
    {
        private static readonly LogContext Log = new("FinanceDomain");

        private const int PRIORITY = 2210;

        public string Name => "Finance";
        public int Priority => PRIORITY;

        public void RegisterUI(UpdateSystem updateSystem)
        {
            updateSystem.RegisterAt<FinanceUISystem>(SystemUpdatePhase.UIUpdate);
        }

        public void RegisterSystems(UpdateSystem updateSystem)
        {
            Log.Info("Registering systems...");

            // War damage debt - deducts repair costs after waves
            updateSystem.RegisterAt<WarDamageDebtSystem>(SystemUpdatePhase.GameSimulation);

            // City debt tracking — daily war-damage debt accumulation. File in
            // Services/City for historical reasons, owned by Finance lifecycle (Phase 2).
            updateSystem.RegisterAt<CityDebtTrackingSystem>(SystemUpdatePhase.GameSimulation);

            Log.Info("Systems registered");
        }
    }
}
