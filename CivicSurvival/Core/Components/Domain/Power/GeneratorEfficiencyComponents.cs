using Unity.Entities;
using Unity.Collections;

namespace CivicSurvival.Core.Components.Domain.Power
{
    /// <summary>
    /// Aggregated generator efficiency multiplier.
    /// 1.0 = baseline, >1.0 increases fuel consumption, <1.0 improves efficiency.
    /// </summary>
    public struct GeneratorEfficiency : IComponentData
    {
        /// <summary>Aggregated efficiency multiplier (1.0 = baseline).</summary>
        public float Value;
    }

    /// <summary>
    /// Source modifier for generator efficiency.
    /// Producers append their multiplier each update.
    /// </summary>
    [InternalBufferCapacity(16)]
    public struct GeneratorEfficiencySource : IBufferElementData
    {
        /// <summary>Unique identifier for this efficiency source.</summary>
        public FixedString32Bytes SourceId;

        /// <summary>Efficiency multiplier from this source.</summary>
        public float Multiplier;
    }
}
