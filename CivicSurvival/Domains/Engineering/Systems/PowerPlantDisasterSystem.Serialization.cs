using Colossal.Serialization.Entities;
using Unity.Entities;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Serialization;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Domains.Engineering.Systems
{
    public partial class PowerPlantDisasterSystem : IDefaultSerializable, IResettable, IBootDefaultsReset
    {
        public void ResetToBootDefaults(ResetReason reason) => ResetState();

        public void SetDefaults(Context context) => ResetState();
        void IResettable.ResetState() => ResetState();

        private void ResetState()
        {
            m_Random = new SerializableRandom(RANDOM_SEED);
            m_GameHour = 0f;
            m_LastCheckHour = -1f;
            if (m_ContractsByBuilding.IsCreated) m_ContractsByBuilding.Clear();
            if (m_DisabledByBuilding.IsCreated) m_DisabledByBuilding.Clear();
            m_DisasterCandidates?.Clear();
            m_DisabledCleanupDone = false;
            InitializeGate();
            Log.Info("SetDefaults called");
        }

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SaveVersions.GLOBAL);
            try
            {
                var state = new PowerPlantDisasterPersistState(
                    m_GameHour,
                    m_LastCheckHour);
                PowerPlantDisasterCodec.Write(state, writer);
                m_Random.Serialize(writer);
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
            SerializationGuard.LogSerialized(nameof(PowerPlantDisasterSystem), SaveVersions.GLOBAL);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SaveVersions.GLOBAL, out var version, out var block, nameof(PowerPlantDisasterSystem)))
            {
                ResetToBootDefaults(ResetReason.VersionMismatch);
                return;
            }
            try
            {
                PowerPlantDisasterCodec.Read(reader, out var state);
                m_GameHour = state.GameHour;
                m_LastCheckHour = state.LastCheckHour;
                m_Random.Deserialize(reader);
                InitializeGate();

                Log.Info($"Deserialized v{version}: GameHour={m_GameHour:F2}, LastCheck={m_LastCheckHour:F2}");
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

            // Act gate reconciles from CurrentActSingleton in ShouldSkipUpdate after load.
            // DO NOT read ScenarioSingleton here -- Scenario domain (2300) deserializes after Engineering (2100)
        }
    }
}
