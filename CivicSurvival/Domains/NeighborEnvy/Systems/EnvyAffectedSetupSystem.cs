using Game;
using Game.Areas;
using Game.Buildings;
using Game.Common;
using Game.Objects;
using Game.Simulation;
using Game.Tools;
using Unity.Burst;
using Unity.Entities;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Components.Domain.NeighborEnvy;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Domains.NeighborEnvy.Systems
{
    /// <summary>
    /// Burst job: add EnvyAffected (disabled) to a residential building via parallel ECB.
    /// EnvyAffected is an empty IEnableableComponent — the only structural change in its whole
    /// lifecycle is this first AddComponent; every later envy state change is a render-safe
    /// enable-bit flip done by NeighborEnvySystem in GameSimulation.
    /// </summary>
#if ENABLE_BURST
    [BurstCompile]
#endif
    public partial struct AddEnvyAffectedJob : IJobEntity
    {
        public EntityCommandBuffer.ParallelWriter ECB;

        void Execute(Entity entity, [EntityIndexInQuery] int sortKey)
        {
            // ENVY-SEED-RENDER-SAFE: sole legal structural add of EnvyAffected. Runs in
            // Modification4 / ModificationBarrier4, so the archetype migration flushes BEFORE
            // RequiredBatchesSystem and the render-side chunk-cache collection of the same frame
            // (render chunk-cache crash class). Value-form add (not generic AddComponent<T>) mirrors
            // AddBlackoutStateJob: both ECB commands are deferred and replay in record order, so the
            // following SetComponentEnabled is safe — seeded DISABLED, semantically inert until
            // NeighborEnvySystem flips the bit.
            ECB.AddComponent(sortKey, entity, new EnvyAffected());
            ECB.SetComponentEnabled<EnvyAffected>(sortKey, entity, false);
        }
    }

    /// <summary>
    /// Seeds EnvyAffected (disabled) onto every residential building, so NeighborEnvySystem
    /// (GameSimulation) and its rebuild/incremental logic only ever flip the enable-bit and
    /// NEVER do a structural add.
    ///
    /// Runs in Modification4 + ModificationBarrier4 (mirror of BlackoutStateSetupSystem /
    /// vanilla IgniteSystem, SystemOrder.cs:162). The first AddComponent&lt;EnvyAffected&gt; on a
    /// residential building is the only structural change here; flushed at Modification4 it
    /// migrates the building's archetype BEFORE RequiredBatchesSystem and the render-side
    /// chunk-cache collection of the same frame. Done in GameSimulation (LateUpdate, where the
    /// envy logic used to add it) the add played back out of phase with the render pass → zeroed
    /// render chunk-cache crash class (c0000005 in a vanilla Burst batch job, mod code absent
    /// from the stack). Modification4 (MainLoop) runs before GameSimulation (LateUpdate), so
    /// EnvyAffected still exists before NeighborEnvySystem reads it — the "component exists
    /// before the logic reads it" invariant is preserved (even strengthened).
    ///
    /// EnvyAffected is NOT serialized — on load no building has it. This system re-seeds all
    /// residential buildings (first throttle), then NeighborEnvySystem's post-load full rebuild
    /// re-enables the affected ones; afterwards only new buildings (city growth) match.
    ///
    /// Feature-gated on ModSettings.NeighborEnvyEnabled (the player-facing / preset-driven toggle,
    /// false by default + under ManagedDeficit): with the feature OFF nothing is ever seeded,
    /// preserving today's zero-exposure default — no archetype migration is imposed on players who
    /// never enable neighbor envy. (NeighborEnvySystem.FeatureEnabled is a serialized flag that is
    /// never set false, so NeighborEnvyEnabled alone is the live gate; seeding while it were toggled
    /// off would only add an inert disabled component, never wrong pressure.)
    ///
    /// Throttled 500ms — new buildings wait at most half a second for the seed.
    /// IJobEntity + ECB.ParallelWriter — no main-thread sync point.
    /// </summary>
    [ActIndependent]
    public partial class EnvyAffectedSetupSystem : ThrottledSystemBase
    {
        private static readonly LogContext Log = new("EnvyAffectedSetup");

        private EntityQuery m_MissingEnvyQuery;
        private ModificationBarrier4 m_ECBSystem = null!;
        private ModSettings? m_Settings;

        protected override int UpdateInterval => Engine.Timing.UPDATE_INTERVAL_500_MS;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_ECBSystem = World.GetOrCreateSystemManaged<ModificationBarrier4>();

            // Residential buildings that do NOT yet carry our EnvyAffected tag.
            // All-set mirrors NeighborEnvySystem.m_ResidentialQuery exactly (Building +
            // ResidentialProperty + ElectricityConsumer + Transform + CurrentDistrict) so the
            // seed-set equals the set the rebuild iterates — every building the rebuild would
            // enable is seeded, none extra. Temp is excluded: tool previews carry
            // Building/ResidentialProperty and must not be seeded (same precedent as the
            // phantom-plant Exclude<Temp> fix).
            m_MissingEnvyQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Building>(),
                    ComponentType.ReadOnly<ResidentialProperty>(),
                    ComponentType.ReadOnly<ElectricityConsumer>(),
                    ComponentType.ReadOnly<Transform>(),
                    ComponentType.ReadOnly<CurrentDistrict>()
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Deleted>(),
                    ComponentType.ReadOnly<Temp>()
                },
                Absent = new[]
                {
                    ComponentType.ReadOnly<EnvyAffected>()
                }
            });

            Log.Info($"{nameof(EnvyAffectedSetupSystem)} created (throttled 500ms, IJobEntity, Modification4)");
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            m_Settings ??= ServiceRegistry.Instance.Require<ModSettings>();
        }

        protected override bool ShouldSkipUpdate()
        {
            return m_Settings == null || !m_Settings.NeighborEnvyEnabled;
        }

        protected override void OnThrottledUpdate()
        {
            // CIVIC186: skip scheduling on an empty query (wasted CPU). Steady state once every
            // residential building is seeded — only fires again on city growth.
            if (m_MissingEnvyQuery.IsEmpty)
                return;

            // Seed signal at Debug only: no transient latch (a one-time flag would leak across CS2's
            // in-process load reuse and falsely silence the post-load re-seed), no CalculateEntityCount
            // (that would force a sync point for a log line). NeighborEnvySystem logs affected counts.
            if (Log.IsDebugEnabled)
                Log.Debug("Seeding EnvyAffected (disabled) onto residential buildings");

            var ecb = m_ECBSystem.CreateCommandBuffer().AsParallelWriter();
            var addJob = new AddEnvyAffectedJob { ECB = ecb };
            Dependency = addJob.ScheduleParallel(m_MissingEnvyQuery, Dependency);
            m_ECBSystem.AddJobHandleForProducer(Dependency);
        }

        protected override void OnDestroy()
        {
            Dependency.Complete();
            base.OnDestroy();
        }
    }
}
