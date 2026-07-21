using Colossal.Serialization.Entities;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Serialization;
using CivicSurvival.Core.Services;

namespace CivicSurvival.Domains.Countermeasures.UI
{
    public partial class CountermeasuresUISystem : IDefaultSerializable, IResettable, IBootDefaultsReset
    {
        public void ResetToBootDefaults(ResetReason reason) => ResetState();

        public void SetDefaults(Context context) => ResetState();
        void IResettable.ResetState() => ResetState();

        private void ResetState()
        {
            m_ArrestedDismissed = false;
            m_ChoiceRequestPending = false;
            m_ChoiceRequestCreatedFrame = -1;
            m_LastChoiceResultValue = default;
            m_LastChoiceResultText = "";
            m_CurrentJournalistValue = default;
            m_CurrentJournalistText = "";
            m_ArrestedModalRequested = false;
        }

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SaveVersions.GLOBAL);
            try
            {
                var state = new CountermeasuresUIPersistState(m_ArrestedDismissed);
                CountermeasuresUICodec.Write(state, writer);
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
            SerializationGuard.LogSerialized(nameof(CountermeasuresUISystem), SaveVersions.GLOBAL);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SaveVersions.GLOBAL, out var version, out var block, nameof(CountermeasuresUISystem)))
            {
                ResetToBootDefaults(ResetReason.VersionMismatch);
                return;
            }
            try
            {
#pragma warning disable CIVIC098 // ModalCoordinator.Instance is static readonly = new(), never null
                ModalCoordinator.Instance.Reset();
#pragma warning restore CIVIC098
                CountermeasuresUICodec.Read(reader, out var state);
                m_ArrestedDismissed = state.ArrestedDismissed;
                m_ArrestedModalRequested = false;

                Log.Info($"[{nameof(CountermeasuresUISystem)}] Deserialized v{version}: ArrestedDismissed={m_ArrestedDismissed}");
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
