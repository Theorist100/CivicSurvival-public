using Game;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Services;
using CivicSurvival.Domains.Corruption.Systems;
using CivicSurvival.Core.Features.CrossDomain.DamageAccounting;
using CivicSurvival.Core.Systems.Requests;
using CivicSurvival.Core.Utils;
using CivicSurvival.Domains.Corruption.UI;

namespace CivicSurvival.Domains.Corruption
{
    /// <summary>
    /// Corruption domain - corruption schemes, shadow reputation, district modernization.
    /// Priority 2220 = Gameplay tier (after ShadowEconomy).
    /// </summary>
    public class CorruptionDomain : IFeatureModule, IContentFeatureModule, IUiFeatureModule
    {
        public void RegisterContent() => SatireRegistry.Register(new CorruptionSatireProvider());

        public void RegisterUI(UpdateSystem updateSystem)
        {
            updateSystem.RegisterAt<MaintenanceContractUISystem>(SystemUpdatePhase.UIUpdate);
            updateSystem.RegisterAt<CorruptionSchemesUISystem>(SystemUpdatePhase.UIUpdate);
        }

        private const int PRIORITY = 2220;

        private static readonly LogContext Log = new("CorruptionDomain");

        public string Name => "Corruption";
        public int Priority => PRIORITY;
        public void RegisterSystems(UpdateSystem updateSystem)
        {
            Log.Info("Registering systems...");

            // Emergency fund - corruption scheme
            updateSystem.RegisterAt<EmergencyFundSystem>(SystemUpdatePhase.GameSimulation);

            // Fuel siphoning - corruption scheme
            updateSystem.RegisterAfter<FuelSiphoningSystem, global::CivicSurvival.Core.Systems.Scheduling.CorruptionSchemesReadyMarker>(SystemUpdatePhase.GameSimulation);

            // Fuel siphoning efficiency writer - applies siphoning modifier to generator efficiency
            updateSystem.RegisterAfter<FuelSiphoningEfficiencyWriterSystem, global::CivicSurvival.Core.Systems.Scheduling.GeneratorEfficiencyClearReadyMarker>(SystemUpdatePhase.GameSimulation);

            // VIP protection racket - corruption scheme
            updateSystem.RegisterAfter<VIPProtectionRacketSystem, global::CivicSurvival.Core.Systems.Scheduling.WorldShockReadyMarker>(SystemUpdatePhase.GameSimulation);

            // Shadow reputation - tracks player's corruption level
            updateSystem.RegisterAfter<ShadowReputationSystem, global::CivicSurvival.Core.Systems.GameTimeSystem>(SystemUpdatePhase.GameSimulation);

            // Maintenance contracts - corruption scheme. Contract responses must drain before
            // the daily publisher snapshots ContractStatsSingleton, so same-frame accepted
            // contracts are visible to CorruptionStateUpdateSystem.
            updateSystem.RegisterBefore<ContractResponseSystem, global::CivicSurvival.Domains.Corruption.Systems.MaintenanceContractSystem>(SystemUpdatePhase.GameSimulation);
            updateSystem.RegisterBefore<MaintenanceContractSystem, global::CivicSurvival.Core.Systems.Scheduling.MaintenanceContractReadyMarker>(SystemUpdatePhase.GameSimulation);

            // District modernization - backup power with honest/corrupt choice
            updateSystem.RegisterAfter<DistrictModernizationSystem, global::CivicSurvival.Core.Systems.Scheduling.ShadowTradeReadyMarker>(SystemUpdatePhase.GameSimulation);

            // Counterfeit battery fires - direct consequence of corrupt modernization.
            // Publishes ICounterfeitFireDedupReader so PowerBackup can skip double-firing.
            updateSystem.RegisterAt<CounterfeitBatteryFireSystem>(SystemUpdatePhase.GameSimulation);

            // Cross-cutting Corruption logic — physical files in Core/Systems for Axiom-5
            // isolation, but lifecycle ownership is here per Phase 2 of the FeatureModule
            // architecture (file moves are deferred to Phase 9).
            updateSystem.RegisterBefore<CorruptionStateUpdateSystem, global::CivicSurvival.Core.Systems.Scheduling.CorruptionStateReadyMarker>(SystemUpdatePhase.GameSimulation);
            updateSystem.RegisterBefore<CorruptionSchemeRequestSystem, global::CivicSurvival.Core.Systems.Scheduling.CorruptionSchemesReadyMarker>(SystemUpdatePhase.GameSimulation);

            Log.Info("Systems registered");
        }
    }
}
