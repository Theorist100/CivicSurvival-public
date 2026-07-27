using Colossal.Serialization.Entities;
using CivicSurvival.Core.Serialization;
using CivicSurvival.Core.Utils;

using CivicSurvival.Core.Interfaces.Services;

namespace CivicSurvival.Domains.Spotters.Systems
{
    /// <summary>
    /// Serialization partial for SpotterSpawnSystem.
    /// Handles save/load of spawn timer and random state.
    /// </summary>
    public partial class SpotterSpawnSystem : IDefaultSerializable, IBootDefaultsReset
    {
        private const int DESERIALIZE_RANDOM_FALLBACK_SEED = 0x5A10_5A10;

        public void ResetToBootDefaults(ResetReason reason) => ResetState();

        public void SetDefaults(Context context)
        {
            ResetState();
        }

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SaveVersions.GLOBAL);
            try
            {
                var state = new SpotterSpawnPersistState(m_SpawnTimer);
                SpotterSpawnCodec.Write(state, writer);
                SerializableRandomCodec.Write(new SerializableRandomState(m_Random.State), writer);
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
            SerializationGuard.LogSerialized(nameof(SpotterSpawnSystem), SaveVersions.GLOBAL);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SaveVersions.GLOBAL, out _, out var block, nameof(SpotterSpawnSystem)))
            {
                ResetToBootDefaults(ResetReason.VersionMismatch);
                return;
            }
            try
            {
                SpotterSpawnCodec.Read(reader, out var state);
                m_SpawnTimer = state.SpawnTimer;
                try
                {
#pragma warning disable CIVIC144 // SerializableRandomState is the scalar payload for SerializableRandomCodec.
                    SerializableRandomCodec.Read(reader, out var randomState);
#pragma warning restore CIVIC144
                    m_Random = new SerializableRandom(randomState.State);
                }
                catch (System.Exception ex)
                {
                    Log.Warn($"Random deserialize failed, reseeding only RNG: {ex}");
                    m_Random = new SerializableRandom(DESERIALIZE_RANDOM_FALLBACK_SEED);
                }
                System.Threading.Interlocked.Exchange(ref m_PendingImpactSpawnCount, 0);
                DrainPendingDistrictQueue(m_PendingBlackoutDistricts);
                DrainPendingDistrictQueue(m_PendingVipDistricts);
                m_Initialized = true;

                Log.Info($"Deserialized: SpawnTimer={m_SpawnTimer:F1}");
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

        private static void DrainPendingDistrictQueue(System.Collections.Concurrent.ConcurrentQueue<int> queue)
        {
            while (queue.TryDequeue(out _))
            {
                // Drain transient event intents after load/default reset.
            }
        }
    }
}
