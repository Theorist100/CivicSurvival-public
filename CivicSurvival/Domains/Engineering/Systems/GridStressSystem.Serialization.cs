using Colossal.Serialization.Entities;
using Game.Common;
using Unity.Collections;
using Unity.Entities;
using CivicSurvival.Core.Components.Domain.Engineering;
using CivicSurvival.Core.Components.Domain.Power;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Serialization;

namespace CivicSurvival.Domains.Engineering.Systems
{
    /// <summary>
    /// GridStressSystem - Save/Load serialization (IDefaultSerializable).
    /// Persists m_LastZone to prevent duplicate GridStressWarningEvent on load.
    /// GridStressData singleton is auto-serialized by ECS (IComponentData).
    /// </summary>
    #pragma warning disable CIVIC223 // PostLoadValidationSystem invokes ICivicSingletonOwner.OnLoadRestore; Deserialize only buffers payload.
    public partial class GridStressSystem : IDefaultSerializable, IResettable, IBootDefaultsReset
    {
        public void ResetToBootDefaults(ResetReason reason) => ResetBootDefaultsFields();

    #pragma warning restore CIVIC223
        [System.NonSerialized] private GridStressData m_RestoredGridStressData;
        [System.NonSerialized] private bool m_HasRestoredGridStressData;

        public void SetDefaults(Context context) => ResetState();
        void IResettable.ResetState() => ResetState();

        private void ResetState()
        {
            ResetBootDefaultsFields();
            if (World != null && World.IsCreated)
                SweepNonCollapsedSidecars(EntityManager);
            Log.Info("SetDefaults called");
        }

