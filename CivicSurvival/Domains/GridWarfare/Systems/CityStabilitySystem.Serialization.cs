using Colossal.Serialization.Entities;
using CivicSurvival.Core.Serialization;

using CivicSurvival.Core.Interfaces.Services;

namespace CivicSurvival.Domains.GridWarfare.Systems
{
    public partial class CityStabilitySystem : IBootDefaultsReset
    {
        public void ResetToBootDefaults(ResetReason reason) => ResetState();

        public void ResetState()
        {
            m_Stability = CityStabilityCodec.DefaultStability;
        }

        public void SetDefaults(Context context) => ResetState();

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SaveVersions.GLOBAL);
            try
            {
                var state = new CityStabilityPersistState(m_Stability);
                CityStabilityCodec.Write(state, writer);
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SaveVersions.GLOBAL, out var version, out var block, nameof(CityStabilitySystem)))
            {
                ResetToBootDefaults(ResetReason.VersionMismatch);
                return;
            }
            try
            {
                CityStabilityCodec.Read(reader, out var state);
                m_Stability = state.Stability;

                Log.Info($"Deserialized v{version}: Stability={m_Stability:P0}");
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
