using System;
using Unity.Entities;
using Unity.Collections;
using Game.Buildings;
using Game.Common;
using CivicSurvival.Core.Components.Domain.Economy;
using CivicSurvival.Core.Components.Domain.Engineering;
using CivicSurvival.Core.Components.Domain.Power;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Components.Requests;
using CivicSurvival.Core.Components.Threats;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Events;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Interfaces.Domain.Engineering;
using CivicSurvival.Core.Interfaces.Domain.Power;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Utils;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.UI.DomainState;

namespace CivicSurvival.Domains.Engineering.Services
{
    /// <summary>
    /// Context data needed for plant repair operations.
    /// Passed to PlantRepairService methods to avoid tight coupling.
    /// </summary>
    public struct PlantRepairContext
    {
        public float GameHour;
        public World World;
        public IEventBus? EventBus;
        [NonEntityIndex] public NativeHashMap<int, Entity> PlantIdToEntity;
        public ComponentLookup<EquipmentWear> WearLookup;
        public ComponentLookup<ElectricityProducer> ProducerLookup;
        public ComponentLookup<PlantBaseCapacity> BaseCapacityLookup;
        public EntityStorageInfoLookup StorageInfoLookup;
        public ComponentLookup<Deleted> DeletedLookup;
        public ComponentLookup<Destroyed> DestroyedLookup;
        /// <summary>
        /// Durable repair sinks (W2 row 3). CompleteRepair mutates the PERSISTED
        /// damage sidecars at the transaction point so a save taken this frame
        /// survives load already-repaired. Null when the owner feature is closed.
        /// </summary>
        public IOperationalDamageRepairSink? OperationalDamageRepairSink;
        public IDisasterRepairSink? DisasterRepairSink;
        public IPowerCapacitySnapshotReader? PowerCapacitySnapshotReader;
        /// <summary>
        /// ECB for structural changes.
        /// </summary>
        public EntityCommandBuffer Ecb;
        /// <summary>
        /// FIX S18-05: Current wave phase — block repair during Attack/Alert.
        /// </summary>
        public GamePhase CurrentPhase;
    }

    /// <summary>
    /// Static helper service for plant repair operations.
    /// Repair-transaction helper shared between PlantRepairRequestProcessor
    /// (StartRepair on schedule, ApplyRepairToEntity on resolved-budget drain)
    /// and PlantWearSimulation (CompleteRepair on expired-repair completion).
    ///
    /// Repair types:
    /// - Municipal: 24h duration, paid from City Budget
    /// - MunicipalWithKickback: Same as Municipal + corruption kickback
    /// - ShadowOps: 2h duration, paid from Shadow Cash
    /// </summary>
    [SingletonOwner(typeof(EquipmentWear))]
    [OwnedSingletonLifecycle(
        Persisted = true,
        EnsurePhase = SingletonLifecyclePhase.ReconcileAfterLoad,
        DisposePhase = SingletonLifecyclePhase.None,
        AllowAsymmetry = true,
        Justification = "Static repair helper is the transaction co-owner for EquipmentWear; PlantWearSimulation owns ECS reconciliation and persistence, PlantRepairRequestProcessor owns pending-set bookkeeping, PlantRepairCommitSystem owns the ModificationEnd apply path. All callers route writes through this helper.")]
    public static class PlantRepairService
    {
        private static readonly LogContext Log = new("PlantRepairService");

        /// <summary>
        /// Find a plant entity by its stable ID.
        /// PERF: O(1) lookup via NativeHashMap.
        /// </summary>
        public static (Entity entity, EquipmentWear wear) FindPlantByStableId(
            ref PlantRepairContext ctx,
            int plantId)
        {
            // Caller may pass an uncreated map: the owning system allocates its
            // NativeHashMap in OnCreate, so a context built before that (or after
            // teardown) leaves it default. TryGetValue on a default NativeHashMap throws.
            if (!ctx.PlantIdToEntity.IsCreated)
                return (Entity.Null, default);

            if (ctx.PlantIdToEntity.TryGetValue(plantId, out Entity entity))
            {
                if (IsLiveWearEntity(ref ctx, entity) && ctx.WearLookup.TryGetComponent(entity, out var wear)
                    && IsLiveBuilding(ref ctx, wear.GetBuildingEntity()))
                {
                    return (entity, wear);
                }
            }
            return (Entity.Null, default);
        }

