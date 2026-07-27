using Game;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Utils;
using CivicSurvival.Domains.NeighborEnvy.Systems;

namespace CivicSurvival.Domains.NeighborEnvy
{
    /// <summary>
    /// Neighbor envy domain - citizens jealous of powered neighbors.
    /// Priority 2250 = Gameplay tier (after Countermeasures).
    /// </summary>
    public class NeighborEnvyDomain : IFeatureModule
    {
        private static readonly LogContext Log = new("NeighborEnvyDomain");

        private const int PRIORITY = 2250;

        public string Name => "NeighborEnvy";
        public int Priority => PRIORITY;
        public void RegisterSystems(UpdateSystem updateSystem)
        {
            Log.Info("Registering systems...");

            // EnvyAffected setup - seeds the EnvyAffected tag (disabled) onto residential buildings.
            // Modification4 + ModificationBarrier4 (mirror of BlackoutStateSetupSystem / vanilla
            // IgniteSystem, SystemOrder.cs:162): the structural AddComponent<EnvyAffected> on a
            // residential building flushes within Modification4, BEFORE RequiredBatchesSystem and
            // the render-side chunk-cache collection of the same frame. Done in GameSimulation
            // (where the envy logic used to add it) the add played back out of phase with the render
            // pass → zeroed render chunk-cache crash class. Modification4 (MainLoop) runs before
            // GameSimulation (LateUpdate), so EnvyAffected exists before NeighborEnvySystem reads it.
            updateSystem.RegisterAt<EnvyAffectedSetupSystem>(SystemUpdatePhase.Modification4);

            // Neighbor envy - citizens jealous of powered neighbors.
            // ORDER-INVARIANT: NeighborEnvy maintains EnvyAffected tags (enable-bit flip only — the
            // structural first-add is owned by EnvyAffectedSetupSystem above); MentalHealthResolver
            // reads them same-frame to compute Pressure_Envy. Anchored before the Core
            // PsyPressureWriterGroup marker, which the Core ordering chain carries through to
            // MentalHealthResolverSystem (PsyPressureWriterGroup → SpotterReadyMarker →
            // CognitiveStateReadyMarker → MentalHealthResolverSystem).
            // This keeps NeighborEnvy free of any Cognitive-domain import (Axiom 5).
            updateSystem.RegisterBefore<NeighborEnvySystem, global::CivicSurvival.Core.Systems.Scheduling.PsyPressureWriterGroup>(SystemUpdatePhase.GameSimulation);

            Log.Info("Systems registered");
        }
    }
}
