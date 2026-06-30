using Game;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Systems;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Core.Features.CrossDomain.ThreatsAirDefense
{
    /// <summary>
    /// Cross-domain coordinator for the Threats↔AirDefense seam (D7 / Phase 5).
    /// Owns the shared intercept request flow + radar UI consumed by both
    /// sides. Closes transitively if either base feature is closed.
    /// </summary>
    public sealed class ThreatsAirDefenseFeature : IFeatureModule, IGatedFeatureModule
    {
        private static readonly LogContext Log = new("ThreatsAirDefenseFeature");

        // Must evaluate AFTER both required features (ThreatDamage=2502, AirDefense=2510)
        // so RequiresFeature gates see them in m_OpenFeatures. Priority order = registration
        // order = gate-evaluation order. Lower priority would close the gate transitively
        // and leave the coordinator's systems (InterceptProcessingSystem, ThreatRadarSystem)
        // unregistered. FeatureGraphValidator now enforces this invariant statically.
        private const int PRIORITY = 2511;

        public string Name => "ThreatsAirDefense";
        public int Priority => PRIORITY;

        public FeatureGate Gate { get; } = FeatureGate.AllOf.Of(
            new FeatureGate.RequiresFeature("ThreatDamage"),
            new FeatureGate.RequiresFeature("AirDefense"));

        public void RegisterSystems(UpdateSystem updateSystem)
        {
            Log.Info("Registering systems...");

            // InterceptBarrier lives in CoreKernel (Core/Systems/Scheduling/) — it's a
            // cross-feature scheduling anchor, not coordinator state. AirDefense-domain
            // producers reach it via World.GetOrCreateSystemManaged like a vanilla barrier.
            updateSystem.RegisterAfter<InterceptProcessingSystem, global::CivicSurvival.Core.Systems.Scheduling.InterceptBarrier>(SystemUpdatePhase.GameSimulation);
            updateSystem.RegisterAfter<ThreatRadarSystem, global::CivicSurvival.Core.Systems.Scheduling.ThreatMovementReadyMarker>(SystemUpdatePhase.GameSimulation);

            // Cross-domain ordering anchor for InterceptProcessingSystem (Axiom 5).
            // Waves's WaveExecutor orders itself after this marker instead of
            // importing InterceptProcessingSystem directly.
            updateSystem.RegisterAfter<global::CivicSurvival.Core.Systems.Scheduling.InterceptProcessingReadyMarker, InterceptProcessingSystem>(SystemUpdatePhase.GameSimulation);

            Log.Info("Systems registered");
        }
    }
}
