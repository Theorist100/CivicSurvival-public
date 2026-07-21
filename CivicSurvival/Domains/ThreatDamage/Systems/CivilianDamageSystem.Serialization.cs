using Colossal.Serialization.Entities;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Serialization;
using CivicSurvival.Core.Systems;

namespace CivicSurvival.Domains.ThreatDamage.Systems
{
    public partial class CivilianDamageSystem : IDefaultSerializable, IBootDefaultsReset
    {
        public void ResetToBootDefaults(ResetReason reason) => ResetState();

        public void ResetState()
        {
            m_UpdateCounter = UPDATE_INTERVAL;
            if (m_DamageByBuilding.IsCreated) m_DamageByBuilding.Clear();
            m_DamageMapOrderToken = int.MinValue;
            if (m_InFrameEcbHits.IsCreated) m_InFrameEcbHits.Clear();
            if (m_DeletionQueued.IsCreated) m_DeletionQueued.Clear();
            if (m_DamageEntityDeletionQueued.IsCreated) m_DamageEntityDeletionQueued.Clear();
            m_SnapshotScratch.Clear();
            m_PendingActTransitionCleanup = false;
            m_PendingActTransitionPublish = false;
            m_GameHour = 0;
            PublishCivilianDamage(CivicSurvival.Core.Components.CrossDomain.CivilianDamageSnapshot.Empty);
            m_LookupsUpdatedThisFrame = false;
            m_KnownAct = default;
            m_KnownActInitialized = false;
            m_threatGenerationClock = null!;
            Log.Info("ResetState: Reset to fresh state");
        }

        void IResettable.ResetState() => ResetState();

        public void SetDefaults(Context context) => ResetState();

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SaveVersions.GLOBAL);
            try
            {
                EmptyPayloadCodec.Write(writer);
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
            SerializationGuard.LogSerialized(nameof(CivilianDamageSystem), SaveVersions.GLOBAL);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SaveVersions.GLOBAL, out var version, out var block, nameof(CivilianDamageSystem)))
            {
                ResetToBootDefaults(ResetReason.VersionMismatch);
                return;
            }
            try
            {
                EmptyPayloadCodec.Read(reader);
                Log.Info($"Deserialized v{version}");
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

            // m_KnownAct synced in ValidateAfterLoad (ScenarioSingleton may not be deserialized yet)
        }
    }
}
