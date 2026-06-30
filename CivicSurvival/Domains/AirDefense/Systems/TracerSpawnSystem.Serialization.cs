using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Serialization;
using Colossal.Serialization.Entities;

namespace CivicSurvival.Domains.AirDefense.Systems
{
    /// <summary>
    /// R9-M14 FIX: Clear m_SpawnQueue on save/load to prevent ghost tracers
    /// from stale AAFireEvent data surviving across sessions.
    /// No Serialize/Deserialize — tracers are ephemeral (sub-second lifetime).
    /// </summary>
    public partial class TracerSpawnSystem : IDefaultSerializable, IResettable, IBootDefaultsReset
    {
        public void ResetToBootDefaults(ResetReason reason) => ResetState();

        public void SetDefaults(Context context) => ResetState();

        public void ResetState()
        {
            if (m_SpawnQueue.IsCreated)
                m_SpawnQueue.Clear();

            // No cached entity state — tracers are created fresh from AAFireEvent each session.
            m_Random = new Unity.Mathematics.Random((uint)(System.Environment.TickCount ^ 0xAA01));
        }

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SaveVersions.GLOBAL);
            try
            {
                TracerSpawnCodec.Write(new TracerSpawnState(m_Random.state), writer);
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SaveVersions.GLOBAL, out _, out var block, nameof(TracerSpawnSystem)))
            {
                ResetToBootDefaults(ResetReason.VersionMismatch);
                return;
            }

            try
            {
                TracerSpawnCodec.Read(reader, out _);

                ResetState();
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
