using Game;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Utils;
using CivicSurvival.Domains.PowerBackup.Systems;
using CivicSurvival.Domains.PowerBackup.UI;

namespace CivicSurvival.Domains.PowerBackup
{
    /// <summary>
    /// PowerBackup domain - generators, batteries, backup power distribution.
    /// Priority 2970 = after Efficiency so BackupPower reads the completed generator-efficiency pipeline.
    /// </summary>
    public class PowerBackupDomain : IFeatureModule, IUiFeatureModule
    {
        private static readonly LogContext Log = new("PowerBackupDomain");

        private const int PRIORITY = 2970;

        public string Name => "PowerBackup";
        public int Priority => PRIORITY;

        public void RegisterUI(UpdateSystem updateSystem)
        {
            updateSystem.RegisterAt<BackupPowerUISystem>(SystemUpdatePhase.UIUpdate);
        }

        public void RegisterSystems(UpdateSystem updateSystem)
        {
            Log.Info("Registering systems...");

            // Backup power runtime - calculates generator/battery output
            updateSystem.RegisterAfter<BackupPowerRuntimeSystem, global::CivicSurvival.Core.Systems.Scheduling.GeneratorEfficiencyReadyMarker>(SystemUpdatePhase.GameSimulation);

            // Backup power distribution - distributes power to buildings
            updateSystem.RegisterAt<BackupPowerDistributionSystem>(SystemUpdatePhase.GameSimulation);

            // Backup power effects - applies power effects to buildings
            updateSystem.RegisterAt<BackupPowerEffectsSystem>(SystemUpdatePhase.GameSimulation);

            Log.Info("Systems registered");
        }
    }
}
