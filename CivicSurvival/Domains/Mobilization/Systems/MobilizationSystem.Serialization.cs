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
                // Reset BEFORE reading, not after: the generated reader skips a field whose tag it
                // does not meet and leaves the previous value in place. CS2 reuses system instances
                // across an in-game load, so "previous value" means the previous CITY's — a save
                // written before a field existed would hand this city the last one's answer, and
                // m_ManpowerCritical alone would carry a critical verdict from a city that is no
                // longer loaded. Every [Persist] field starts from its declared default here, and
                // only what the save actually carries overwrites it.
                ResetPersistFields();
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
