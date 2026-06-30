using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Components.Domain.Power;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;
using Game;
using Game.Common;
using Unity.Entities;

namespace CivicSurvival.Domains.Engineering.Systems
{
    /// <summary>
    /// Consumer half of the power-index producer/consumer split (mirror of vanilla
    /// <c>IgniteSystem</c> and the mod's own <c>ModFireApplySystem</c>). Runs in
    /// ModificationEnd: reads <see cref="PowerIndexIntent"/> entities created by
    /// <c>PowerCapacityIndexSystem</c> in GameSimulation and performs the first add of the
    /// index/modifier components on the vanilla grid plant from THIS phase — where vanilla's
    /// render batch pipeline (<c>RequiredBatchesSystem</c>, <c>PreCullingSystem</c>,
    /// <c>BatchInstanceSystem</c>, all later in MainLoop) expects the archetype migration.
    ///
    /// Doing the structural add from GameSimulation (LateUpdate, end of frame) instead
    /// desyncs with the render pass and can crash a vanilla Burst batch job on a null chunk
    /// pointer (dump 1138); and a GameSimulation system cannot even create a
    /// <c>ModificationEndBarrier</c> buffer — the barrier's usage gate is closed outside
    /// ModificationEnd ("not allowed").
    ///
    /// The consumer is a dumb materialiser: all values are computed by the producer in
    /// GameSimulation (where the grid lookups are valid) and carried in the intent. The add
    /// is idempotent (ECB add-or-set), so a surviving intent on an already-indexed plant
    /// (e.g. post-load <c>ValidateAfterLoad</c> ran first) just re-sets the values and is
    /// destroyed — no migration, no loop.
    /// </summary>
    [ActIndependent]
    public partial class PowerIndexApplySystem : CivicSystemBase
    {
        private static readonly LogContext Log = new("PowerIndexApplySystem");

        private EntityQuery m_IntentQuery;
        private ModificationEndBarrier m_ModificationEndBarrier = null!;
        private EntityStorageInfoLookup m_StorageInfoLookup;
        private ComponentLookup<Deleted> m_DeletedLookup;
        private ComponentLookup<Destroyed> m_DestroyedLookup;
        private ComponentTypeSet m_GridIndexComponents;
        private readonly System.Collections.Generic.HashSet<long> m_ProcessedBuildings = new();
        [System.NonSerialized] private int m_ProcessedBuildingsFrame = -1;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_IntentQuery = GetEntityQuery(ComponentType.ReadWrite<PowerIndexIntent>());
            m_ModificationEndBarrier = World.GetOrCreateSystemManaged<ModificationEndBarrier>();
            m_StorageInfoLookup = GetEntityStorageInfoLookup();
            m_DeletedLookup = GetComponentLookup<Deleted>(true);
            m_DestroyedLookup = GetComponentLookup<Destroyed>(true);

            // One archetype migration for the full grid-plant index set instead of nine
            // sequential AddComponent migrations. GridStressModifier and the four damage/wear
            // modifiers carry their correct zero-state in their default value; base capacity, kind,
            // construction, saturation (neutral = factor 1, not the struct-default 0) and index state
            // are SetComponent'd after the batched add.
            m_GridIndexComponents = new ComponentTypeSet(new ComponentType[]
            {
                ComponentType.ReadWrite<PlantBaseCapacity>(),
                ComponentType.ReadWrite<PowerPlantKind>(),
                ComponentType.ReadWrite<GridStressModifier>(),
                ComponentType.ReadWrite<ConstructionModifier>(),
                ComponentType.ReadWrite<EquipmentWearModifier>(),
                ComponentType.ReadWrite<OperationalDamageModifier>(),
                ComponentType.ReadWrite<DisasterDamageModifier>(),
                ComponentType.ReadWrite<SaturationModifier>(),
                ComponentType.ReadWrite<PowerCapacityIndexState>()
            });

            RequireForUpdate(m_IntentQuery);
            Log.Info("Created");
        }

