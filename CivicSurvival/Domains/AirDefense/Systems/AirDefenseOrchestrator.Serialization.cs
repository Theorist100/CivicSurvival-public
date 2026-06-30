using Colossal.Serialization.Entities;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Serialization;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Domains.AirDefense.Systems
{
    /// <summary>
    /// Serialization partial for AirDefenseOrchestrator.
    /// Persists random state for deterministic continuation after save/load.
    /// </summary>
    public partial class AirDefenseOrchestrator : IDefaultSerializable, IBootDefaultsReset
    {
        public void ResetToBootDefaults(ResetReason reason)
        {
            ResetRuntimeFields(reseedRandom: true);
            Log.Info($"[BOOT-RESET] system={nameof(AirDefenseOrchestrator)} reason={reason}");
        }

        public void SetDefaults(Context context)
        {
            ResetState();
        }

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SaveVersions.GLOBAL);
            try
            {
                SerializableRandomCodec.Write(new SerializableRandomState(m_Random.State), writer);
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
            SerializationGuard.LogSerialized(nameof(AirDefenseOrchestrator), SaveVersions.GLOBAL);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SaveVersions.GLOBAL, out _, out var block, nameof(AirDefenseOrchestrator)))
            {
                ResetToBootDefaults(ResetReason.VersionMismatch);
                return;
            }
            try
            {
                ResetRuntimeFields(reseedRandom: false);
#pragma warning disable CIVIC144 // SerializableRandomState is scalar ulong RNG state, not a collection size/capacity
                SerializableRandomCodec.Read(reader, out var randomState);
                m_Random = new SerializableRandom(randomState.State);
#pragma warning restore CIVIC144
                Log.Info("Deserialized: Random state restored");
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
