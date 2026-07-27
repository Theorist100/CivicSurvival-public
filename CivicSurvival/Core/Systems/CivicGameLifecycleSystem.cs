using Game;
using Colossal.Serialization.Entities;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Diagnostics;
using CivicSurvival.Core.Services;

namespace CivicSurvival.Core.Systems
{
    /// <summary>
    /// Minimal vanilla lifecycle bridge. The gameplay-ready flip is owned by
    /// PostLoadValidationSystem after its post-load pass; this system only observes
    /// vanilla loading-complete and cleanup transitions.
    /// </summary>
    [ActIndependent]
    [FrameworkSystem]
    public partial class CivicGameLifecycleSystem : GameSystemBase
    {
        protected override void OnCreate()
        {
            base.OnCreate();
            CivicGameLifecycle.RegisterDefaultSubscribers();
        }

        protected override void OnUpdate() { }

        protected override void OnGamePreload(Purpose purpose, GameMode mode)
        {
            base.OnGamePreload(purpose, mode);

            // Crash-heartbeat loading boundary: a city load / new game starts here (vanilla raises
            // OnGamePreload before any Deserialize). Marking Loading means a watchdog ANR during the
            // synchronous load is read as a legitimate long sync op, not an in-game freeze.
            if (mode == GameMode.Game && (purpose == Purpose.LoadGame || purpose == Purpose.NewGame))
                CrashContextProvider.SetPhase(LifecyclePhase.Loading);
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);

            if (mode == GameMode.Game && (purpose == Purpose.LoadGame || purpose == Purpose.NewGame))
            {
                // Load finished but the sim has NOT ticked yet. Decompile-verified (SimulationSystem.OnUpdate):
                // GameSimulation ticks only at selectedSpeed != 0 (:221/:273), and pausedAfterLoading sets
                // selectedSpeed = 0 here (:213) — so GameTimeSystem (which owns ActiveSim) may not run for a
                // long time, or ever, until the player unpauses. Stamp Loaded; ActiveSim is set at the first
                // real GameTimeSystem tick. A freeze in this window is not a gameplay-sim freeze.
                CrashContextProvider.SetPhase(LifecyclePhase.Loaded);
                CivicGameLifecycle.MarkLoadingComplete(purpose, mode);
                return;
            }

            if (mode == GameMode.MainMenu && purpose == Purpose.Cleanup)
            {
                CrashContextProvider.SetPhase(LifecyclePhase.Menu);
                CivicGameLifecycle.MarkNotReady("ExitToMenu");
            }
        }
    }
}