        /// <summary>
        /// Diagnostic breakdown of WHY <see cref="FindPlantByStableId"/> resolved to
        /// Entity.Null. Cold path only (repair rejection logging). Names the exact failing
        /// branch so a UI_PLANT_REPAIR_NOT_FOUND line is classifiable without guessing:
        /// mapHit=false → id absent from the StablePlantId map; exists=false → stale/churned
        /// building ref (post-load no-rebind / duplicate sidecar); destroyed=true → vanilla
        /// ruin. Reuses ctx lookups only — no query scan, no sync point.
        /// </summary>
        public static string DiagnoseResolve(ref PlantRepairContext ctx, int plantId)
        {
            if (!ctx.PlantIdToEntity.IsCreated)
                return "mapHit=false(map-uncreated)";
            if (!ctx.PlantIdToEntity.TryGetValue(plantId, out Entity wearEntity))
                return "mapHit=false";

            bool wearLive = IsLiveWearEntity(ref ctx, wearEntity);
            if (!ctx.WearLookup.TryGetComponent(wearEntity, out var wear))
                return $"mapHit=true wear={wearEntity.Index}:{wearEntity.Version} wearLive={wearLive} hasWear=false";

            var b = wear.GetBuildingEntity();
            bool exists = b != Entity.Null && ctx.StorageInfoLookup.Exists(b);
            bool deleted = exists && ctx.DeletedLookup.HasComponent(b);
            bool destroyed = exists && ctx.DestroyedLookup.HasComponent(b);
            return $"mapHit=true wear={wearEntity.Index}:{wearEntity.Version} wearLive={wearLive} "
                + $"building={b.Index}:{b.Version} exists={exists} deleted={deleted} destroyed={destroyed}";
        }

        /// <summary>
        /// Calculate repair cost for a given plant and repair type.
        /// Used by UI to show costs before starting repair.
        /// </summary>
        public static (int cost, int kickback) CalculateRepairCost(
            ref PlantRepairContext ctx,
            int plantId,
            RepairType repairType)
        {
            var (entity, wear) = FindPlantByStableId(ref ctx, plantId);
            if (entity == Entity.Null)
                return (0, 0);
            int wearPercent = GetBillableRepairPercent(ref ctx, wear);

            var repairParams = RepairPaymentHelper.CalculateRepairParams(wearPercent, repairType);
            return (repairParams.Cost, repairParams.Kickback);
        }

        /// <summary>
        /// Apply repair state to entity: set sidecar repair state and schedule repair completion.
        /// PowerCapacityPipeline hydrates EquipmentWearModifier.IsUnderRepair.
        /// </summary>
        public static bool ApplyRepairToEntity(
            ref PlantRepairContext ctx,
            Entity entity,
            ref EquipmentWear wear,
            float durationHours)
        {
            if (!GameDurationHours.TryCreate(durationHours, out var repairDuration, RepairTransactionIntent.MaxDurationHours))
            {
                Log.Warn($"ApplyRepair: invalid repair duration {durationHours}, rejected");
                return false;
            }

            // Set repair end time
            wear.RepairEndHour = ctx.GameHour + repairDuration.Value;

            // ProducerLookup and ModifiersLookup are on vanilla BUILDING, not mod entity
            var buildingEntity = wear.GetBuildingEntity();
            if (!IsLiveWearEntity(ref ctx, entity) || !IsLiveBuilding(ref ctx, buildingEntity))
            {
                Log.Warn($"ApplyRepair: stale plant entity {entity.Index}:{entity.Version} rejected");
                return false;
            }

            if (!CanApplyRepairState(ref ctx, buildingEntity))
            {
                Log.Warn($"ApplyRepair: building {buildingEntity.Index}:{buildingEntity.Version} missing repair modifier/base-capacity components");
                return false;
            }

            // Capture original capacity if not set (prefer PlantBaseCapacity over degraded m_Capacity)
            if (wear.OriginalCapacity <= 0)
            {
                if (ctx.BaseCapacityLookup.TryGetComponent(buildingEntity, out var baseCap) && baseCap.OriginalCapacity > 0)
                    wear.OriginalCapacity = baseCap.OriginalCapacity;
                else if (ctx.ProducerLookup.TryGetComponent(buildingEntity, out var producer))
                {
                    Log.Warn($"ApplyRepair: PlantBaseCapacity missing for {buildingEntity.Index}, using current m_Capacity={producer.m_Capacity} (may be degraded)");
                    wear.OriginalCapacity = producer.m_Capacity;
                }
            }

            // Direct write OK: EquipmentWear is custom component on mod entity
#pragma warning disable CIVIC035 // Entity guaranteed to have component (wear is ref param from caller)
            ctx.WearLookup[entity] = wear;
#pragma warning restore CIVIC035
            return true;
        }

