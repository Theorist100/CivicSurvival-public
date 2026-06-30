using Unity.Entities;

namespace CivicSurvival.Core.Systems.Scheduling
{
    /// <summary>
    /// Registration-order marker after generator-efficiency clear and before per-domain writers.
    /// </summary>
    public partial class GeneratorEfficiencyClearReadyMarker : SystemBase
    {
        protected override void OnUpdate()
        {
        }
    }

    /// <summary>
    /// Registration-order marker after generator-efficiency clear/write/aggregate.
    /// </summary>
    public partial class GeneratorEfficiencyReadyMarker : SystemBase
    {
        protected override void OnUpdate()
        {
        }
    }
}
