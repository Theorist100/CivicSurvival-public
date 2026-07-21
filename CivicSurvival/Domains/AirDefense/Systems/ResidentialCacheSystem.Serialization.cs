using Colossal.Serialization.Entities;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Serialization;

namespace CivicSurvival.Domains.AirDefense.Systems
{
    /// <summary>
    /// Serialization partial. The cache is derived from the live ECS query
    /// and is intentionally not persisted (CIVIC150 in the main partial).
    /// We still participate in IDefaultSerializable/IResettable so the
    /// engine drives our own reset on new game and save load — previously
    /// AirDefenseOrchestrator reached in to call ResetState for us, which
    /// NPE'd at deserialize time because the cross-system reference is
    /// resolved later, in OnStartRunning.
    /// </summary>
    public partial class ResidentialCacheSystem : IDefaultSerializable, IResettable, IBootDefaultsReset
    {
        public void ResetToBootDefaults(ResetReason reason) => ResetState();

        public void SetDefaults(Context context) => ResetState();

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            // Forward-compatible empty block — no persisted state today, but
            // future fields can be added without breaking save compatibility.
            var block = SerializationGuard.BeginBlock(writer, SaveVersions.GLOBAL);
            try
            {
                EmptyPayloadCodec.Write(writer);
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            // Drain the empty block when present (older saves with no block
            // simply skip via TryBeginBlock returning false). Either way the
            // cache rebuilds on first OnUpdate; all we owe on load is to
            // clear stale runtime state.
            if (!SerializationGuard.TryBeginBlock(reader, SaveVersions.GLOBAL, out _, out var block, nameof(ResidentialCacheSystem)))
            {
                ResetToBootDefaults(ResetReason.VersionMismatch);
                return;
            }

            try
            {
                EmptyPayloadCodec.Read(reader);
                ResetTransientRuntimeState();
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
