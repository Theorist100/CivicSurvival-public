using Game;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Utils;
using CivicSurvival.Domains.Refugees.Systems;

namespace CivicSurvival.Domains.Refugees
{
    /// <summary>
    /// Refugee domain - spawn, migration, integration.
    /// Priority 2700 = Gameplay tier (after Narrative).
    /// </summary>
    public class RefugeesDomain : IFeatureModule
    {
        private static readonly LogContext Log = new("RefugeesDomain");

        private const int PRIORITY = 2700;

        public string Name => "Refugees";
        public int Priority => PRIORITY;
        public void RegisterSystems(UpdateSystem updateSystem)
        {
            Log.Info("Registering systems...");

            // Refugee coordinator - reacts to ActChangedEvent
            updateSystem.RegisterAt<RefugeeInfluxCoordinator>(SystemUpdatePhase.GameSimulation);

            // Refugee spawn - creates refugee entities.
            // Producer of spawn ECB only; does not read state committed by a prior drain
            // (reads time/milestone/parks/outside-connections/config/service state, all live).
            // RefugeeProcessSystem and RefugeeMigrationSystem operate on the next tick after
            // ECB playback via their own queries, so same-tick visibility is not required.
            // Pre-migration anchor on Game.EndFrameBarrier was effectively "next render frame
            // after MainLoop barrier drained"; after the sub-tick barrier migration the same
            // anchor on GameSimulationEndBarrier puts the spawn inside the closed-gate window
            // and adds no real ordering value. Default placement is correct.
            updateSystem.RegisterAt<RefugeeSpawnSystem>(SystemUpdatePhase.GameSimulation);

            // Refugee process - disables PropertySeeker on spawned households so
            // vanilla HouseholdFindPropertySystem cannot assign them permanent housing.
            // RequireForUpdate keeps the system silent outside the influx window.
            updateSystem.RegisterBefore<RefugeeProcessSystem, global::Game.Simulation.HouseholdFindPropertySystem>(SystemUpdatePhase.GameSimulation);

            // Refugee orphan scan - edge-triggered detector that re-arms the relocation
            // marker when a sheltering park is destroyed (no reactive cross-domain signal
            // exists — Axiom 5). Gated by refugee presence + a live-park-count drop, so it
            // costs a count-compare when nothing is destroyed. Registered BEFORE migration
            // so the marker it sets is visible to the migration consumer in the same/next
            // tick (registration-site ordering, Axiom 7 — not Unity attributes).
            updateSystem.RegisterBefore<RefugeeOrphanScanSystem, global::CivicSurvival.Domains.Refugees.Systems.RefugeeMigrationSystem>(SystemUpdatePhase.GameSimulation);

            // Refugee migration - assigns park temp homes before vanilla housing search.
            // RefugeeSpawnSystem output is ECB-visible on a later tick, and RMS is
            // throttled by game hours, so a direct spawn anchor is unnecessary here.
            updateSystem.RegisterBefore<RefugeeMigrationSystem, global::Game.Simulation.HouseholdFindPropertySystem>(SystemUpdatePhase.GameSimulation);

            // Refugee integration - integrates refugees into city
            updateSystem.RegisterAfter<RefugeeIntegrationSystem, global::CivicSurvival.Domains.Refugees.Systems.RefugeeMigrationSystem>(SystemUpdatePhase.GameSimulation);

            // Refugee support costs - deducts budget for refugee support (CDI-3)
            updateSystem.RegisterAfter<RefugeeSupportCostSystem, global::CivicSurvival.Domains.Refugees.Systems.RefugeeIntegrationSystem>(SystemUpdatePhase.GameSimulation);

            // Refugee retention - strips vanilla MovingAway from refugee households
            // before HouseholdMoveAwaySystem can walk them out of the city, repairs
            // citizen-side traces, and tops wallets up to zero so the standing
            // NoMoney move-away condition never fires. Refugees never leave.
            updateSystem.RegisterBefore<RefugeeRetentionSystem, global::Game.Simulation.HouseholdMoveAwaySystem>(SystemUpdatePhase.GameSimulation);

            Log.Info("Systems registered");
        }
    }
}
