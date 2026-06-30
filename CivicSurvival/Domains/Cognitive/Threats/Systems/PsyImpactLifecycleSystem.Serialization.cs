using Colossal.Serialization.Entities;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Serialization;

namespace CivicSurvival.Domains.Cognitive.Threats.Systems
{
    /// <summary>
    /// S17b-8 FIX: Reset m_BulkInitDone on load.
    /// HasPsyState tags are NOT serialized — all households are untagged after load.
    /// Phase 1 must re-run to re-tag them, so m_BulkInitDone must be false.
    /// No actual data to serialize — just the load-boundary reset hook.
    /// </summary>
    public partial class PsyImpactLifecycleSystem : IDefaultSerializable, IResettable, IBootDefaultsReset
    {
        public void ResetToBootDefaults(ResetReason reason) => ResetState();

        public void SetDefaults(Context context) => ResetState();
        void IResettable.ResetState() => ResetState();

        private void ResetState()
        {
            m_BulkInitDone = false;
            m_StableEmptyFrames = 0;
            m_SlotAssignCounter = 0;
            Enabled = true;
            ResetCounters(); // FIX W2-L3: Reset static counter on load
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
            SerializationGuard.LogSerialized(nameof(PsyImpactLifecycleSystem), SaveVersions.GLOBAL);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SaveVersions.GLOBAL, out _, out var block, nameof(PsyImpactLifecycleSystem)))
            {
                ResetToBootDefaults(ResetReason.VersionMismatch);
                return;
            }
            try
            {
                ResetOnlyStateCodec.Read(reader);
                // FIX W3-L11: Use ResetState() to also reset static diagnostic counter
                ResetState();
                Log.Info("Deserialized: m_BulkInitDone reset (Phase 1 will re-tag households)");
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
