using System;
using Game.Buildings;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Simulation;
using Unity.Collections;
using Unity.Entities;
using CivicSurvival.Core.Components.Domain.Economy;
using CivicSurvival.Core.Components.Domain.Engineering;
using CivicSurvival.Core.Components.Domain.Power;
using CivicSurvival.Core.Components.Threats;

namespace CivicSurvival.Domains.Engineering.Pipelines.PowerCapacity
{
    public struct PowerCapacityPipelineContext
    {
        public EntityManager EntityManager;
        public EntityCommandBuffer Ecb;
        public Func<EntityCommandBuffer> EcbFactory;
        public bool HasEcb;
        public bool IsSafeFrame;
        public bool ConstructionDelayEnabled;
        public PrefabSystem PrefabSystem;

        public EntityQuery PlantProducerQuery;
        public EntityQuery OutsideConnectionQuery;
        public EntityQuery ResolvedPlantQuery;
        public EntityQuery GridStressQuery;
        public EntityQuery UnderConstructionQuery;
        public EntityQuery DisabledByDisasterQuery;
        public EntityQuery EquipmentWearQuery;
        public EntityQuery PowerPlantDamageQuery;
        public EntityQuery CollapsedProducerQuery;
        public EntityQuery DistrictPowerQuery;
        public EntityQuery ExternalPowerInputQuery;
        public EntityQuery ShadowExportStateQuery;

        public ComponentLookup<ElectricityProducer> ProducerLookup;
        public ComponentLookup<PlantBaseCapacity> BaseCapacityLookup;
        public ComponentLookup<PowerPlantKind> PlantKindLookup;
        public ComponentLookup<GridStressModifier> GridStressModifierLookup;
        public ComponentLookup<ConstructionModifier> ConstructionModifierLookup;
        public ComponentLookup<EquipmentWearModifier> WearModifierLookup;
        public ComponentLookup<OperationalDamageModifier> OperationalDamageModifierLookup;
        public ComponentLookup<DisasterDamageModifier> DisasterDamageModifierLookup;
        public ComponentLookup<SaturationModifier> SaturationModifierLookup;
        public ComponentLookup<ImportCapModifier> ImportCapModifierLookup;
        public ComponentLookup<Game.Net.OutsideConnection> OutsideConnectionLookup;
        public ComponentLookup<ElectricityBuildingConnection> ConnectionLookup;
        public ComponentLookup<ElectricityFlowEdge> FlowEdgeLookup;
        public ComponentLookup<ElectricityNodeConnection> NodeConnectionLookup;
        public ComponentLookup<Game.Common.Owner> OwnerLookup;
        public BufferLookup<ConnectedFlowEdge> FlowConnectionLookup;
        /// <summary>ElectricityFlowSystem.sinkNode — the end node of every export trade edge.</summary>
        public Entity ElectricitySinkNode;
        public ComponentLookup<PrefabRef> PrefabRefLookup;
        public BufferLookup<SubObject> SubObjectLookup;
        public BufferLookup<InstalledUpgrade> InstalledUpgradeLookup;
        // NOTE: the Efficiency buffer is deliberately NOT part of this context. The resolver's
        // slot-26 write lives inside PlantResolveJob (RW BufferLookup<Efficiency> on the job),
        // ordered by the job graph — main-thread context access would need the retired
        // CompleteDependencyBeforeRW<Efficiency> fence drain back.
        public EntityStorageInfoLookup EntityStorageInfoLookup;
        public ComponentLookup<PowerPlantData> PowerPlantDataLookup;
        public ComponentLookup<EmergencyGeneratorData> EmergencyGeneratorDataLookup;
        public ComponentLookup<BatteryData> BatteryDataLookup;
        public ComponentLookup<WindPoweredData> WindPoweredDataLookup;
        public ComponentLookup<SolarPoweredData> SolarPoweredDataLookup;
        public ComponentLookup<GarbagePoweredData> GarbagePoweredDataLookup;
        public ComponentLookup<WaterPoweredData> WaterPoweredDataLookup;
        /// <summary>
        /// Runtime hydro component on the building (not the prefab). Vanilla
        /// <c>PowerPlantAISystem.GetWaterCapacity</c> uses <c>m_Length × m_Height</c>
        /// clamped to 1_000_000 m² before multiplying by
        /// <see cref="WaterPoweredData"/>.m_CapacityFactor. Reading the prefab data
        /// alone (without the runtime <c>WaterPowered</c>) over-estimates nameplate
        /// for every dam smaller than the clamp ceiling.
        /// </summary>
        public ComponentLookup<Game.Buildings.WaterPowered> WaterPoweredLookup;
        public ComponentLookup<GroundWaterPoweredData> GroundWaterPoweredDataLookup;
        public ComponentLookup<EquipmentWear> EquipmentWearLookup;
        public ComponentLookup<PowerPlantDamage> PowerPlantDamageLookup;
        public NativeArray<Entity> CachedResolvedPlantEntities;
        public bool HasCachedResolvedPlantEntities;

        public void EnsureEcb()
        {
            if (HasEcb)
                return;

            if (EcbFactory == null)
                throw new InvalidOperationException("PowerCapacityPipelineContext requires an ECB factory before structural writes.");

            Ecb = EcbFactory();
            HasEcb = true;
        }

        public NativeArray<Entity> GetResolvedPlantEntities()
        {
            if (!HasCachedResolvedPlantEntities)
            {
                CachedResolvedPlantEntities = ResolvedPlantQuery.ToEntityArray(Allocator.Temp);
                HasCachedResolvedPlantEntities = true;
            }

            return CachedResolvedPlantEntities;
        }

        public void DisposeCachedResolvedPlantEntities()
        {
            if (!HasCachedResolvedPlantEntities)
                return;

            if (CachedResolvedPlantEntities.IsCreated)
                CachedResolvedPlantEntities.Dispose();

            CachedResolvedPlantEntities = default;
            HasCachedResolvedPlantEntities = false;
        }
    }
}
