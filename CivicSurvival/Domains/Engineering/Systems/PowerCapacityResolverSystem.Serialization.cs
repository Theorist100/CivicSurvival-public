using Colossal.Serialization.Entities;
using Unity.Entities;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Components.Domain.Power;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Serialization;

namespace CivicSurvival.Domains.Engineering.Systems
{
    /// <summary>
    /// PowerCapacityResolverSystem — Save/Load of the Daily Peak demand ring (Фаза 3).
    ///
    /// The resolver owns the persisted <see cref="DemandPeakSingleton"/> (24h rolling demand-peak
    /// ring). The ring must survive load: a reloaded spam city with an empty ring would see a low
    /// ratio → saturation degradation disabled → spam immunity returns.
    ///
    /// Pattern mirrors GridStressSystem.Serialization.cs (stage in Deserialize, write into the
    /// singleton in OnLoadRestore) and MaintenanceContractSystem (lazy post-load reconcile latch,
    /// because the data needed for reconcile — PowerGridSingleton.Demand — is not fresh yet in
    /// ValidateAfterLoad).
    /// </summary>
    #pragma warning disable CIVIC223 // PostLoadValidationSystem invokes ICivicSingletonOwner.OnLoadRestore; Deserialize only buffers payload.
    public partial class PowerCapacityResolverSystem : IDefaultSerializable, IResettable, IBootDefaultsReset
    #pragma warning restore CIVIC223
    {
        public void ResetToBootDefaults(ResetReason reason) => ResetBootDefaultsFields();
        public void SetDefaults(Context context) => ResetState();
        void IResettable.ResetState() => ResetState();

        private void ResetState() => ResetBootDefaultsFields();

        /// <summary>
        /// Field-only reset (new-game / version-mismatch / deserialize failure). Zeroes ALL of the
        /// system's transient ([NonSerialized] / non-persisted) state so a reused system instance
        /// does not carry the previous city's values across a load — no structural ECS work here (the
        /// DemandPeakSingleton ring is created/normalised structurally in OnCreate/OnStartRunning/
        /// OnLoadRestore, never in the reset path). Mirrors GridStressSystem.ResetBootDefaultsFields.
        /// </summary>
        private void ResetBootDefaultsFields()
        {
            // Pending-resolve latches: a resolve scheduled by the previous city must not publish
            // into the new one. Field-only here (CIVIC462) — no JobHandle.Complete: by the time
            // any reset path runs (Deserialize / new-game SetDefaults), the load's structural
            // changes have already force-completed every in-flight job, so dropping the latch is
            // safe. The full invalidate (Complete + clear) lives in ValidateAfterLoad.
            m_HasPendingResolveResults = false;
            m_PendingWasAfterLoad = false;
            if (m_PlantWorkInput.IsCreated)
                m_PlantWorkInput.Clear();
            if (m_PendingPlantRows.IsCreated)
                m_PendingPlantRows.Clear();
            if (m_PendingEdgeWrites.IsCreated)
                m_PendingEdgeWrites.Clear();
            if (m_PendingAggregates.IsCreated)
                m_PendingAggregates.Value = default;

            // Daily Peak (Фаза 3) restore latches.
            m_HasRestoredDemandPeak = false;
            m_RestoredDemandPeak = default;
            m_DemandPeakReconcilePending = false;

            // Snapshot publication latch — re-resolved on the first tick / ValidateAfterLoad.
#pragma warning disable CIVIC458 // Resetting the publisher's own snapshot latch on boot/new-game; same field the ValidateAfterLoad path clears.
            m_LatestSnapshot = PowerCapacitySnapshot.Empty;
            m_HasPublishedSnapshot = false;
#pragma warning restore CIVIC458

            // Flow-edge retry latch — rebuilt from the next ResolveAndPublish.
            m_FlowEdgeDirty = false;

            // Import-cap observation latch — re-observed in ValidateAfterLoad / ObserveImportCapVersion.
            m_LastObservedImportCapKW = int.MinValue;
            m_LastObservedImportCapKnown = false;

            // Export-edge diagnostics latch — a warning burned in the previous city must
            // not mute the unresolved-route log for the next one.
            m_ExportEdgeUnresolvedStreak = 0;
            m_ExportEdgeWarned = false;

            // SURPLUS dead-band hold — re-snapped on the first post-load resolve.
            m_PublishedCityDispatchableMW = 0;

            // Per-tick derived fleet aggregates — recomputed every throttle tick.
            m_FleetNameplateKW = 0;
            m_FleetTargetFactor = 1f;
        }

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SaveVersions.GLOBAL);
            try
            {
                var state = BuildPersistStateFromSingleton();
                DemandPeakCodec.Write(state, writer);
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
            SerializationGuard.LogSerialized(nameof(PowerCapacityResolverSystem), SaveVersions.GLOBAL);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SaveVersions.GLOBAL, out _, out var block, nameof(PowerCapacityResolverSystem)))
            {
                ResetToBootDefaults(ResetReason.VersionMismatch);
                return;
            }
            try
            {
                DemandPeakCodec.Read(reader, out var state);
                m_RestoredDemandPeak = state;
                m_HasRestoredDemandPeak = true;
                // Defer the staleness reconcile to the first OnThrottledUpdate: GameTimeSystem is not
                // active during Deserialize and PowerGridSingleton.Demand reads 0 until
                // PowerGridDataSystem republishes it (after the resolver in the hydration order).
                m_DemandPeakReconcilePending = true;
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
            // Structural singleton create/normalise lives here (and OnCreate/OnStartRunning), never
            // in the field-only reset path. Mirrors GridStressSystem.OnLoadRestore.
            Entity entity = DemandPeakSingleton.EnsureExists(entityManager);

            // Either write the restored ring, or zero the live ring for a reused system instance that
            // carries the previous city's buckets (no restored payload ⇒ start clean).
            System.Collections.Generic.IReadOnlyList<int>? sourceBuckets =
                m_HasRestoredDemandPeak ? m_RestoredDemandPeak.HourlyPeakKW : null;
            if (entityManager.HasComponent<DemandPeakSingleton>(entity))
            {
                entityManager.SetComponentData(entity, new DemandPeakSingleton
                {
                    CursorHour = m_HasRestoredDemandPeak ? m_RestoredDemandPeak.CursorHour : 0,
                    LastSampleGameHours = m_HasRestoredDemandPeak ? m_RestoredDemandPeak.LastSampleGameHours : 0.0
                });

                if (entityManager.HasBuffer<DemandPeakBucket>(entity))
                {
                    var ring = entityManager.GetBuffer<DemandPeakBucket>(entity);
                    ring.Clear();
                    for (int i = 0; i < DemandPeakSingleton.BUCKETS; i++)
                        ring.Add(new DemandPeakBucket { PeakKW = sourceBuckets != null ? sourceBuckets[i] : 0 });
                }
            }

            m_HasRestoredDemandPeak = false;
            // Re-arm the lazy reconcile: OnLoadRestore can run on a path where Deserialize was not
            // the one that set the flag (default-reset reload), so a stale ring is still checked.
            m_DemandPeakReconcilePending = true;
        }

        private DemandPeakPersistState BuildPersistStateFromSingleton()
        {
            var buckets = new int[DemandPeakSingleton.BUCKETS];
            int cursor = 0;
            double lastSample = 0.0;

            if (World != null && World.IsCreated
                && m_DemandPeakQuery.TryGetSingletonEntity<DemandPeakSingleton>(out var entity)
                && EntityManager.HasComponent<DemandPeakSingleton>(entity))
            {
                var state = EntityManager.GetComponentData<DemandPeakSingleton>(entity);
                cursor = state.CursorHour;
                lastSample = state.LastSampleGameHours;
                if (EntityManager.HasBuffer<DemandPeakBucket>(entity))
                {
                    var ring = EntityManager.GetBuffer<DemandPeakBucket>(entity);
                    int n = System.Math.Min(ring.Length, DemandPeakSingleton.BUCKETS);
                    for (int i = 0; i < n; i++)
                        buckets[i] = ring[i].PeakKW;
                }
            }

            return new DemandPeakPersistState(buckets, cursor, lastSample);
        }
    }
}