        protected override void OnUpdateImpl()
        {
            if (m_IntentQuery.IsEmpty)
                return;

            m_StorageInfoLookup.Update(this);
            m_DeletedLookup.Update(this);
            m_DestroyedLookup.Update(this);

            int frame = UnityEngine.Time.frameCount;
            if (frame != m_ProcessedBuildingsFrame)
            {
                m_ProcessedBuildings.Clear();
                m_ProcessedBuildingsFrame = frame;
            }

            EntityCommandBuffer ecb = default;
            bool ecbCreated = false;

            foreach (var (intentRef, entity) in
                SystemAPI.Query<RefRW<PowerIndexIntent>>()
                .WithEntityAccess())
            {
                ref var intent = ref intentRef.ValueRW;
                if (intent.Applied)
                    continue;

                if (!ecbCreated)
                {
                    ecb = m_ModificationEndBarrier.CreateCommandBuffer();
                    ecbCreated = true;
                }

                intent.Applied = true;

                var building = intent.Building.ToEntity();

                // Cross-frame guard: plant may have been destroyed/deleted between the
                // producer (frame N) and this apply (frame N+1).
                if (!m_StorageInfoLookup.Exists(building)
                    || m_DeletedLookup.HasComponent(building)
                    || m_DestroyedLookup.HasComponent(building))
                {
                    ecb.DestroyEntity(entity);
                    continue;
                }

                // Same-pass dedup: two intents on one plant this frame migrate the archetype
                // only once.
                if (!m_ProcessedBuildings.Add(BuildingIdentityKey.Pack(building.Index, building.Version)))
                {
                    ecb.DestroyEntity(entity);
                    continue;
                }

                ApplyIndex(ecb, building, in intent);
                ecb.DestroyEntity(entity);
            }

            if (ecbCreated)
                m_ModificationEndBarrier.AddJobHandleForProducer(Dependency);
        }

        /// <summary>
        /// Materialises the index components on the vanilla grid plant. Structural add lands
        /// in ModificationEnd, in phase with the render pass. ECB add-or-set semantics make
        /// it idempotent: re-applying a surviving intent on an already-indexed plant re-sets
        /// the values without an archetype migration.
        /// </summary>
        private void ApplyIndex(EntityCommandBuffer ecb, Entity building, in PowerIndexIntent intent)
        {
            if (!intent.Classified)
            {
                // Unclassified producer: only PowerPlantKind, mirroring the producer's
                // unclassified early-out (no capacity, no index state).
                ecb.AddComponent(building, new PowerPlantKind { Value = intent.Kind });
                return;
            }

            ecb.AddComponent(building, m_GridIndexComponents);
            ecb.SetComponent(building, new PlantBaseCapacity { OriginalCapacity = intent.OriginalCapacityKW });
            ecb.SetComponent(building, new PowerPlantKind { Value = intent.Kind });
            // GridStressModifier intentionally NOT set here — the batched AddComponent above gives it
            // its default zero-state (IsCollapsed=false). PowerCapacityResolverSystem.ApplyGridStressModifier
            // is the sole hydrator from the CollapsedProducer sidecar.
            // ConstructionModifier IS set explicitly (not left at the batched struct-default false):
            // the producer's ConstructionDelayEnabled read rides intent.ConstructionPending, so a new
            // plant starts under-construction (0 MW) when the feature is on instead of leaking full
            // nameplate. Classification is NOT done here — only ConstructionDelaySystem marks the
            // plant (managed ConstructionClassifiedState side-set), and the resolver's gate keeps it
            // offline until then.
            ecb.SetComponent(building, new ConstructionModifier { IsUnderConstruction = intent.ConstructionPending, Progress = 0f });
            // SaturationModifier IS set explicitly (not left at the batched struct-default 0f): its
            // neutral state is factor=1 (no penalty) until ApplySaturationInertia first runs, so a new
            // plant must NOT start at the struct-default 0 (which would knock it to zero capacity).
            ecb.SetComponent(building, new SaturationModifier { SaturationFactor = 1f, LastUpdateGameHours = 0.0 });
            ecb.SetComponent(building, intent.IndexState);
        }
    }
}
