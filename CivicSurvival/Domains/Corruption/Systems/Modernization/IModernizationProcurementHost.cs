using Game;
using Game.Areas;
using Game.Common;
using Game.Simulation;
using Unity.Entities;
using CivicSurvival.Core.Components.Domain.Economy;
using CivicSurvival.Core.Components.Domain.Power;
using CivicSurvival.Core.Components.Lifecycle;
using CivicSurvival.Core.Interfaces.Domain.Power;
using CivicSurvival.Core.Components.Requests;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Interfaces.Domain.Corruption;
using CivicSurvival.Core.Interfaces.Domain.Economy;
using CivicSurvival.Core.Systems;
using CivicSurvival.Core.Systems.Scheduling;

namespace CivicSurvival.Domains.Corruption.Systems.Modernization
{
    /// <summary>
    /// Narrow host contract exposed by <see cref="DistrictModernizationSystem"/> to the
    /// procurement collaborators. Encapsulates ECS-context access (lookups, queries,
    /// ECB allocation, dependency registration) so the procurement processor and
    /// equipment installer remain plain classes outside the SystemBase hierarchy.
    /// </summary>
    internal interface IModernizationProcurementHost
    {
        World World { get; }
        IEventBus? EventBus { get; }
        GameSimulationEndBarrier GameSimulationEndBarrier { get; }
        IShadowReputationService ReputationService { get; }
        IShadowWalletService ResolveWalletService();
        GameTimeSystem? ResolveTimeSystem();
        double ElapsedTime { get; }

        EntityQuery BuildingsWithDistrictQuery { get; }
        EntityQuery CounterfeitQuery { get; }
        EntityQuery PendingModernizationBudgetQuery { get; }
        EntityQuery ModernizationInstallReceiptQuery { get; }

        ComponentLookup<CurrentDistrict> CurrentDistrictLookup { get; }
        IBackupPowerLinkReader BackupPowerLinks { get; }
        ComponentLookup<BackupPower> BackupPowerLookup { get; }
        ComponentLookup<CounterfeitBattery> CounterfeitBatteryLookup { get; }
        ComponentLookup<Deleted> DeletedLookup { get; }

        /// <summary>Issue a GameSimulationEndBarrier command buffer and register the system's
        /// current dependency as a producer for it.</summary>
        EntityCommandBuffer CreateCommandBuffer();
        void RegisterECBProducer();
    }
}
