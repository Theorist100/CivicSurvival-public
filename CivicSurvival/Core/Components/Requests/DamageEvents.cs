using CivicSurvival.Core.Types;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Features.CrossDomain.DamageAccounting;
using Colossal.Serialization.Entities;
using Unity.Entities;

namespace CivicSurvival.Core.Components.Requests
{
    /// <summary>
    /// Damage source classification for financial tracking.
    /// </summary>
    public enum DamageType : byte
    {
        /// <summary>Missile strike operational damage (via ThreatArrivalSystem).</summary>
        Operational = 0,

        /// <summary>Random plant disaster (via PowerPlantDisasterSystem).</summary>
        Disaster = 1,

        /// <summary>Equipment wear explosion (via PlantExplosionService).</summary>
        Explosion = 2,

        /// <summary>Counterfeit battery fire (via CounterfeitBatteryFireSystem).</summary>
        Fire = 3
    }

    /// <summary>
    /// Which damage classes a completed repair actually addressed. Bitmask so a
    /// single full repair (which clears wear + operational + disaster together,
    /// see PlantRepairService.CompleteRepair "FIX S18-02") tells each consumer
    /// whether the repair was paid to fix <em>its</em> damage class. Without it,
    /// any paid wear repair would force-cancel an unrelated active disaster
    /// lifecycle for free (Cluster C / C-3).
    /// </summary>
    [System.Flags]
    public enum RepairCauseMask : byte
    {
        None = 0,

        /// <summary>Equipment wear / explosion damage was present and cleared.</summary>
        Wear = 1,

        /// <summary>Missile operational damage was present and cleared.</summary>
        Operational = 2,

        /// <summary>Plant disaster damage was present and cleared.</summary>
        Disaster = 4
    }

    /// <summary>
    /// Ephemeral entity event: damage was applied to a power plant.
    /// Spawned by damage producers via ECB, consumed by DamageAccountingSystem.
    ///
    /// Producers: OperationalDamageSystem, CounterfeitBatteryFireSystem,
    ///            PlantExplosionService, PowerPlantDisasterSystem
    /// Consumer: DamageAccountingSystem (debriefing accumulation + payment with debt fallback)
    ///
    /// One-frame lifespan: created via ECB → consumed + destroyed next frame.
    ///
    /// Transient by design: this is a one-frame signal, not durable state (the
    /// authoritative damage lives in PowerPlantDamage/EquipmentWear and the
    /// accumulated debrief total in DebriefingInfraStats, all persisted on their
    /// own). It MUST NOT roundtrip through a save: a persisted instance would be
    /// re-charged or re-accumulated on load. <see cref="IEmptySerializable"/> is
    /// the Colossal-sanctioned save-safe contract for that — no persisted payload;
    /// stale instances are purged on load by DamageAccountingSystem.PurgeAfterLoad
    /// (SAVE_LOAD_LIFECYCLE_DOCTRINE Invariant 3; the purge is the Invariant 5
    /// "Phase split" PURGE pass — runs strictly after every reconcile pass),
    /// exactly like InterceptRequest.
    /// </summary>
    [RequestPersistence(RequestPersistenceKind.TransientInput, typeof(DamageAccountingSystem))]
    public struct DamageAppliedEvent : IComponentData, ICommandRequest, IEmptySerializable
    {
        /// <summary>Damage source classification.</summary>
        public DamageType Type;

        /// <summary>Vanilla building reference (typed Index+Version).</summary>
        public BuildingRef Building;

        /// <summary>Damage severity as fraction [0..1].</summary>
        public float DamagePercent;

        /// <summary>Estimated repair cost in $. 0 = free damage (disaster auto-repair).</summary>
        public long EstimatedRepairCost;

        /// <summary>True = accumulate in DebriefingInfraStats (wave damage). False = immediate payment.</summary>
        public bool IsWaveDamage;

        public void SetDefaults() => this = default;
    }

    /// <summary>
    /// Ephemeral entity event: repair was completed on a power plant.
    /// Spawned by PlantRepairService via ECB, consumed by damage systems for cleanup.
    ///
    /// Producer: PlantRepairService.CompleteRepair()
    /// Consumers: OperationalDamageSystem (delete PowerPlantDamage mod entity),
    ///            PowerPlantDisasterSystem (cancel DisabledByDisaster),
    ///            DamageAccountingSystem (deterministic owner — destroys the event
    ///            after all consumers ran, RegisterAfter(PowerCapacityWriterGroup)])
    ///
    /// One-frame lifespan: created via ECB → consumed by the damage systems →
    /// destroyed by DamageAccountingSystem the same frame.
    ///
    /// Transient by design — <see cref="IEmptySerializable"/>, no persisted
    /// payload, stale instances purged on load by
    /// DamageAccountingSystem.PurgeAfterLoad (the PURGE pass — runs strictly
    /// after every validator's reconcile pass; Invariant 5 "Phase split").
    /// This contract is now TRUTHFUL
    /// (W2 row 3 fix): durable repair state is applied at the transaction point
    /// in PlantRepairService.CompleteRepair via IOperationalDamageRepairSink /
    /// IDisasterRepairSink, which mutate the PERSISTED PowerPlantDamage /
    /// DisabledByDisaster sidecars synchronously. This event is therefore NO
    /// LONGER load-bearing — it only drives same-session structural cleanup of
    /// the now-repaired sidecar + the DamageAccounting hook. Losing it across
    /// save/load is harmless because the persisted sidecars already encode the
    /// repair (unlike before, where a save in the in-flight window lost the
    /// paid repair entirely).
    /// </summary>
    [RequestPersistence(RequestPersistenceKind.TransientInput, typeof(DamageAccountingSystem))]
    public struct RepairCompletedEvent : IComponentData, ICommandRequest, IEmptySerializable
    {
        /// <summary>Vanilla building reference (typed Index+Version).</summary>
        public BuildingRef Building;

        /// <summary>Game hour when repair completed. M05 fix: used to detect post-repair missile damage.</summary>
        public double RepairCompletedGameHour;

        /// <summary>
        /// Which damage classes this repair actually cleared (set in
        /// PlantRepairService.CompleteRepair from the modifiers present at
        /// completion). Consumers act only on their own class: a wear-only
        /// repair must not cancel an unrelated active disaster (C-3).
        /// </summary>
        public RepairCauseMask CauseMask;

        public void SetDefaults() => this = default;
    }
}
