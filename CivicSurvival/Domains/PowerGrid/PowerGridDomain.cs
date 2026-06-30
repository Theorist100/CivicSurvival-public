using System.Collections.Generic;
using Game;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Utils;
using CivicSurvival.Domains.PowerGrid.Systems;
using CivicSurvival.Domains.PowerGrid.UI;

namespace CivicSurvival.Domains.PowerGrid
{
    /// <summary>
    /// PowerGrid domain - electricity production, consumption, shadow trading.
    ///
    /// Registers systems for:
    /// - Power grid data (production/consumption calculations)
    /// - District power distribution
    /// - Shadow export/import (black market electricity trading)
    /// - Auto-dispatch (automatic load shedding) — reacts to PowerGridSingleton.RawBalance / GridStressData
    ///
    /// Related domains (register their own systems):
    /// - Blackout: BlackoutStateSetupSystem, BlackoutSystem
    /// - Engineering: GridStressSystem, ThresholdOperationSystem, PowerCapacityResolverSystem
    /// - PowerBackup: BackupPower* systems
    ///
    /// Priority 2105 = bumped up from 2000 to register AFTER Engineering (2100) so
    /// AutoDispatchSystem reads GridStressData / PowerGridSingleton in the same frame they are
    /// written under the Engineering pipeline (frame-ordering anchor, not a service-resolution
    /// dependency). Hard dependency declared via IDependentFeatureModule.
    /// </summary>
    public class PowerGridDomain : IFeatureModule, IDependentFeatureModule, IUiFeatureModule
    {
        private static readonly LogContext Log = new("PowerGridDomain");

        public string Name => "PowerGrid";

        private const int PRIORITY = 2105;

        public int Priority => PRIORITY;

        public IReadOnlyList<string> Dependencies => new[] { "Engineering" };

        public void RegisterUI(UpdateSystem updateSystem)
        {
            updateSystem.RegisterAt<PowerGridUISystem>(SystemUpdatePhase.UIUpdate);
            updateSystem.RegisterAt<DistrictUISystem>(SystemUpdatePhase.UIUpdate);
        }


        public void RegisterSystems(UpdateSystem updateSystem)
        {
            Log.Info("Registering systems...");

            // External power aggregation - combines all external sources
            updateSystem.RegisterBefore<ExternalPowerAggregationSystem, global::CivicSurvival.Domains.PowerGrid.Systems.PowerGridDataSystem>(SystemUpdatePhase.GameSimulation);

            // Power grid data - calculates production/consumption
            updateSystem.RegisterBefore<PowerGridDataSystem, global::CivicSurvival.Core.Systems.Scheduling.PowerDataReadyMarker>(SystemUpdatePhase.GameSimulation);

            // District power - calculates MW per district
            updateSystem.RegisterBefore<DistrictPowerSystem, global::CivicSurvival.Domains.PowerGrid.Systems.PowerGridDataSystem>(SystemUpdatePhase.GameSimulation);

            // Legacy PowerGridWiringSystem removed: ShadowTrade is owned by ShadowEconomy
            // and integrated through ECS state/request components.

            // Auto-dispatch - automatic load shedding based on grid stress
            updateSystem.RegisterAfter<AutoDispatchSystem, global::CivicSurvival.Core.Systems.Scheduling.PowerCapacityReadyMarker>(SystemUpdatePhase.GameSimulation);

            Log.Info("Systems registered");
        }
    }
}
