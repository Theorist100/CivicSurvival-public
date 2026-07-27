using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Interfaces.Domain.Engineering
{
    /// <summary>
    /// Durable repair sink for operational (missile) plant damage.
    /// Implemented by OperationalDamageSystem (owner of the persisted
    /// <c>PowerPlantDamage</c> sidecar), invoked synchronously from
    /// <c>PlantRepairService.CompleteRepair</c> at the repair transaction point.
    ///
    /// W2 row 3 root fix: the paid repair must mutate the PERSISTED sidecar
    /// the instant payment completes, so a save taken in the same frame survives
    /// load already-repaired. The transient <c>RepairCompletedEvent</c> is no
    /// longer load-bearing — it only drives same-session structural cleanup.
    ///
    /// Cross-domain consumer (PlantRepairService in Engineering): when
    /// ThreatDamage feature is closed, repair calls silently drop via the
    /// generated null-object — no damage to clear.
    /// </summary>
    [OwnedByFeatureId(FeatureIds.ThreatDamageName)]
    [GenerateNullObject]
    public interface IOperationalDamageRepairSink
    {
        /// <summary>
        /// Zero the persisted PowerPlantDamage record for <paramref name="building"/>.
        /// Idempotent. Honours BuildingRef.Version and the M05 guard
        /// (post-repair missile damage at/after <paramref name="repairGameHour"/>
        /// is preserved).
        /// </summary>
        void ClearRepairedOperationalDamage(BuildingRef building, double repairGameHour);
    }
}
