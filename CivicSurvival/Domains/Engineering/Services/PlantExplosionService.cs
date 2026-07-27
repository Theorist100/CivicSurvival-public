using System;
using Game.Buildings;
using Game.Common;
using Game.Events;
using Game.Prefabs;
using Unity.Entities;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Components.Domain.Economy;
using CivicSurvival.Core.Components.Domain.Power;
using CivicSurvival.Core.Components.Requests;
using CivicSurvival.Core.Components.Threats;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Events;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Domains.Engineering.Services
{
    /// <summary>
    /// Stateless context bundle for <see cref="PlantExplosionService.Trigger"/>.
    /// All lookups must be refreshed by the caller before invocation; the service
    /// itself only reads them.
    /// </summary>
    public struct PlantExplosionContext
    {
        public World World;
        public EntityCommandBuffer Ecb;
        public IEventBus? EventBus;
        public PrefabSystem PrefabSystem;
        // Shared cross-system Ignite/Destroy dedup. Replaces the per-system
        // NativeHashSet<Entity> IgniteQueuedThisFrame each producer used to carry.
        public IFrameMutationDedup FrameMutationDedup;
        public ComponentLookup<EquipmentWear> WearLookup;
        public ComponentLookup<PlantBaseCapacity> BaseCapacityLookup;
        public ComponentLookup<PrefabRef> PrefabRefLookup;
        public ComponentLookup<OnFire> OnFireLookup;
        public ComponentLookup<Deleted> DeletedLookup;
        public ComponentLookup<Destroyed> DestroyedLookup;
        public EntityStorageInfoLookup StorageInfoLookup;
        /// <summary>
        /// True when the building already carries an active <see cref="CollapsedProducer"/>
        /// mod entity. The caller computes this (PlantWearSimulation owns the
        /// CollapsedProducer query) and passes the result so the service stays
        /// stateless and SystemAPI-free.
        /// </summary>
        public bool OnCollapsedProducer;
    }

    /// <summary>
    /// Static helper that applies the side effects of an equipment-wear
    /// explosion: durable damage stamp on the <see cref="EquipmentWear"/>
    /// sidecar, charge-owed marker, transient <see cref="DamageAppliedEvent"/>,
    /// and (if the plant has not already collapsed) a vanilla
    /// <see cref="Game.Events.Ignite"/> event routed through the shared
    /// <see cref="BuildingDamageHelper"/>.
    ///
    /// Ownership of <see cref="EquipmentWear"/> explosion-state fields is split
    /// across the engineering writers — none of them is a single writer of the
    /// whole component, and the attribute graph reflects that:
    /// <list type="bullet">
    ///   <item><b>this service</b> writes <c>HasExploded</c>,
    ///   <c>SavedExplosionDamage</c>, <c>SavedExplosionRepairCost</c> when the
    ///   explosion fires, and OPENS the charge by writing
    ///   <c>ExplosionChargeSettled = false</c>.</item>
    ///   <item><see cref="Core.Features.CrossDomain.DamageAccounting.DamageAccountingSystem"/>
    ///   SETTLES the charge by writing <c>ExplosionChargeSettled = true</c>
    ///   after a successful bill or a zero-cost no-op reconcile (also a
    ///   <see cref="SingletonOwnerAttribute"/> co-owner; see W2 Invariant 5).</item>
    ///   <item><see cref="PlantRepairService.CompleteRepair"/> zeroes
    ///   <c>HasExploded</c>, <c>WearPercent</c>, <c>OverloadHours</c> and
    ///   stamps <c>LastMaintenanceHour</c>/<c>RepairEpoch</c> when a paid
    ///   repair finishes — it does NOT touch <c>ExplosionChargeSettled</c>
    ///   directly (the durable charge marker is owned by the open/settle
    ///   pair above).</item>
    /// </list>
    ///
    /// <see cref="SingletonOwnerAttribute"/> records this co-ownership the
    /// same way <see cref="PlantRepairService"/> does: the component lives in
    /// <c>Core.Components.Domain.Economy</c> for organisational reasons, but
    /// every writer of explosion-state fields is in Engineering. The attribute
    /// documents that fact to CIVIC212.
    /// </summary>
    [SingletonOwner(typeof(EquipmentWear))]
    [OwnedSingletonLifecycle(
        Persisted = true,
        EnsurePhase = SingletonLifecyclePhase.None,
        DisposePhase = SingletonLifecyclePhase.None,
        AllowAsymmetry = true,
        Justification = "Static explosion helper co-owns the explosion-state fields on EquipmentWear (HasExploded, SavedExplosionDamage, SavedExplosionRepairCost, ExplosionChargeSettled); PlantWearSimulation owns the sidecar entity lifecycle.")]
    public static class PlantExplosionService
    {
        private static readonly LogContext Log = new("PlantExplosionService");

        public const float ExplosionFireIntensity = 0.7f;

        /// <summary>
        /// Returns the number of ECB commands queued so the caller can keep its
        /// diagnostics counter up-to-date.
        /// </summary>
        public static int Trigger(ref PlantExplosionContext ctx, Entity wearEntity)
        {
            if (!ctx.WearLookup.TryGetComponent(wearEntity, out var wear))
                return 0;
            if (!IsLivePlantEntity(ref ctx, wearEntity, in wear))
                return 0;
            if (wear.HasExploded)
                return 0;

            var buildingEntity = wear.GetBuildingEntity();
            var wearCfg = BalanceConfig.Current.EquipmentWear;
            float explosionDamage = wearCfg.ExplosionDamage;
            int originalCapacity = ctx.BaseCapacityLookup.TryGetComponent(buildingEntity, out var baseCap)
                ? baseCap.OriginalCapacity
                : 0;
            int lostMW = (int)Math.Round(originalCapacity * explosionDamage) / 1000;

            // FIX S12-02: Always write damage even if collapsed. Old guard skipped the entire
            // explosion including ExplosionDamagePercent write — so when grid recovered, plant
            // returned at full capacity despite having exploded. Damage must persist.
            wear.HasExploded = true;
            wear.SavedExplosionDamage = explosionDamage;
            // F-16 (ACC-08): resolve the repair cost against live config NOW and
            // persist it. ReconcileUnsettledExplosionCharges / ReissueExplosionCharge
            // read this stored amount on load, so a RepairCostPerPercent tuned
            // between save and load cannot change what the player is charged for
            // the same logical explosion.
            long explosionRepairCost = (long)Math.Round(explosionDamage * 100) * wearCfg.RepairCostPerPercent;
            wear.SavedExplosionRepairCost = explosionRepairCost;
            // W2 Invariant 5: durable "charge owed" marker. DamageAccountingSystem
            // reconciles HasExploded && !ExplosionChargeSettled on load, so losing
            // the transient DamageAppliedEvent below no longer loses the charge.
            wear.ExplosionChargeSettled = false;
#pragma warning disable CIVIC035 // wearEntity from caller — guaranteed component (TryGetComponent guard above)
            ctx.WearLookup[wearEntity] = wear;
#pragma warning restore CIVIC035

            int ecbCount = 0;

            // FIX S5-05: Spawn DamageAppliedEvent for financial tracking
            // (in-session fast path; no longer load-bearing — see ExplosionChargeSettled)
            var damageEvent = ctx.Ecb.CreateEntity();
            ctx.Ecb.AddComponent(damageEvent, new DamageAppliedEvent
            {
                Type = DamageType.Explosion,
                Building = BuildingRef.FromEntity(buildingEntity),
                DamagePercent = explosionDamage,
                EstimatedRepairCost = explosionRepairCost,
                IsWaveDamage = false
            });
            ecbCount++;

            // FIX S12-02: Skip fire effect during collapse (plant already at 0, fire confuses player)
            if (ctx.OnCollapsedProducer)
            {
                Log.Info($"Plant {buildingEntity.Index} exploded during collapse — damage {explosionDamage:P0} recorded, fire skipped");
                return ecbCount;
            }

            // Apply OnFire + BatchesUpdated directly via BuildingDamageHelper rather
            // than constructing Ignite { m_Event = Entity.Null }. Intensity merge
            // against an existing vanilla Fire (m_Event != Entity.Null) preserves
            // that real event reference — vanilla escalation/spread continues
            // normally on top of a mod-explosion intensity boost.
            if (BuildingDamageHelper.TryApplyModFire(
                ctx.Ecb,
                buildingEntity,
                ctx.FrameMutationDedup,
                ctx.OnFireLookup,
                ctx.DestroyedLookup,
                ctx.DeletedLookup,
                ExplosionFireIntensity,
                allowExistingFire: true))
            {
                ecbCount++;
            }

            string plantName = ctx.PrefabRefLookup.TryGetComponent(buildingEntity, out var prefabRef)
                ? PowerPlantUtils.GetDisplayName(PowerPlantUtils.GetPlantType(ctx.PrefabSystem, prefabRef))
                : "Power Plant";

            Log.Warn($"EXPLOSION at {plantName}! Equipment wear: {wear.WearPercent:P0}, lost {lostMW} MW!");
            ctx.EventBus?.SafePublish(
                new InfraEvent(InfraEventType.EquipmentExplosion, BuildingIndex: buildingEntity.Index, WearPercent: wear.WearPercent),
                nameof(PlantExplosionService));

            return ecbCount;
        }

        private static bool IsLivePlantEntity(ref PlantExplosionContext ctx, Entity wearEntity, in EquipmentWear wear)
        {
            if (wearEntity == Entity.Null || !ctx.StorageInfoLookup.Exists(wearEntity))
                return false;
            if (ctx.DeletedLookup.HasComponent(wearEntity) || ctx.DestroyedLookup.HasComponent(wearEntity))
                return false;

            var buildingEntity = wear.GetBuildingEntity();
            if (buildingEntity == Entity.Null || !ctx.StorageInfoLookup.Exists(buildingEntity))
                return false;
            return !ctx.DeletedLookup.HasComponent(buildingEntity)
                && !ctx.DestroyedLookup.HasComponent(buildingEntity);
        }
    }
}
