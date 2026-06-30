using Colossal.Serialization.Entities;
using Unity.Mathematics;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Serialization;

namespace CivicSurvival.Domains.Cognitive.Threats.Systems
{
    /// <summary>
    /// FIX W2-H4: Reset ephemeral state on load/reset.
    /// m_WasActive, m_TimeSinceLastPost, m_Random must not survive across loads —
    /// stale values bypass reactivation guard and produce instant bot posts.
    /// No actual data to serialize — just the load-boundary reset hook.
    /// </summary>
    public partial class IPSOBotMessageSystem : IDefaultSerializable, IResettable, IBootDefaultsReset
    {
        public void ResetToBootDefaults(ResetReason reason) => ((IResettable)this).ResetState();

        public void SetDefaults(Context context) => ((IResettable)this).ResetState();

        void IResettable.ResetState()
        {
            m_WasActive = false;
            m_TimeSinceLastPost = 0f;
            m_Random = CreateRandom();
        }

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SaveVersions.GLOBAL);
            try
            {
                var state = new IpsoBotMessagePersistState(m_Random.state);
                IpsoBotMessageCodec.Write(state, writer);
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
            SerializationGuard.LogSerialized(nameof(IPSOBotMessageSystem), SaveVersions.GLOBAL);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SaveVersions.GLOBAL, out _, out var block, nameof(IPSOBotMessageSystem)))
            {
                ResetToBootDefaults(ResetReason.VersionMismatch);
                return;
            }
            try
            {
                ((IResettable)this).ResetState();
                IpsoBotMessageCodec.Read(reader, out var state);
                if (state.RandomState != 0)
                    m_Random.state = state.RandomState;
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
