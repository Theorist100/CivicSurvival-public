using Game;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Utils;
using CivicSurvival.Domains.ThreatFlight.Systems;

namespace CivicSurvival.Domains.ThreatFlight
{
    /// <summary>
    /// ThreatFlight domain — drone/ballistic movement, obstacle avoidance, render sync.
    /// Priority 2501 = Gameplay tier (same as Threats, registered separately via Mod.cs).
    ///
    /// Decoupled from Threats domain — no cross-domain RegisterAfter].
    /// 1-frame latency at boundary is acceptable (drone overshoot = 0.8m at 50 m/s × 16ms).
    /// </summary>
    public class ThreatFlightDomain : IFeatureModule
    {
        private static readonly LogContext Log = new("ThreatFlightDomain");

        private const int PRIORITY = 2501;

        public string Name => "ThreatFlight";
        public int Priority => PRIORITY;

        public void RegisterSystems(UpdateSystem updateSystem)
        {
            Log.Info("Registering systems...");

            // Threat movement — moves drones/ballistics toward targets (async job pattern)
            updateSystem.RegisterBefore<ThreatMovementSystem, global::CivicSurvival.Core.Systems.Scheduling.ThreatMovementReadyMarker>(SystemUpdatePhase.GameSimulation);

            Log.Info("Systems registered");
        }
    }
}
