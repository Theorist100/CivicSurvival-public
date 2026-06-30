using Game;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Utils;
using CivicSurvival.Domains.Mobilization.Systems;
using CivicSurvival.Domains.Mobilization.UI;

namespace CivicSurvival.Domains.Mobilization
{
    /// <summary>
    /// Mobilization domain - manpower management for AA and military operations.
    /// Priority 2150 = after Engineering (2100), before Economy (2200).
    /// </summary>
    public class MobilizationDomain : IFeatureModule, IUiFeatureModule
    {
        private static readonly LogContext Log = new("MobilizationDomain");

        private const int PRIORITY = 2150;

        public string Name => "Mobilization";
        public int Priority => PRIORITY;

        public void RegisterUI(UpdateSystem updateSystem)
        {
            updateSystem.RegisterAt<MobilizationUISystem>(SystemUpdatePhase.UIUpdate);
        }

        public void RegisterSystems(UpdateSystem updateSystem)
        {
            Log.Info("Registering systems...");

            // Mobilization system - manpower management for AA and military operations
            updateSystem.RegisterAt<MobilizationSystem>(SystemUpdatePhase.GameSimulation);

            // AA crew assignment - processes RequestCrewTag from AirDefense domain.
            // Placement can complete while selectedSpeed==0, so manpower deduction
            // and CrewAssigned writes must also run in a pause-tolerant phase.
            // Uses RequireForUpdate - zero overhead when no AA needs crew.
            updateSystem.RegisterAt<AACrewAssignmentSystem>(SystemUpdatePhase.ModificationEnd);

            Log.Info("Systems registered");
        }
    }
}
