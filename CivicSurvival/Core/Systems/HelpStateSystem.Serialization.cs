using Colossal.Serialization.Entities;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Serialization;

namespace CivicSurvival.Core.Systems
{
    public partial class HelpStateSystem : IBootDefaultsReset
    {
        public void ResetToBootDefaults(ResetReason reason) => ResetState();

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SaveVersions.GLOBAL);
            try
            {
                HelpStateCodec.Write(new HelpState(m_GridHelpSeen, m_ShadowHelpSeen), writer);
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            // Note: version discarded - no migration needed for simple bool flags
            if (!SerializationGuard.TryBeginBlock(reader, SaveVersions.GLOBAL, out _, out var block, nameof(HelpStateSystem)))
            {
                ResetToBootDefaults(ResetReason.VersionMismatch);
                return;
            }
            try
            {
                HelpStateCodec.Read(reader, out var state);
                m_GridHelpSeen = state.GridHelpSeen;
                m_ShadowHelpSeen = state.ShadowHelpSeen;
                m_NeedsBindingSync = true;  // FIX: Mark for UI sync after load
                SyncBindingsNow();

                Log.Info($"Deserialized: Grid={m_GridHelpSeen}, Shadow={m_ShadowHelpSeen}");
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

        public void SetDefaults(Context context) => ResetState();
        void IResettable.ResetState() => ResetState();

        private void ResetState()
        {
            m_GridHelpSeen = false;
            m_ShadowHelpSeen = false;
            m_NeedsBindingSync = true;  // FIX: Mark for UI sync after defaults
            SyncBindingsNow();
        }

        private void SyncBindingsNow()
        {
            if (m_GridHelpSeenBinding == null || m_ShadowHelpSeenBinding == null)
                return;

            m_GridHelpSeenBinding.Update(m_GridHelpSeen);
            m_ShadowHelpSeenBinding.Update(m_ShadowHelpSeen);
            m_NeedsBindingSync = false;
        }
    }
}
