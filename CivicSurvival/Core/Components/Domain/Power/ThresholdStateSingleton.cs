using CivicSurvival.Core.Components.Lifecycle;
using Unity.Entities;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Components.Domain.Power
{
    /// <summary>
    /// Threshold operation state as ECS singleton.
    /// Threshold cuts off buildings that receive less than 90% power.
    ///
    /// Access: SystemAPI.GetSingleton&lt;ThresholdStateSingleton&gt;()
    /// Modify: SystemAPI.GetSingletonRW&lt;ThresholdStateSingleton&gt;()
    ///
    /// Writer: ThresholdOperationSystem
    /// Readers: PowerGridUIPanel, DistrictUIPanel
    ///
    /// For per-district data, query ThresholdCutBuffer on this singleton entity.
    /// </summary>
    public struct ThresholdStateSingleton : IComponentData
    {
        /// <summary>True if any buildings are currently cut off by threshold.</summary>
        public bool IsActive;

        /// <summary>Current count of buildings cut off by threshold.</summary>
        public int CutoffCount;

        /// <summary>Total kW lost to threshold cuts in last update window.
        /// Computed as the sum of m_FulfilledConsumption captured before zeroing in ThresholdOperationJob.
        /// UI uses this to compute Delivered = Consumption − CutoffKW.</summary>
        public int CutoffKW;

        public static ThresholdStateSingleton Default => new()
        {
            IsActive = false,
            CutoffCount = 0,
            CutoffKW = 0
        };

        public static void EnsureExists(EntityManager em)
        {
            CivicSingleton.Ensure(em, Default, new EnsureSingletonPolicy<ThresholdStateSingleton>
            {
                EnsureShape = EnsureShape
            });
        }

        private static void EnsureShape(EntityManager em, Entity entity)
        {
            if (!em.HasBuffer<ThresholdCutBuffer>(entity))
                em.AddBuffer<ThresholdCutBuffer>(entity);
        }
    }

    /// <summary>
    /// Buffer element for per-district threshold cut counts.
    /// Attached to ThresholdStateSingleton entity.
    /// </summary>
    [InternalBufferCapacity(16)]
    public struct ThresholdCutBuffer : IBufferElementData
    {
        public DistrictRef District;
        public int CutCount;
        public int CutKW;
    }
}
