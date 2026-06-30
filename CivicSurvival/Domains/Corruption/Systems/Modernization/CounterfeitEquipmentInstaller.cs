using Game;
using Game.Areas;
using Game.Buildings;
using Game.Common;
using Game.Simulation;
using Unity.Collections;
using Unity.Entities;
using CivicSurvival.Core.Components.Domain.Economy;
using CivicSurvival.Core.Components.Domain.Power;
using CivicSurvival.Core.Components.Requests;
using CivicSurvival.Core.Interfaces.Domain.Power;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Domains.Corruption.Systems.Modernization
{
    /// <summary>
    /// Equipment install for District Modernization: creates the BackupPower mod
    /// entity per building and (for corrupt procurement) the CounterfeitBattery
    /// sidecar. Reads cleanup scratch via <see cref="CounterfeitCleanupService.IsBeingCleaned"/>
    /// so the same procurement that wipes old counterfeit equipment can replace it
    /// in place without skipping a recycled entity index.
    /// </summary>
    internal sealed class CounterfeitEquipmentInstaller
    {
        private readonly CounterfeitCleanupService m_Cleanup;

        public CounterfeitEquipmentInstaller(CounterfeitCleanupService cleanup)
        {
            m_Cleanup = cleanup;
        }

        /// <summary>Stateless — no-op. Present so the system's ResetState path can
        /// reference every sub-service uniformly (CIVIC314 coverage).</summary>
        public void Reset()
        {
            // No mutable state — intentionally empty.
        }

        /// <summary>Install standard home batteries. Returns actual installed count
        /// (may be lower than <paramref name="buildingCount"/> if buildings were
        /// demolished between intent and confirm).</summary>
        public int InstallHonest(
            Unity.Collections.FixedString128Bytes operationKey,
            int districtIndex,
            int buildingCount,
            int activationDay,
            int totalCost,
            EntityCommandBuffer ecb,
            EntityQuery buildingsWithDistrictQuery,
            ComponentLookup<CurrentDistrict> currentDistrictLookup,
            IBackupPowerLinkReader backupLinks)
        {
            int installed = 0;
            var entities = buildingsWithDistrictQuery.ToEntityArray(Allocator.Temp);
            try
            {
                for (int i = 0; i < entities.Length && installed < buildingCount; i++)
                {
                    Entity entity = entities[i];
                    if (!currentDistrictLookup.TryGetComponent(entity, out var district))
                        continue;
                    if (district.m_District.Index != districtIndex)
                        continue;

                    // Skip buildings that already have a live backup (unless being replaced this pass).
                    if (backupLinks.TryGet(BuildingRef.FromEntity(entity), out _)
                        && !m_Cleanup.IsBeingCleaned(BuildingIdentityKey.Pack(entity)))
                        continue;

                    var battery = BackupPowerFactory.Create(BackupPowerType.HomeBattery, BuildingRef.FromEntity(entity));
                    var backupModEntity = ecb.CreateEntity();
                    ecb.AddComponent(backupModEntity, battery);
                    ecb.AddComponent(backupModEntity, new BatteryLayerTag { Layer = BatteryLayer.Private });
                    ecb.AddComponent(backupModEntity, CreateReceipt(
                        operationKey,
                        districtIndex,
                        ContractorType.Honest,
                        activationDay,
                        totalCost,
                        entity,
                        ModernizationReceiptKind.BackupPower));
                    // Link established via battery.Building (set in BackupPowerFactory.Create);
                    // RebuildLinkMap publishes it. Nothing is written on the vanilla building.
                    installed++;
                }
            }
            finally
            {
                if (entities.IsCreated) entities.Dispose();
            }
            return installed;
        }

        /// <summary>Install counterfeit (reduced-capacity) batteries plus the sidecar
        /// CounterfeitBattery mod entity carrying installation district / day metadata.</summary>
        public int InstallCounterfeit(
            Unity.Collections.FixedString128Bytes operationKey,
            int districtIndex,
            int buildingCount,
            int currentDay,
            int totalCost,
            EntityCommandBuffer ecb,
            EntityQuery buildingsWithDistrictQuery,
            ComponentLookup<CurrentDistrict> currentDistrictLookup,
            IBackupPowerLinkReader backupLinks)
        {
            var spCfg = BalanceConfig.Current.ShadowProcurement;
            int installed = 0;
            var entities = buildingsWithDistrictQuery.ToEntityArray(Allocator.Temp);
            try
            {
                for (int i = 0; i < entities.Length && installed < buildingCount; i++)
                {
                    Entity entity = entities[i];
                    if (!currentDistrictLookup.TryGetComponent(entity, out var district))
                        continue;
                    if (district.m_District.Index != districtIndex)
                        continue;

                    // Skip buildings that already have a live backup (unless being replaced this pass).
                    if (backupLinks.TryGet(BuildingRef.FromEntity(entity), out _)
                        && !m_Cleanup.IsBeingCleaned(BuildingIdentityKey.Pack(entity)))
                        continue;

                    var battery = BackupPowerFactory.Create(BackupPowerType.HomeBattery, BuildingRef.FromEntity(entity));
                    battery.CapacityWh = (int)System.Math.Round(battery.CapacityWh * spCfg.CounterfeitCapacityMult);
                    battery.CurrentChargeWh = battery.CapacityWh;

                    var backupModEntity = ecb.CreateEntity();
                    ecb.AddComponent(backupModEntity, battery);
                    ecb.AddComponent(backupModEntity, new BatteryLayerTag { Layer = BatteryLayer.Private });
                    ecb.AddComponent(backupModEntity, CreateReceipt(
                        operationKey,
                        districtIndex,
                        ContractorType.YourGuy,
                        currentDay,
                        totalCost,
                        entity,
                        ModernizationReceiptKind.BackupPower));
                    // Link established via battery.Building (set in BackupPowerFactory.Create);
                    // RebuildLinkMap publishes it. Nothing is written on the vanilla building.

                    var counterfeitModEntity = ecb.CreateEntity();
                    ecb.AddComponent(counterfeitModEntity, new CounterfeitBattery
                    {
                        Building = BuildingRef.FromEntity(entity),
                        FireRiskMultiplier = spCfg.CounterfeitFireRiskMult,
                        DegradationRate = spCfg.CounterfeitDegradationMult,
                        InstallationDay = currentDay,
                        InstallationDistrictId = districtIndex
                    });
                    ecb.AddComponent(counterfeitModEntity, CreateReceipt(
                        operationKey,
                        districtIndex,
                        ContractorType.YourGuy,
                        currentDay,
                        totalCost,
                        entity,
                        ModernizationReceiptKind.CounterfeitBattery));

                    installed++;
                }
            }
            finally
            {
                if (entities.IsCreated) entities.Dispose();
            }
            return installed;
        }

        private static ModernizationInstallReceipt CreateReceipt(
            Unity.Collections.FixedString128Bytes operationKey,
            int districtIndex,
            ContractorType contractor,
            int activationDay,
            int totalCost,
            Entity building,
            ModernizationReceiptKind kind)
        {
            return new ModernizationInstallReceipt
            {
                OperationKey = operationKey,
                DistrictId = districtIndex,
                Contractor = contractor,
                ActivationDay = activationDay,
                TotalCost = totalCost,
                BuildingKey = BuildingIdentityKey.Pack(building),
                Kind = kind
            };
        }
    }
}
