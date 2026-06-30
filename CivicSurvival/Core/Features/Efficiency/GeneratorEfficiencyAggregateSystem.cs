using Game;
using Unity.Entities;
using Unity.Mathematics;
using CivicSurvival.Core.Components.Domain.Power;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Utils;
using CivicSurvival.Core.Attributes;

namespace CivicSurvival.Core.Features.Efficiency
{
    /// <summary>
    /// Aggregates generator efficiency modifiers into a single multiplier.
    /// Runs every tick — all ECS access via read-only lookups to avoid RW sync points.
    /// </summary>
    [ActIndependent]
    public partial class GeneratorEfficiencyAggregateSystem : ThrottledSystemBase
    {
        private static readonly LogContext Log = new("GeneratorEfficiencyAggregateSystem");

        private EntityQuery m_EfficiencyQuery;

        protected override int UpdateInterval => Engine.Timing.UPDATE_INTERVAL_1_SECOND;

        // S24-B1 FIX: Share throttle phase with ClearSystem so both fire on the same frame.
        // Without this, different type names → different phases → Clear fires without Aggregate (efficiency = 1.0).
        protected override string ThrottlePhaseKey => nameof(GeneratorEfficiencyClearSystem);

        // Read-only lookup (RO sync — only waits for write jobs, not all jobs)
        private BufferLookup<GeneratorEfficiencySource> m_SourceReadLookup;

        // Write lookup for IJob (eliminates SetComponent sync point)
        private ComponentLookup<GeneratorEfficiency> m_EffWriteLookup;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_EfficiencyQuery = GetEntityQuery(
                ComponentType.ReadWrite<GeneratorEfficiency>(),
                ComponentType.ReadOnly<GeneratorEfficiencySource>()
            );
            m_SourceReadLookup = GetBufferLookup<GeneratorEfficiencySource>(true);
            m_EffWriteLookup = GetComponentLookup<GeneratorEfficiency>(false);
        }

        [CompletesDependency("OnThrottledUpdate fallback path: CalculateEntityCount + ToEntityArray run only when GeneratorEfficiency singleton invariant is violated (>1 entity); diagnostic-only sync amortised over throttle interval")]
        protected override void OnThrottledUpdate()
        {
            // TOCTOU FIX: Use TryGetSingletonEntity for atomic singleton access
            if (!m_EfficiencyQuery.TryGetSingletonEntity<GeneratorEfficiency>(out var entity))
            {
                // Check if this is due to duplicates (warn) or just empty (normal)
                int count = m_EfficiencyQuery.CalculateEntityCount();
                if (count > 1)
                {
                    Log.Warn($"Expected 1 entity, found {count}. Using first.");
                    using var entities = m_EfficiencyQuery.ToEntityArray(Unity.Collections.Allocator.Temp);
                    if (entities.Length == 0)
                        return;
                    entity = entities[0];
                }
                else
                {
                    return; // No singleton exists
                }
            }

            // Read-only lookup — RO sync only (no RW stall from SystemAPI.GetBuffer)
            m_SourceReadLookup.Update(this);
            if (!m_SourceReadLookup.HasBuffer(entity)) return;
            var buffer = m_SourceReadLookup[entity];

            float total = 1f;
            for (int i = 0; i < buffer.Length; i++)
            {
                total *= buffer[i].Multiplier;
            }

            total = math.clamp(total, 0.01f, 10f);

            // Write via ComponentLookup on main thread — cheaper than SystemAPI.SetComponent
            // because lookup is already updated and no additional query resolution needed
            m_EffWriteLookup.Update(this);
            m_EffWriteLookup[entity] = new GeneratorEfficiency { Value = total };
        }
    }
}
