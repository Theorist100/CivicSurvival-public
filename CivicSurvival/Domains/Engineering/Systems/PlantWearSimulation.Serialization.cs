using Colossal.Serialization.Entities;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Serialization;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Domains.Engineering.Systems
{
    /// <summary>
    /// PlantWearSimulation - Save/Load serialization (IDefaultSerializable).
    /// Persists the StablePlantId counter (m_NextPlantId) and simulation clock
    /// (m_GameHour, m_LastGameHour). Pending repair set is rebuilt by
    /// PlantRepairRequestProcessor.HydratePendingRepairPlantIds post-load.
    /// </summary>
    public partial class PlantWearSimulation : IDefaultSerializable, IResettable, IBootDefaultsReset
    {
#pragma warning disable CIVIC324 // Ephemeral boot-reset gate; consumed by ValidateAfterLoad to clear runtime buffers exactly once.
        [System.NonSerialized] private bool m_PendingBootDefaultRuntimeReset;
#pragma warning restore CIVIC324

        public void ResetToBootDefaults(ResetReason reason)
        {
            ResetManualPersistState();
            InitializeGate();
            m_PendingBootDefaultRuntimeReset = true;
            Log.Info($"Boot reset: reason={reason}");
        }

        public void SetDefaults(Context context) => ResetState();
        void IResettable.ResetState() => ResetState();

        private void ResetState()
        {
            ResetManualPersistState();
            InitializeGate();
            // Complete any in-flight Burst job before clearing its buffers
            if (m_HasPendingResults)
            {
                m_PendingJobHandle.Complete();
                m_HasPendingResults = false;
            }

            // Clear native containers (IsCreated guards support early reset/test-world setup)
            if (m_PlantIdToEntity.IsCreated) m_PlantIdToEntity.Clear();
            if (m_WearEntities.IsCreated) m_WearEntities.Clear();
            if (m_WearInputs.IsCreated) m_WearInputs.Clear();
            if (m_PendingEntities.IsCreated) m_PendingEntities.Clear();
            // V_REGRESSION Phase 8: m_IgniteQueuedThisFrame retired; shared
            // IFrameMutationDedup frame-cleared by FrameMutationDedupClearSystem.
            if (m_WearOutputs.IsCreated) { m_WearOutputs.Dispose(); m_WearOutputs = default; }
            m_HasPendingResults = false;
            m_PendingCount = 0;
            m_SuppressInitialEnabledRebase = false;
            // m_StorageInfoLookup NOT reset: it is an EntityManager accessor acquired
            // in OnCreate, not game state. Setting it to default orphans the internal
            // EntityComponentStore pointer; .Update(this) on a default-constructed
            // handle does not re-acquire it, so .Exists() NREs. The other lookups
            // are likewise never reset here.

            Log.Info("SetDefaults called");
        }

        private void ResetManualPersistState()
        {
            m_NextPlantId = 1;
            m_GameHour = 0.0;
            m_LastGameHour = -1.0;
        }

        private void CompleteAndClearRuntimeBuffers()
        {
            if (m_HasPendingResults)
            {
                m_PendingJobHandle.Complete();
                m_HasPendingResults = false;
            }

            if (m_PlantIdToEntity.IsCreated) m_PlantIdToEntity.Clear();
            if (m_WearEntities.IsCreated) m_WearEntities.Clear();
            if (m_WearInputs.IsCreated) m_WearInputs.Clear();
            if (m_PendingEntities.IsCreated) m_PendingEntities.Clear();
            // m_IgniteQueuedThisFrame retired in Phase 8 — see comment above.
            if (m_WearOutputs.IsCreated) { m_WearOutputs.Dispose(); m_WearOutputs = default; }
            m_HasPendingResults = false;
            m_PendingCount = 0;
            m_SuppressInitialEnabledRebase = false;
        }

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SaveVersions.GLOBAL);
            try
            {
                var state = new EquipmentWearPersistState(m_NextPlantId, m_GameHour, m_LastGameHour);
                EquipmentWearCodec.Write(state, writer);
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
            SerializationGuard.LogSerialized(nameof(PlantWearSimulation), SaveVersions.GLOBAL);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SaveVersions.GLOBAL, out var version, out var block, nameof(PlantWearSimulation)))
            {
                ResetToBootDefaults(ResetReason.VersionMismatch);
                return;
            }
            try
            {
                // CIVIC348: complete in-flight job and dispose outputs before overwriting state
                if (m_HasPendingResults)
                {
                    m_PendingJobHandle.Complete();
                    m_HasPendingResults = false;
                }
                if (m_WearOutputs.IsCreated) { m_WearOutputs.Dispose(); m_WearOutputs = default; }

                if (m_PlantIdToEntity.IsCreated) m_PlantIdToEntity.Clear();
                m_SuppressInitialEnabledRebase = false;

                EquipmentWearCodec.Read(reader, out var state);
                m_NextPlantId = state.NextPlantId;
                m_GameHour = state.GameHour;
                m_LastGameHour = state.LastGameHour;

                Log.Info($"Deserialized v{version}: NextPlantId={m_NextPlantId}, GameHour={m_GameHour:F2}");
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

            // Act gate is derived from CurrentActSingleton by runtime ingress; it is
            // not serialized. Reset to AwaitingActState for the next ShouldSkipUpdate().
            InitializeGate();
        }
    }
}
