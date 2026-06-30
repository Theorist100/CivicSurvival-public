using Colossal.Serialization.Entities;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Serialization;

namespace CivicSurvival.Domains.Refugees.Systems
{
    /// <summary>
    /// RefugeeIntegrationSystem - Save/Load serialization (IDefaultSerializable).
    /// Persists integration batch index to prevent duplicate chirper messages.
    /// </summary>
    public partial class RefugeeIntegrationSystem : IDefaultSerializable, IResettable, IBootDefaultsReset
    {
        public void ResetToBootDefaults(ResetReason reason) => ResetState();

        public void SetDefaults(Context context) => ResetState();

        void IResettable.ResetState() => ResetState();

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SaveVersions.GLOBAL);
            try
            {
                var state = new RefugeeIntegrationPersistState(
                    m_LastCheckGameHours,
                    m_IntegrationBatchIndex);
                RefugeeIntegrationCodec.Write(state, writer);
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
            SerializationGuard.LogSerialized(nameof(RefugeeIntegrationSystem), SaveVersions.GLOBAL);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SaveVersions.GLOBAL, out var version, out var block, nameof(RefugeeIntegrationSystem)))
            {
                ResetToBootDefaults(ResetReason.VersionMismatch);
                return;
            }
            try
            {
                RefugeeIntegrationCodec.Read(reader, out var state);
                m_LastCheckGameHours = state.LastCheckGameHours;
                m_IntegrationBatchIndex = state.IntegrationBatchIndex;

                Log.Info($"Deserialized v{version}: BatchIndex={m_IntegrationBatchIndex}");
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

        private void ResetState()
        {
            m_LastCheckGameHours = 0.0;
            m_IntegrationBatchIndex = 0;
            Log.Info("State reset");
        }
    }
}
