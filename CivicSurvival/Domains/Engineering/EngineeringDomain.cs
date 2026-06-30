using Game;
using Game.Simulation;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.Systems.Scheduling;
using CivicSurvival.Core.Utils;
using CivicSurvival.Domains.Engineering.Systems;

namespace CivicSurvival.Domains.Engineering
{
    /// <summary>
    /// Engineering domain - grid stress, threshold operation, equipment wear, disasters.
    /// Priority 2100 = Gameplay tier. Engineering registers BEFORE PowerGrid (2105 since
    /// 2026-05-20) because PowerGrid's AutoDispatchSystem consumes Engineering's
    /// IPowerCapacitySnapshotReader at OnCreate. Engineering also registers before
    /// Mobilization (2150) and ShadowEconomy (2151) within the 2100-2199 band.
    /// </summary>
    public class EngineeringDomain : IFeatureModule, IContentFeatureModule
    {
        public void RegisterContent() => SatireRegistry.Register(new EngineeringSatireProvider());

        private static readonly LogContext Log = new("EngineeringDomain");

        private const int PRIORITY = 2100;

        public string Name => "Engineering";
        public int Priority => PRIORITY;

        public void RegisterSystems(UpdateSystem updateSystem)
        {
            Log.Info("Registering systems...");

            // Power capacity index + resolver chain: hard-ordered through vanilla ref-map anchors.
            updateSystem.RegisterAfter<PowerCapacityIndexSystem, PowerPlantAISystem>(SystemUpdatePhase.GameSimulation);
            updateSystem.RegisterAfter<ConstructionDelaySystem, PowerCapacityIndexSystem>(SystemUpdatePhase.GameSimulation);

            // Power-index producer/consumer split (mirror of vanilla FireSimulationSystem ->
            // IgniteSystem and the mod's ModFireApplySystem): PowerCapacityIndexSystem
            // (GameSimulation) emits a PowerIndexIntent for a new grid plant; this consumer
            // performs the first archetype-migrating add of the index/modifier components in
            // ModificationEnd, in phase with the vanilla render batch pipeline. RegisterAt:
            // ModificationEndBarrier playback already runs before RequiredBatchesSystem
            // within the phase.
            updateSystem.RegisterAt<PowerIndexApplySystem>(SystemUpdatePhase.ModificationEnd);

            // Grid stress - tracks frequency and stress during deficit after construction state is current.
            updateSystem.RegisterBefore<GridStressSystem, global::CivicSurvival.Domains.Engineering.Systems.PowerPlantDisasterSystem>(SystemUpdatePhase.GameSimulation);

            // Threshold operation - load shedding to prevent collapse
            updateSystem.RegisterAfter<ThresholdOperationSystem, global::CivicSurvival.Core.Systems.Scheduling.BlackoutReadyMarker>(SystemUpdatePhase.GameSimulation);

            // Equipment wear - assign tracking to new producers
            updateSystem.RegisterBefore<EquipmentWearAssignSystem, global::CivicSurvival.Domains.Engineering.Systems.PlantWearSimulation>(SystemUpdatePhase.GameSimulation);

            // Equipment wear - power plant aging and maintenance.
            // Orders after OperationalDamageReadyMarker (Core marker) instead of
            // OperationalDamageSystem (ThreatDamage) to preserve Axiom 5 — no
            // cross-domain type reference.
            updateSystem.RegisterAfter<PlantWearSimulation, global::CivicSurvival.Core.Systems.Scheduling.OperationalDamageReadyMarker>(SystemUpdatePhase.GameSimulation);

            // PlantRepairRequestProcessor: drains resolved budget results AND
            // owns the pending-plant set and snapshot revision. Active plant
            // repair transactions close in ModificationEnd below; this system
            // stays in GameSimulation only to hydrate/publish runtime state.
            updateSystem.RegisterAfter<PlantRepairRequestProcessor, PlantWearSimulation>(SystemUpdatePhase.GameSimulation);

            // Pause-safe plant-repair transaction pipeline. UI creates a
            // PlantRepairRequest in UIUpdate; intake/payment/commit close it in
            // ModificationEnd so repair is visible even at selectedSpeed==0.
            updateSystem.RegisterAt<PlantRepairIntakeSystem>(SystemUpdatePhase.ModificationEnd);
            updateSystem.RegisterAfter<PlantRepairPaymentSystem, PlantRepairIntakeSystem>(SystemUpdatePhase.ModificationEnd);
            updateSystem.RegisterAfter<PlantRepairCommitSystem, PlantRepairPaymentSystem>(SystemUpdatePhase.ModificationEnd);

            // Power plant disasters - random failures
            updateSystem.RegisterAfter<PowerPlantDisasterSystem, global::CivicSurvival.Domains.Engineering.Systems.ConstructionDelaySystem>(SystemUpdatePhase.GameSimulation);

            // Winter multiplier - increased consumption in cold weather
            updateSystem.RegisterAt<WinterMultiplierSystem>(SystemUpdatePhase.GameSimulation);

            // Single mod writer to final plant capacity.
            updateSystem.RegisterAfter<PowerCapacityResolverSystem, ConstructionDelaySystem>(SystemUpdatePhase.GameSimulation);
            updateSystem.RegisterAfter<PowerCapacityReadyMarker, PowerCapacityResolverSystem>(SystemUpdatePhase.GameSimulation);

            // Equipment UI - caches wear data for UI panels (IEquipmentUIService)
            updateSystem.RegisterAfter<EquipmentUISystem>(SystemUpdatePhase.GameSimulation);

            // NOTE: CascadeEffectSystem REMOVED - vanilla already handles electricity → water pump via EfficiencyFactor.ElectricitySupply

            Log.Info("Systems registered");
        }
    }
}
