using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Interfaces.Domain.Engineering
{
    /// <summary>
    /// Durable repair sink for power-plant disaster damage.
    /// Implemented by PowerPlantDisasterSystem (owner of the persisted
    /// <c>DisabledByDisaster</c> sidecar), invoked synchronously from
    /// <c>PlantRepairService.CompleteRepair</c> at the repair transaction point.
    ///
    /// W2 row 3 root fix: the paid repair stamps a persisted
    /// <c>RepairedThroughHour</c> on the disaster record the instant payment
    /// completes, so a save taken in the same frame survives load with the
    /// disaster cancelled — independent of the transient RepairCompletedEvent.
    ///
    /// Same-domain consumer (PlantRepairService in Engineering): historically
    /// uses <c>Get&lt;T&gt;()</c> rather than <c>Require&lt;T&gt;()</c>. The
    /// generated null-object lets the call site stay defensive (closed Engineering
    /// is a hypothetical never-fired path that the analyzer still requires us to
    /// classify; in practice Engineering is AlwaysOpen).
    /// </summary>
    [OwnedByFeatureId(FeatureIds.EngineeringName)]
    [GenerateNullObject]
    public interface IDisasterRepairSink
    {
        /// <summary>
        /// Mark the persisted DisabledByDisaster record for
        /// <paramref name="building"/> repaired through
        /// <paramref name="repairGameHour"/>. Idempotent. A disaster created
        /// after the billed repair is left untouched.
        /// </summary>
        void ClearRepairedDisaster(BuildingRef building, double repairGameHour);
    }
}
