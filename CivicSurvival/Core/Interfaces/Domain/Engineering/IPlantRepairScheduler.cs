using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Components.Requests;
using CivicSurvival.Core.Infrastructure;

namespace CivicSurvival.Core.Interfaces.Domain.Engineering
{
    /// <summary>
    /// Schedules power plant repair payment and intent creation.
    /// Implemented by PlantRepairIntakeSystem. The active UI path is consumed in
    /// ModificationEnd; this service remains for programmatic repair scheduling.
    ///
    /// The implementation creates its own EntityCommandBuffer from the owner's
    /// ModificationEnd barrier — callers cannot supply one, because the pause-safe
    /// pipeline pins repair intent creation to that barrier specifically.
    ///
    /// Tuple return replaces the prior <c>out string reasonId</c> contract so
    /// NullObjectGenerator (which rejects out/ref parameters via CIVIC420) can
    /// emit a null-object. When the owner feature (Engineering) is open in
    /// wave 1 and the service is missing, <see cref="ServiceRegistryFeatureExtensions.TryGetOrNullObject"/>
    /// throws — surfacing the bug. When the owner feature is closed (wave 2+
    /// option), the generated null-object's <c>default((RequestStatus, string))</c>
    /// return tells the consumer the request was not applied.
    /// </summary>
    [GenerateNullObject]
    [OwnedByFeatureId(FeatureIds.EngineeringName)]
    public interface IPlantRepairScheduler
    {
        (RequestStatus status, string reasonId) ScheduleRepair(
            int stablePlantId,
            RepairType repairType,
            RequestMeta requestMeta);
    }
}
