using Game;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Utils;
using CivicSurvival.Domains.Tutorial.Systems;

namespace CivicSurvival.Domains.Tutorial
{
    /// <summary>
    /// Tutorial domain - tutorial modals and guidance.
    /// Priority 2310 = Gameplay tier (after Scenario).
    /// </summary>
    public class TutorialDomain : IFeatureModule, IUiFeatureModule
    {
        private static readonly LogContext Log = new("TutorialDomain");

        private const int PRIORITY = 2310;

        public string Name => "Tutorial";
        public int Priority => PRIORITY;
        public void RegisterSystems(UpdateSystem updateSystem)
        {
            Log.Info("Systems registered (UI-only feature — see RegisterUI)");
        }

        public void RegisterUI(UpdateSystem updateSystem)
        {
            // Tutorial modals are entirely UI-driven (UIUpdate phase).
            updateSystem.RegisterAt<CrisisTutorialSystem>(SystemUpdatePhase.UIUpdate);
            updateSystem.RegisterAt<MilestoneTutorialSystem>(SystemUpdatePhase.UIUpdate);
        }
    }
}