        /// <summary>
        /// Complete repair: clear sidecar repair state and persisted damage sources.
        /// PowerCapacityPipeline will restore capacity from OriginalCapacity.
        /// </summary>
        public static void CompleteRepair(ref PlantRepairContext ctx, Entity entity)
        {
            if (!IsLiveWearEntity(ref ctx, entity) || !ctx.WearLookup.TryGetComponent(entity, out var wear))
                return;

            // ModifiersLookup is on vanilla BUILDING, not mod entity
            var buildingEntity = wear.GetBuildingEntity();
            if (!IsLiveBuilding(ref ctx, buildingEntity))
            {
                Log.Warn($"CompleteRepair: stale building {buildingEntity.Index}:{buildingEntity.Version} rejected");
                return;
            }
            // FIX S18-02: Clear ALL damage types — player paid for full repair, plant should return to 100%.
            // T2-2 FIX was overly conservative: leaving missile/disaster damage after repair confuses players
            // because no UI breakdown shows why capacity is still reduced.
            //
            // C-3: record which damage classes were actually present (and cleared) so the
            // RepairCompletedEvent consumers act only on their own class. Capture each
            // value BEFORE it is zeroed below. Same source as GetBillableRepairPercent,
            // so the cancelled lifecycle matches what the player was billed for.
            var causeMask = RepairCauseMask.None;
            if (wear.WearPercent > 0f || wear.HasExploded)
                causeMask |= RepairCauseMask.Wear;

            if (TryGetCapacitySnapshot(ref ctx, buildingEntity, out var capacitySnapshot))
            {
                if (capacitySnapshot.ExplosionDamagePercent > 0f)
                    causeMask |= RepairCauseMask.Wear;
                if (capacitySnapshot.OperationalDamagePercent > 0f)
                    causeMask |= RepairCauseMask.Operational;
                if (capacitySnapshot.DisasterDamagePercent > 0f)
                    causeMask |= RepairCauseMask.Disaster;
            }

            // Reset wear and clear repair
            wear.WearPercent = 0f;
            // ORDER-INVARIANT: DamageAccountingSystem owns settling
            // ExplosionChargeSettled. Keep HasExploded alive while an explosion
            // charge is still owed so a save/load before the budget result can
            // re-issue from the durable marker.
            if (!wear.HasExploded || wear.ExplosionChargeSettled)
                wear.HasExploded = false;
            wear.OverloadHours = 0f;
            wear.LastMaintenanceHour = ctx.GameHour;
            wear.RepairEndHour = 0f;
            unchecked { wear.RepairEpoch++; }
            // Direct write OK: EquipmentWear is custom component on mod entity; TryGetComponent at line 222 guarantees component exists
#pragma warning disable CIVIC035 // Entity guaranteed to have component (wear is ref param from caller)
            ctx.WearLookup[entity] = wear;
#pragma warning restore CIVIC035

            // W2 row 3 root fix: durable mutation at the transaction point. The
            // owning systems zero/stamp their PERSISTED sidecars synchronously so
            // a save taken this frame survives load already-repaired — the repair
            // no longer depends on a transient event surviving save/load.
            if ((causeMask & RepairCauseMask.Operational) != 0)
                ctx.OperationalDamageRepairSink?.ClearRepairedOperationalDamage(wear.Building, ctx.GameHour);
            if ((causeMask & RepairCauseMask.Disaster) != 0)
                ctx.DisasterRepairSink?.ClearRepairedDisaster(wear.Building, ctx.GameHour);

            // RepairCompletedEvent is now only an in-session structural-cleanup
            // optimisation (delete the zeroed sidecar this session) + DamageAccounting
            // hook — NO LONGER load-bearing. OperationalDamageSystem and
            // PowerPlantDisasterSystem own their sidecar entities.
            var repairEvent = ctx.Ecb.CreateEntity();
            ctx.Ecb.AddComponent(repairEvent, new RepairCompletedEvent
            {
                Building = wear.Building,
                RepairCompletedGameHour = ctx.GameHour,
                CauseMask = causeMask
            });

            int restoredCapacity = ctx.BaseCapacityLookup.TryGetComponent(buildingEntity, out var baseCap) ? baseCap.OriginalCapacity : 0;
            Log.Info($"Repair completed on entity {entity.Index}, capacity restored to {restoredCapacity / 1000} MW");
        }

