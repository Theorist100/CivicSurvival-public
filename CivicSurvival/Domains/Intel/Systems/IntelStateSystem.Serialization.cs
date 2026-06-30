using Colossal.Serialization.Entities;
using CivicSurvival.Core.Serialization;

using CivicSurvival.Core.Interfaces.Services;

namespace CivicSurvival.Domains.Intel.Systems
{
    public partial class IntelStateSystem : IBootDefaultsReset
    {
        public void ResetToBootDefaults(ResetReason reason)
        {
            m_HasInsider = false;
            m_IntelUpgradeLevel = 0;
            m_InvalidEndTimeLogged = false;
        }

        // L13: domain-local version and manual codec prevent silent schema drift
        // when persisted fields are added/reordered.
        private const byte INTEL_SAVE_VERSION = 1;

        public void SetDefaults(Context context) => ResetState();

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, INTEL_SAVE_VERSION);
            try
            {
                var state = new IntelPersistState(m_HasInsider, m_IntelUpgradeLevel, MAX_INTEL_UPGRADE_LEVEL);
                IntelStateCodec.Write(state, writer);
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
            SerializationGuard.LogSerialized(nameof(IntelStateSystem), INTEL_SAVE_VERSION);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, INTEL_SAVE_VERSION, out var version, out var block, nameof(IntelStateSystem)))
            {
                ResetToBootDefaults(ResetReason.VersionMismatch);
                return;
            }
            try
            {
                IntelStateCodec.Read(reader, MAX_INTEL_UPGRADE_LEVEL, out var state);
                m_HasInsider = state.HasInsider;
                m_IntelUpgradeLevel = state.IntelUpgradeLevel;

                Log.Info($"Deserialized v{version}: HasInsider={m_HasInsider}, UpgradeLevel={m_IntelUpgradeLevel}");
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
