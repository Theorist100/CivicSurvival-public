using Colossal.Serialization.Entities;
using CivicSurvival.Core.Serialization;

using CivicSurvival.Core.Interfaces.Services;

namespace CivicSurvival.Domains.Mobilization.Systems
{
    public partial class MobilizationSystem : IBootDefaultsReset
    {
        public void ResetToBootDefaults(ResetReason reason)
        {
            ResetBootDefaultsFields();
            Log.Info($"[BOOT-RESET] system={nameof(MobilizationSystem)} reason={reason}");
        }

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SaveVersions.GLOBAL);
            try
            {
                SerializePersistFields(writer);
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
            SerializationGuard.LogSerialized(nameof(MobilizationSystem), SaveVersions.GLOBAL);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SaveVersions.GLOBAL, out var version, out var block, nameof(MobilizationSystem)))
            {
                ResetToBootDefaults(ResetReason.VersionMismatch);
                return;
            }
            try
            {
                DeserializePersistFields(reader);
                m_CachedBreakdown = default;
                m_CachedPopulation = 0;
                m_LastUpdateFrame = 0;
                m_IsDirty = true;

                // UpdateBreakdown is deferred to ValidateAfterLoad/next OnUpdate, but the
                // runtime singleton/cache must be invalidated immediately. On same-session
                // load the plain MobilizationStateSingleton can still contain the previous
                // full-state while m_UsedManpower has already been restored from the save.
                Log.Info($"Deserialized v{version}: used={m_UsedManpower}, casualties={m_Casualties}, conscription={m_ConscriptionActive}");
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
