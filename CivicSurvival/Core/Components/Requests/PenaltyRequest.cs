using CivicSurvival.Core.Components.Lifecycle;
using Unity.Entities;
using CivicSurvival.Core.Types;

using CivicSurvival.Core.Features.Wellbeing;
namespace CivicSurvival.Core.Components.Requests
{
    /// <summary>
    /// Singleton marker for PenaltyRequest buffer.
    /// Created by DistrictPenaltySystem.
    /// </summary>
    public struct PenaltyRequestSingleton : IComponentData
    {
        public static void EnsureExists(EntityManager em)
        {
            CivicSingleton.Ensure(em, default(PenaltyRequestSingleton), new EnsureSingletonPolicy<PenaltyRequestSingleton>
            {
                EnsureShape = EnsureShape
            });
        }

        private static void EnsureShape(EntityManager em, Entity entity)
        {
            if (!em.HasBuffer<PenaltyRequest>(entity))
                em.AddBuffer<PenaltyRequest>(entity);
        }
    }

    /// <summary>
    /// Request to register or remove a district penalty.
    /// Buffer element pattern - multiple requests can accumulate per frame.
    ///
    /// Producers: BlackoutSystem, BuckwheatSystem, and systems using DistrictPenaltySystem direct API.
    /// Consumer: DistrictPenaltySystem. Direct API calls drain this buffer before mutating state,
    /// so buffered and direct writes share one ordering point.
    ///
    /// Usage:
    /// <code>
    /// // Get buffer
    /// if (!m_PenaltyRequestQuery.TryGetSingletonBuffer&lt;PenaltyRequest&gt;(out var buffer))
    ///     return;
    ///
    /// // Add request
    /// buffer.Add(new PenaltyRequest
    /// {
    ///     DistrictIndex = districtIndex,
    ///     Source = PenaltySource.Blackout,
    ///     IsRemoval = false
    /// });
    /// </code>
    /// </summary>
#pragma warning disable CIVIC127 // Rare requests (1-3 per event), heap allocation acceptable
    [InternalBufferCapacity(128)]
    public struct PenaltyRequest : IBufferElementData
#pragma warning restore CIVIC127
    {
        /// <summary>District index to apply/remove penalty.</summary>
#pragma warning disable CIVIC262 // Not an entity ref — vanilla district buffer position
        public int DistrictIndex;
#pragma warning restore CIVIC262

        /// <summary>Source of the penalty.</summary>
        public PenaltySource Source;

        /// <summary>True = remove penalty, False = add penalty.</summary>
        public bool IsRemoval;
    }
}