        private void ResetBootDefaultsFields()
        {
            m_HasRestoredGridStressData = false;
            m_LastGameHour = -1.0;
            m_LastZone = GridStressZone.Normal;
            m_KnownAct = default;
            // Re-arm the one-shot pre-collapse warning so a fresh game/episode warns again.
            m_CriticalModalShown = false;
            // ForceCollapseOwnerReset clears the prev-key set and bumps the
            // revision so the post-reset Empty publish never compares equal
            // to the cursored Empty from a prior reset.
            ForceCollapseOwnerReset();
            if (m_CollapsedByBuilding.IsCreated) m_CollapsedByBuilding.Clear();
            // Frame-local set, rebuilt every OnThrottledUpdate; clear here to
            // satisfy the IResettable contract and silence CIVIC278.
            if (m_CurCollapseOwnerKeys.IsCreated) m_CurCollapseOwnerKeys.Clear();
#if DEBUG
            // Drop any debug command queued but not yet drained so it cannot carry into a loaded city.
            m_DebugForceCollapsePending = false;
            m_DebugResetStressPending = false;
            m_DebugSetStressHoursPending = false;
            m_DebugSetStressHoursValue = 0f;
            m_DebugCommandSource = "";
#endif
        }

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SaveVersions.GLOBAL);
            try
            {
                if (!m_StressDataQuery.TryGetSingleton<GridStressData>(out var gridStress))
                    gridStress = GridStressData.CreateDefault();

                var state = new GridStressPersistState(
                    m_LastGameHour,
                    (byte)m_LastZone,
                    gridStress.StressHours,
                    gridStress.CurrentFrequency,
                    gridStress.IsCollapsed,
                    (byte)gridStress.Zone,
                    gridStress.RecoveryHoursRemaining,
                    gridStress.CollapseThresholdHours);
                GridStressCodec.Write(state, writer);
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
            SerializationGuard.LogSerialized(nameof(GridStressSystem), SaveVersions.GLOBAL);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SaveVersions.GLOBAL, out var version, out var block, nameof(GridStressSystem)))
            {
                ResetToBootDefaults(ResetReason.VersionMismatch);
                return;
            }
            try
            {
                GridStressCodec.Read(reader, out var state);
                m_LastGameHour = state.LastGameHour;
                // Enum.IsDefined requires the value to match the enum's underlying type
                // exactly (GridStressZone : byte). state.LastZone/Zone are already byte —
                // casting to int makes IsDefined throw ArgumentException (Int32 vs Byte),
                // which previously dropped every load into ResetToBootDefaults.
                byte lastZone = System.Enum.IsDefined(typeof(GridStressZone), state.LastZone)
                    ? state.LastZone
                    : (byte)GridStressZone.Normal;
                byte zone = System.Enum.IsDefined(typeof(GridStressZone), state.Zone)
                    ? state.Zone
                    : (byte)GridStressZone.Normal;
                m_LastZone = (GridStressZone)lastZone;
                m_RestoredGridStressData = new GridStressData
                {
                    StressHours = state.StressHours,
                    CurrentFrequency = state.CurrentFrequency,
                    IsCollapsed = state.IsCollapsed,
                    Zone = (GridStressZone)zone,
                    RecoveryHoursRemaining = state.RecoveryHoursRemaining,
                    CollapseThresholdHours = state.CollapseThresholdHours
                };
                m_HasRestoredGridStressData = true;

                Log.Info($"Deserialized v{version}: LastGameHour={m_LastGameHour:F2}, LastZone={m_LastZone}");
            }
            catch (System.Exception ex)
            {
                Log.Error($"Deserialize failed: {ex}");
                ResetToBootDefaults(ResetReason.DeserializeFailed);
            }
            finally
            {
                SerializationGuard.EndBlock(reader, block);
            }
        }

        public void OnLoadRestore(EntityManager entityManager)
        {
            GridStressData.EnsureExists(entityManager);
            GridStressData restored = m_HasRestoredGridStressData
                ? m_RestoredGridStressData
                : GridStressData.CreateDefault();
            // Cached-query singleton lookup is context-free, unlike SystemAPI.TryGetSingletonEntity,
            // so it stays correct when OnLoadRestore runs from PostLoadValidationSystem's context.
            if (m_StressDataQuery.TryGetSingletonEntity<GridStressData>(out var entity))
            {
                entityManager.SetComponentData(entity, restored);
            }
            if (!restored.IsCollapsed)
                SweepNonCollapsedSidecars(entityManager);
            m_HasRestoredGridStressData = false;
        }

        private void SweepNonCollapsedSidecars(EntityManager entityManager)
        {
            var collapsedQuery = entityManager.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<CollapsedProducer>() },
                None = new[] { ComponentType.ReadOnly<Deleted>() }
            });
            using (var collapsedEntities = collapsedQuery.ToEntityArray(Allocator.Temp))
            {
                for (int i = 0; i < collapsedEntities.Length; i++)
                    entityManager.AddComponent<Deleted>(collapsedEntities[i]);
                if (collapsedEntities.Length > 0)
                    Log.Info($"Cleared {collapsedEntities.Length} stale CollapsedProducer sidecars for non-collapsed grid");
            }
            collapsedQuery.Dispose();

            var modifierQuery = entityManager.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadWrite<GridStressModifier>() },
                None = new[] { ComponentType.ReadOnly<Deleted>() }
            });
            using (var modifierEntities = modifierQuery.ToEntityArray(Allocator.Temp))
            {
                int cleared = 0;
                for (int i = 0; i < modifierEntities.Length; i++)
                {
                    Entity entity = modifierEntities[i];
                    var modifier = entityManager.GetComponentData<GridStressModifier>(entity);
                    if (!modifier.IsCollapsed)
                        continue;
                    modifier.IsCollapsed = false;
                    entityManager.SetComponentData(entity, modifier);
                    cleared++;
                }
                if (cleared > 0)
                    Log.Info($"Cleared {cleared} stale GridStressModifier flags for non-collapsed grid");
            }
            modifierQuery.Dispose();

            if (m_CollapsedByBuilding.IsCreated)
                m_CollapsedByBuilding.Clear();
            ForceCollapseOwnerReset();
        }
    }
}