        private static int GetBillableRepairPercent(ref PlantRepairContext ctx, EquipmentWear wear)
        {
            float explosionPercent = wear.HasExploded ? wear.SavedExplosionDamage : 0f;
            float operationalPercent = 0f;
            float disasterPercent = 0f;

            if (TryGetCapacitySnapshot(ref ctx, wear.GetBuildingEntity(), out var snapshot))
            {
                explosionPercent = Math.Max(explosionPercent, snapshot.ExplosionDamagePercent);
                operationalPercent = snapshot.OperationalDamagePercent;
                disasterPercent = snapshot.DisasterDamagePercent;
            }

            return RepairPaymentHelper.BillableRepairPercent(
                wear.WearPercent, explosionPercent, operationalPercent, disasterPercent);
        }

        private static bool CanApplyRepairState(ref PlantRepairContext ctx, Entity buildingEntity)
        {
            if (!IsLiveBuilding(ref ctx, buildingEntity))
                return false;

            return ctx.BaseCapacityLookup.HasComponent(buildingEntity) ||
                   ctx.ProducerLookup.HasComponent(buildingEntity);
        }

        private static bool TryGetCapacitySnapshot(
            ref PlantRepairContext ctx,
            Entity buildingEntity,
            out PowerCapacityPlantSnapshot plantSnapshot)
        {
            plantSnapshot = default;
            if (ctx.PowerCapacitySnapshotReader == null
                || !ctx.PowerCapacitySnapshotReader.TryGetSnapshot(out var snapshot))
            {
                return false;
            }

            var plants = snapshot.Plants;
            for (int i = 0; i < plants.Count; i++)
            {
                if (plants[i].Plant == buildingEntity)
                {
                    plantSnapshot = plants[i];
                    return true;
                }
            }

            return false;
        }

        private static bool IsLiveWearEntity(ref PlantRepairContext ctx, Entity entity)
        {
            if (entity == Entity.Null || !ctx.StorageInfoLookup.Exists(entity))
                return false;
            return !ctx.DeletedLookup.HasComponent(entity) && !ctx.DestroyedLookup.HasComponent(entity);
        }

        private static bool IsLiveBuilding(ref PlantRepairContext ctx, Entity buildingEntity)
        {
            if (buildingEntity == Entity.Null || !ctx.StorageInfoLookup.Exists(buildingEntity))
                return false;
            return !ctx.DeletedLookup.HasComponent(buildingEntity) && !ctx.DestroyedLookup.HasComponent(buildingEntity);
        }
    }
}
