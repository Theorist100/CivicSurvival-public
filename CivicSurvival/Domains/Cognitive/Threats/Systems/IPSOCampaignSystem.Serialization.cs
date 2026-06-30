using Colossal.Serialization.Entities;
using CivicSurvival.Core.Components.Domain.Cognitive;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Serialization;

namespace CivicSurvival.Domains.Cognitive.Threats.Systems
{
    /// <summary>
    /// Reset-only serialization. Runtime gate state is rebuilt by the next
    /// ShouldSkipUpdate reconcile; Deserialize does not touch ECS act state.
    /// </summary>
    public partial class IPSOCampaignSystem : IDefaultSerializable, IResettable, IBootDefaultsReset
    {
        public void ResetToBootDefaults(ResetReason reason)
        {
            InitializeGate();
            m_PendingActApply = false;
            m_PendingActIsActive = false;
            m_PendingActClearExposure = false;
            m_PendingInitialActReconcile = false;
            m_PendingInitialActIsActive = false;
            m_PendingWaveSpike = false;
        }

        public void SetDefaults(Context context) => ResetState();
        void IResettable.ResetState() => ResetState();

        private void ResetState()
        {
            InitializeGate();
            // Spike state lives on the serialized IPSOState.PostWaveSpikeTimer singleton
            // (game-time countdown); only the transient managed gate/intent latches reset here.
            m_PendingActApply = false;
            m_PendingActIsActive = false;
            m_PendingActClearExposure = false;
            m_PendingInitialActReconcile = false;
            m_PendingInitialActIsActive = false;
            m_PendingWaveSpike = false;
        }

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SaveVersions.GLOBAL);
            try
            {
                ResetOnlyStateCodec.Write(writer);
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
            SerializationGuard.LogSerialized(nameof(IPSOCampaignSystem), SaveVersions.GLOBAL);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SaveVersions.GLOBAL, out _, out var block, nameof(IPSOCampaignSystem)))
            {
                ResetToBootDefaults(ResetReason.VersionMismatch);
                return;
            }
            try
            {
                ResetOnlyStateCodec.Read(reader);
                // FIX W3-L12: Use ResetState() instead of inline duplication
                ResetState();
                Log.Info("Deserialized: IPSO act gate reset; next update will reconcile CurrentActSingleton");
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
    }
}
