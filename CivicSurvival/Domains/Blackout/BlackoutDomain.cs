using Game;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.Utils;
using CivicSurvival.Domains.Blackout.Systems;

namespace CivicSurvival.Domains.Blackout
{
    /// <summary>
    /// Blackout domain - power outages, rolling blackouts, district disconnection.
    /// Priority 2050 = Gameplay tier (after PowerGrid data, before Engineering stress).
    /// </summary>
    public class BlackoutDomain : IFeatureModule, IContentFeatureModule
    {
        public void RegisterContent() => SatireRegistry.Register(new BlackoutSatireProvider());

        private const int PRIORITY = 2050;

        private static readonly LogContext Log = new("BlackoutDomain");

        public string Name => "Blackout";
        public int Priority => PRIORITY;

        public void RegisterSystems(UpdateSystem updateSystem)
        {
            Log.Info("Registering systems...");

            // Blackout state setup - adds BlackoutState component to new buildings.
            // Modification4 + ModificationBarrier4 (mirror of vanilla IgniteSystem,
            // SystemOrder.cs:162): the structural AddComponent<BlackoutState> on a new
            // consumer building flushes within Modification4, BEFORE RequiredBatchesSystem
            // and the render-side chunk-cache collection of the same frame. Run in
            // GameSimulation (BlackoutSystem) the add played back out of phase with the
            // render pass → zeroed render chunk-cache crash class. Modification4 (MainLoop)
            // runs before GameSimulation (LateUpdate) in the frame, so BlackoutState still
            // exists before BlackoutSystem reads it — invariant preserved (even strengthened).
            updateSystem.RegisterAt<BlackoutStateSetupSystem>(SystemUpdatePhase.Modification4);

            // Blackout system - enforces power cuts in blackout districts
            updateSystem.RegisterBefore<BlackoutSystem, global::CivicSurvival.Core.Systems.Scheduling.BlackoutReadyMarker>(SystemUpdatePhase.GameSimulation);

            // Blackout event producer - generates events for UI/telemetry
            updateSystem.RegisterAfter<BlackoutEventProducerSystem, global::CivicSurvival.Core.Systems.Scheduling.PowerDataReadyMarker>(SystemUpdatePhase.GameSimulation);

            // NOTE: BlackoutStressSystem REMOVED - logic moved to BlackoutCalculator
            // in MentalHealthResolverSystem (Logic Composition pattern)

            Log.Info("Systems registered");
        }
    }
}
