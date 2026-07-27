using Colossal.Serialization.Entities;
using Unity.Entities;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Components.Domain.Power;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Serialization;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Domains.PowerBackup.Systems
{
    /// <summary>
    /// BackupPowerRuntimeSystem - Save/Load serialization (IDefaultSerializable).
    /// Persists m_LastGameHour to prevent time jump bugs on load.
    /// Without this, delta calculation would cause batteries to lose 15+ hours of charge in one frame.
    /// Stats (m_Stats) are not serialized - recalculated on each update.
    /// </summary>
    #pragma warning disable CIVIC223 // PostLoadValidationSystem invokes ICivicSingletonOwner.OnLoadRestore; Deserialize only buffers payload.
    public partial class BackupPowerRuntimeSystem : IDefaultSerializable, IResettable, IBootDefaultsReset
    {
        public void ResetToBootDefaults(ResetReason reason) => ResetBootDefaultsFields();

    #pragma warning restore CIVIC223
        public void SetDefaults(Context context) => ResetState();
        void IResettable.ResetState() => ResetState();

        private void ResetState()
        {
            Dependency.Complete();
            ResetBootDefaultsFields();
            Log.Info("SetDefaults called");
        }

        private void ResetBootDefaultsFields()
        {
            m_LastGameHour = 0f;
            m_LastBatteryTier = int.MaxValue;
            m_CurrentPolicy = BackupPolicy.Reserve;
            m_DepletionNotified = false;
            m_RechargeNotified = false;
            m_GeneratorDepletionNotified = false;
            ResetTransientRuntimeState();
            InitializeGate();
        }

        private void CompletePendingRuntimeJobsForLoad()
        {
            if (m_HasPendingJob || m_HasPendingSingletonWrite)
                m_PendingJobHandle.Complete();
        }

        private void ResetTransientRuntimeState()
        {
            // Field-only so ResetToBootDefaults remains a pure boot-default path.
            m_HasPendingJob = false;
            m_HasPendingSingletonWrite = false;
            m_HasStats = false;
            m_IsFirstTick = true;
            m_DeltaHours = 0f;
            m_LastCounterfeitVersion = -1;
            m_Stats = default;
            m_UiState = new BackupPowerStateSingleton { Policy = m_CurrentPolicy };
            m_HospitalsPowered = 0;
            m_HospitalsTotal = 0;
            m_SchoolsPowered = 0;
            m_SchoolsTotal = 0;
            if (m_CounterfeitByBuilding.IsCreated) m_CounterfeitByBuilding.Clear();
            if (m_DistrictCoverageMap.IsCreated) m_DistrictCoverageMap.Clear();
            if (m_StatsProtectedBuildings.IsCreated) m_StatsProtectedBuildings.Value = 0;
            if (m_StatsDischargingCount.IsCreated) m_StatsDischargingCount.Value = 0;
            if (m_StatsGeneratorsRunning.IsCreated) m_StatsGeneratorsRunning.Value = 0;
            if (m_StatsTotalCapacityWh.IsCreated) m_StatsTotalCapacityWh.Value = 0;
            if (m_StatsTotalChargeWh.IsCreated) m_StatsTotalChargeWh.Value = 0;
            if (m_StatsGeneratorsTotal.IsCreated) m_StatsGeneratorsTotal.Value = 0;
            if (m_StatsGeneratorsFueled.IsCreated) m_StatsGeneratorsFueled.Value = 0;
            if (m_CovHospitalsPowered.IsCreated) m_CovHospitalsPowered.Value = 0;
            if (m_CovHospitalsTotal.IsCreated) m_CovHospitalsTotal.Value = 0;
            if (m_CovSchoolsPowered.IsCreated) m_CovSchoolsPowered.Value = 0;
            if (m_CovSchoolsTotal.IsCreated) m_CovSchoolsTotal.Value = 0;
        }

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SaveVersions.GLOBAL);
            try
            {

                BackupPowerRuntimeCodec.Write(
                    new BackupPowerRuntimeState(
                        m_LastGameHour,
                        m_LastBatteryTier,
                        m_DepletionNotified,
                        m_RechargeNotified,
                        m_GeneratorDepletionNotified,
                        m_CurrentPolicy),
                    writer);

            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
            SerializationGuard.LogSerialized(nameof(BackupPowerRuntimeSystem), SaveVersions.GLOBAL);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            // LOAD-INVARIANT: drain pending handles before any reset/restore clears job flags.
            // Done here (outside the boot-reset path) so failure routes stay field-only (CIVIC438).
            CompletePendingRuntimeJobsForLoad();

            if (!SerializationGuard.TryBeginBlock(reader, SaveVersions.GLOBAL, out var version, out var block, nameof(BackupPowerRuntimeSystem)))
            {
                ResetToBootDefaults(ResetReason.VersionMismatch);
                return;
            }
            try
            {
                BackupPowerRuntimeCodec.Read(reader, out var state);
                m_LastGameHour = state.LastGameHour;
                m_LastBatteryTier = state.LastBatteryTier;
                m_DepletionNotified = state.DepletionNotified;
                m_RechargeNotified = state.RechargeNotified;
                m_GeneratorDepletionNotified = state.GeneratorDepletionNotified;
                m_CurrentPolicy = state.Policy;
                ResetTransientRuntimeState();

                Log.Info($"Deserialized v{version}: LastGameHour={m_LastGameHour:F2}, BatteryTier={m_LastBatteryTier}, Policy={m_CurrentPolicy}");
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

            InitializeGate();
        }

        public void OnLoadRestore(EntityManager entityManager)
        {
            BackupPowerStateSingleton.EnsureExists(entityManager);
            ChargeRateRegistry.EnsureExists(entityManager);

            if (m_BackupPowerSingletonQuery.TryGetSingletonEntity<BackupPowerStateSingleton>(out var singletonEntity))
            {
                entityManager.SetComponentData(singletonEntity, new BackupPowerStateSingleton { Policy = m_CurrentPolicy });
                if (!entityManager.HasBuffer<DistrictBatteryCoverage>(singletonEntity))
                    entityManager.AddBuffer<DistrictBatteryCoverage>(singletonEntity);
            }
        }
    }
}
