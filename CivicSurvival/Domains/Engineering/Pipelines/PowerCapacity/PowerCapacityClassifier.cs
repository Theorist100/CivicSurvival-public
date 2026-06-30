using System;
using System.Collections.Generic;
using Game.Buildings;
using Game.Prefabs;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Components.Domain.Engineering;
using CivicSurvival.Core.Components.Domain.Power;
using CivicSurvival.Core.Components.Threats;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Domains.Engineering.Pipelines.PowerCapacity
{
    internal static class PowerCapacityClassifier
    {
        // Vanilla's L*H clamp ceiling (m²) from PowerPlantAISystem.GetWaterCapacity.
        // Anything larger is treated as a flat 1_000_000 m² for capacity purposes.
        private const float HydroLengthHeightClampM2 = 1_000_000f;
        private const int UpgradeHashSeed = 17;
        private const int UpgradeHashMultiplier = 31;
        private const int HydroShapeHashMultiplier = 397;

        public static PlantClassification ClassifyPlant(ref PowerCapacityPipelineContext ctx, Entity entity)
        {
            if (!ctx.PrefabRefLookup.TryGetComponent(entity, out var prefabRef))
                return new PlantClassification(PlantKind.Unclassified, 0, PowerPlantUtils.PlantType.Unknown, CapacityChannel.GridProducer);

            var plantType = PowerPlantUtils.GetPlantType(ctx.PrefabSystem, prefabRef);
            var channel = ClassifyChannel(ref ctx, prefabRef.m_Prefab);
            var capacity = GetOriginalNameplateCapacity(ref ctx, entity, prefabRef.m_Prefab, channel);
            var kind = ClassifyPlantKind(ref ctx, prefabRef.m_Prefab, plantType);

            return new PlantClassification(kind, capacity, plantType, channel);
        }

        public static CapacityChannel ClassifyChannel(ref PowerCapacityPipelineContext ctx, Entity prefabEntity)
        {
            return ctx.EmergencyGeneratorDataLookup.HasComponent(prefabEntity)
                && ctx.BatteryDataLookup.HasComponent(prefabEntity)
                ? CapacityChannel.EmergencyBattery
                : CapacityChannel.GridProducer;
        }

        public static PlantKind ClassifyPlantKind(
            ref PowerCapacityPipelineContext ctx,
            Entity prefabEntity,
            PowerPlantUtils.PlantType plantType)
        {
            switch (plantType)
            {
                case PowerPlantUtils.PlantType.Wind:
                case PowerPlantUtils.PlantType.Solar:
                    return PlantKind.Renewable;
                case PowerPlantUtils.PlantType.Hydro:
                    return PlantKind.Hydro;
                case PowerPlantUtils.PlantType.Geothermal:
                    return PlantKind.Geothermal;
                default:
                    if (ctx.EmergencyGeneratorDataLookup.HasComponent(prefabEntity))
                        return PlantKind.EmergencyGenerator;
                    if (ctx.PowerPlantDataLookup.HasComponent(prefabEntity))
                        return PlantKind.Thermal;
                    if (ctx.GarbagePoweredDataLookup.HasComponent(prefabEntity))
                        return PlantKind.Garbage;
                    return PlantKind.Unclassified;
            }
        }

        /// <summary>
        /// Compute stable upgraded nameplate capacity for a power plant, mirroring vanilla's
        /// <c>PowerPlantAISystem</c> upgrade combination logic but stripped of runtime
        /// scaling (efficiency, weather, resource availability) — we want a stable base
        /// for damage / repair / disaster / import-cap modifiers, not current production.
        ///
        /// Strategy (per <c>PowerPlantAISystem.cs:184-246</c>):
        /// - Each typed power data (<c>PowerPlantData</c>, <c>EmergencyGeneratorData</c>,
        ///   <c>WindPoweredData</c>, <c>SolarPoweredData</c>, <c>GarbagePoweredData</c>,
        ///   <c>GroundWaterPoweredData</c>, <c>WaterPoweredData</c>) is resolved **independently**
        ///   via <see cref="ResolveAndFold{T}"/>: take the prefab's value (parent or first
        ///   subobject that has it) or <c>default(T)</c>, then always fold <c>InstalledUpgrade</c>
        ///   contributions on top via <c>UpgradeUtils.CombineStats</c>.
        /// - Folding always runs, even when the base prefab does not carry the type — this matches
        ///   vanilla and lets an upgrade introduce a new power type (e.g. solar panel upgrade on a
        ///   thermal plant). <c>CombineStats</c> already skips <c>BuildingOption.Inactive</c>.
        /// - Hydro contribution mirrors vanilla
        ///   <c>PowerPlantAISystem.GetWaterCapacity(WaterPowered, WaterPoweredData)</c>:
        ///   <c>min(L*H, 1_000_000f) × WaterPoweredData.m_CapacityFactor</c>, where
        ///   <c>L</c> and <c>H</c> come from the runtime <c>Game.Buildings.WaterPowered</c>
        ///   component populated by <c>WaterPoweredInitializeSystem</c>. Before that
        ///   initializer runs, the component is missing and we contribute 0 (matches
        ///   vanilla's natural saturation).
        /// - Final nameplate is the sum across types. Type-specific extraction (different
        ///   nameplate fields: <c>m_ElectricityProduction</c>, <c>m_Production</c>,
        ///   <c>m_Capacity</c>, hydro formula above) is unrolled.
        /// </summary>
        public static int GetOriginalNameplateCapacity(
            ref PowerCapacityPipelineContext ctx,
            Entity buildingEntity,
            Entity prefabEntity,
            CapacityChannel channel = CapacityChannel.GridProducer)
        {
            bool hasUpgrades = ctx.InstalledUpgradeLookup.TryGetBuffer(buildingEntity, out var upgrades);
            int total = 0;

            var ppd = ResolveAndFold(ref ctx, prefabEntity, upgrades, hasUpgrades, ref ctx.PowerPlantDataLookup);
            if (ppd.m_ElectricityProduction > 0)
                total += ppd.m_ElectricityProduction;

            if (channel != CapacityChannel.EmergencyBattery)
            {
                var egd = ResolveAndFold(ref ctx, prefabEntity, upgrades, hasUpgrades, ref ctx.EmergencyGeneratorDataLookup);
                if (egd.m_ElectricityProduction > 0)
                    total += egd.m_ElectricityProduction;
            }

            var windData = ResolveAndFold(ref ctx, prefabEntity, upgrades, hasUpgrades, ref ctx.WindPoweredDataLookup);
            if (windData.m_Production > 0)
                total += windData.m_Production;

            var solarData = ResolveAndFold(ref ctx, prefabEntity, upgrades, hasUpgrades, ref ctx.SolarPoweredDataLookup);
            if (solarData.m_Production > 0)
                total += solarData.m_Production;

            var garbageData = ResolveAndFold(ref ctx, prefabEntity, upgrades, hasUpgrades, ref ctx.GarbagePoweredDataLookup);
            if (garbageData.m_Capacity > 0)
                total += garbageData.m_Capacity;

            var groundWaterData = ResolveAndFold(ref ctx, prefabEntity, upgrades, hasUpgrades, ref ctx.GroundWaterPoweredDataLookup);
            if (groundWaterData.m_Production > 0)
                total += groundWaterData.m_Production;

            var waterData = ResolveAndFold(ref ctx, prefabEntity, upgrades, hasUpgrades, ref ctx.WaterPoweredDataLookup);
            if (waterData.m_CapacityFactor > 0f
                && ctx.WaterPoweredLookup.TryGetComponent(buildingEntity, out var waterPowered))
            {
                float clamped = math.min(waterPowered.m_Length * waterPowered.m_Height, HydroLengthHeightClampM2);
                if (clamped > 0f)
                    total += (int)Math.Round(clamped * waterData.m_CapacityFactor);
            }

            return total;
        }

        public static int ComputeUpgradeHash(ref PowerCapacityPipelineContext ctx, Entity buildingEntity)
        {
            if (!ctx.InstalledUpgradeLookup.TryGetBuffer(buildingEntity, out var upgrades) || upgrades.Length == 0)
                return 0;

            var keys = new List<long>(upgrades.Length);
            for (int i = 0; i < upgrades.Length; i++)
            {
                Entity upgradePrefab = upgrades[i].m_Upgrade;
                keys.Add(((long)upgradePrefab.Index << 32) | (uint)upgradePrefab.Version);
            }

            keys.Sort();
            unchecked
            {
                int hash = UpgradeHashSeed;
                for (int i = 0; i < keys.Count; i++)
                    hash = (hash * UpgradeHashMultiplier) ^ keys[i].GetHashCode();
                return hash;
            }
        }

        public static int ComputeHydroShapeHash(ref PowerCapacityPipelineContext ctx, Entity buildingEntity)
        {
            if (!ctx.WaterPoweredLookup.TryGetComponent(buildingEntity, out var waterPowered))
                return 0;

            unchecked
            {
                int lengthBucket = (int)Math.Round(waterPowered.m_Length);
                int heightBucket = (int)Math.Round(waterPowered.m_Height);
                return (lengthBucket * HydroShapeHashMultiplier) ^ heightBucket;
            }
        }

        /// <summary>
        /// Resolve a single typed power-data struct for a building and fold in any active
        /// <c>InstalledUpgrade</c> contributions, mirroring vanilla's per-type pattern in
        /// <c>PowerPlantAISystem.OnUpdate</c>.
        ///
        /// Resolution order for the base struct:
        /// 1. Read directly from the building prefab. If present → that's the base.
        /// 2. Otherwise scan <c>SubObject</c> child prefabs and take the first one carrying T.
        ///    Civic-specific wrapper prefabs sometimes host the actual power data on a subobject.
        /// 3. If no candidate has T, the base stays <c>default(T)</c> (zero contribution).
        ///
        /// Folding always runs when an upgrade buffer is present — this is the load-bearing
        /// invariant. Even if no candidate prefab has T, an active upgrade prefab may have it
        /// (e.g. solar-panel upgrade on a thermal plant); folding over <c>default(T)</c> still
        /// captures that contribution. <c>UpgradeUtils.CombineStats</c> already filters
        /// <c>BuildingOption.Inactive</c> upgrades internally.
        /// </summary>
        private static T ResolveAndFold<T>(
            ref PowerCapacityPipelineContext ctx,
            Entity buildingPrefab,
            DynamicBuffer<InstalledUpgrade> upgrades,
            bool hasUpgrades,
            ref ComponentLookup<T> lookup)
            where T : unmanaged, IComponentData, ICombineData<T>
        {
            if (!lookup.TryGetComponent(buildingPrefab, out T data))
            {
                if (ctx.SubObjectLookup.TryGetBuffer(buildingPrefab, out var subObjects))
                {
                    for (int i = 0; i < subObjects.Length; i++)
                    {
                        if (lookup.TryGetComponent(subObjects[i].m_Prefab, out T subData))
                        {
                            data = subData;
                            break;
                        }
                    }
                }
            }

            if (hasUpgrades)
                UpgradeUtils.CombineStats(ref data, upgrades, ref ctx.PrefabRefLookup, ref lookup);

            return data;
        }

        public static NativeHashSet<long> BuildCollapsedProducerSet(ref PowerCapacityPipelineContext ctx)
        {
            var existingCollapsed = new NativeHashSet<long>(16, Allocator.Temp);
            using var collapsedEntities = ctx.CollapsedProducerQuery.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < collapsedEntities.Length; i++)
            {
                Entity collapsedEntity = collapsedEntities[i];
                if (!ctx.EntityManager.HasComponent<CollapsedProducer>(collapsedEntity))
                    continue;
                var collapsed = ctx.EntityManager.GetComponentData<CollapsedProducer>(collapsedEntity);
                existingCollapsed.Add(BuildingIdentityKey.Pack(collapsed.Building.Index, collapsed.Building.Version));
            }

            return existingCollapsed;
        }

    }
}
