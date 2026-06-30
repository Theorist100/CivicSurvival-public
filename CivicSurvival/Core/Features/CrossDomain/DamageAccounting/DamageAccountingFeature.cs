using Game;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Features.CrossDomain.DamageAccounting;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Core.Features.CrossDomain.DamageAccounting
{
    /// <summary>
    /// AlwaysOpen Coordinator (D9, locked) consolidating damage-event accumulation
    /// and damage stats derivation. Documented optional producers: Engineering,
    /// PowerBackup, ThreatDamage. No required dependencies — coordinator is
    /// no-op-correct when no DamageAppliedEvent / RepairCompletedEvent entities
    /// exist (closed-feature semantics §2.3).
    /// </summary>
    public sealed class DamageAccountingFeature : IFeatureModule
    {
        private static readonly LogContext Log = new("DamageAccountingFeature");

        private const int PRIORITY = 2495;

        public string Name => "DamageAccounting";
        public int Priority => PRIORITY;

        public void RegisterSystems(UpdateSystem updateSystem)
        {
            Log.Info("Registering systems...");

            updateSystem.RegisterAfter<DamageAccountingSystem, global::CivicSurvival.Core.Systems.Scheduling.PowerCapacityWriterGroup>(SystemUpdatePhase.GameSimulation);
            updateSystem.RegisterAt<DamageStatsUpdateSystem>(SystemUpdatePhase.GameSimulation);

            // Cross-domain ordering anchor for DamageStatsUpdateSystem (Axiom 5).
            // GridWarfare's CityStabilitySystem orders itself after this marker
            // instead of importing DamageStatsUpdateSystem directly.
            updateSystem.RegisterAfter<global::CivicSurvival.Core.Systems.Scheduling.DamageStatsReadyMarker, DamageStatsUpdateSystem>(SystemUpdatePhase.GameSimulation);

            Log.Info("Systems registered");
        }
    }
}
