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

            // Draft exemptions - corruption scheme (deferral rate + disability buy-out toasts)
            updateSystem.RegisterAfter<DraftExemptionSystem, global::CivicSurvival.Core.Systems.Scheduling.CorruptionSchemesReadyMarker>(SystemUpdatePhase.GameSimulation);

            // Fuel siphoning efficiency writer - applies siphoning modifier to generator efficiency
            updateSystem.RegisterAfter<FuelSiphoningEfficiencyWriterSystem, global::CivicSurvival.Core.Systems.Scheduling.GeneratorEfficiencyClearReadyMarker>(SystemUpdatePhase.GameSimulation);

            // VIP protection racket - corruption scheme
            updateSystem.RegisterAfter<VIPProtectionRacketSystem, global::CivicSurvival.Core.Systems.Scheduling.WorldShockReadyMarker>(SystemUpdatePhase.GameSimulation);

            // Shadow reputation - tracks player's corruption level
            updateSystem.RegisterAfter<ShadowReputationSystem, global::CivicSurvival.Core.Systems.GameTimeSystem>(SystemUpdatePhase.GameSimulation);

            // Maintenance contracts - corruption scheme. Contract responses run on the
            // pause-safe ModificationEnd route (Axiom 14): UI creates a ContractResponse
            // via EndFrameBarrier, this consumer validates, pays synchronously through
            // BudgetTransactionResolver and creates the contract in the same tick — so
            // the decision modal works at selectedSpeed==0. MaintenanceContractSystem
            // (GameSimulation) sees the new ContractData one frame later, which is fine
            // for its daily ContractStatsSingleton snapshot.
            updateSystem.RegisterAt<ContractResponseSystem>(SystemUpdatePhase.ModificationEnd);
            updateSystem.RegisterBefore<MaintenanceContractSystem, global::CivicSurvival.Core.Systems.Scheduling.MaintenanceContractReadyMarker>(SystemUpdatePhase.GameSimulation);

            // District modernization - backup power with honest/corrupt choice
            updateSystem.RegisterAfter<DistrictModernizationSystem, global::CivicSurvival.Core.Systems.Scheduling.ShadowTradeReadyMarker>(SystemUpdatePhase.GameSimulation);

            // Counterfeit battery fires - direct consequence of corrupt modernization.
            // Publishes ICounterfeitFireDedupReader so PowerBackup can skip double-firing.
            updateSystem.RegisterAt<CounterfeitBatteryFireSystem>(SystemUpdatePhase.GameSimulation);

            // Cross-cutting Corruption logic — physical files in Core/Systems for Axiom-5
            // isolation, but lifecycle ownership is here per the FeatureModule
            // architecture (file moves are deferred).
            updateSystem.RegisterBefore<CorruptionStateUpdateSystem, global::CivicSurvival.Core.Systems.Scheduling.CorruptionStateReadyMarker>(SystemUpdatePhase.GameSimulation);

            // Corruption scheme sliders run on the pause-safe ModificationEnd route
            // (Axiom 14): the three percent settings are config writes, so applying
            // them at selectedSpeed==0 is safe — the daily consumers (EmergencyFund/
            // FuelSiphoning/DraftExemption, GameSimulation) read the new values on
            // their next tick.
            updateSystem.RegisterAt<CorruptionSchemeRequestSystem>(SystemUpdatePhase.ModificationEnd);

            // Construction kickback - corruption scheme. Skims a share of road/net
            // construction spend into the shadow wallet. Runs in ModificationEnd
            // (Axiom 14): vanilla stamps Recent on built edges in ApplyTool (which
            // ticks while paused), and the transient Created/Updated tags survive
            // until Cleanup — so skim accrues even when the player builds at
            // selectedSpeed==0. The daily payout itself is event-driven (DayChanged).
            updateSystem.RegisterAt<ConstructionKickbackSystem>(SystemUpdatePhase.ModificationEnd);

            Log.Info("Systems registered");
        }
    }
}
