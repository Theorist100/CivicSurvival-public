using Colossal.Serialization.Entities;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Serialization;

namespace CivicSurvival.Domains.AirDefense.Systems
{
    /// <summary>
    /// Persistence for HeritageGrantSystem.
    /// The "already granted" decision is derived at runtime from
    /// AirDefenseCreditsSingleton state.
    /// Only the transient zero-production fallback counter needs to survive load.
    /// </summary>
    public partial class HeritageGrantSystem : IDefaultSerializable, IResettable, IBootDefaultsReset
    {
        public void ResetToBootDefaults(ResetReason reason) => ResetState();

        public void SetDefaults(Context context) => ResetState();
        public void ResetState()
        {
            m_ZeroProductionFrames = 0;
            ResetTransientRuntimeState();
        }

        private void ResetTransientRuntimeState()
        {
            m_GrantResolved = false;
            m_EventBusUnavailableLogged = false;
        }

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SaveVersions.GLOBAL);
            try
            {
                HeritageGrantCodec.Write(new HeritageGrantState(m_ZeroProductionFrames), writer);
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SaveVersions.GLOBAL, out _, out var block, nameof(HeritageGrantSystem)))
            {
                ResetToBootDefaults(ResetReason.VersionMismatch);
                return;
            }
            try
            {
                HeritageGrantCodec.Read(reader, out var state);
                m_ZeroProductionFrames = state.ZeroProductionFrames;
                ResetTransientRuntimeState();
                Log.Info($"Deserialized: m_ZeroProductionFrames={m_ZeroProductionFrames}");
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
