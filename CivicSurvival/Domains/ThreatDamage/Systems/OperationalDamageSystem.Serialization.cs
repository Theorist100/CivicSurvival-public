using Colossal.Serialization.Entities;
using CivicSurvival.Core.Serialization;
using CivicSurvival.Core.Systems;

using CivicSurvival.Core.Interfaces.Services;

namespace CivicSurvival.Domains.ThreatDamage.Systems
{
    public partial class OperationalDamageSystem : IDefaultSerializable, IBootDefaultsReset
    {
        public void ResetToBootDefaults(ResetReason reason)
        {
            m_GameTime = 0.0;
            m_UpdateCounter = UPDATE_INTERVAL;
            if (m_DamageByBuilding.IsCreated) m_DamageByBuilding.Clear();
            m_DamageMapOrderToken = int.MinValue;
            if (m_InFrameEcbHits.IsCreated) m_InFrameEcbHits.Clear();
            if (m_DeletionQueued.IsCreated) m_DeletionQueued.Clear();
            if (m_DamageEntityDeletionQueued.IsCreated) m_DamageEntityDeletionQueued.Clear();
            m_PendingActTransitionCleanup = false;
            m_KnownAct = default;
            m_KnownActInitialized = false;
            m_threatGenerationClock = null!;
        }

        /// <summary>
        /// Reset serializable state to defaults.
        /// Called on new game and when save version is incompatible.
        /// </summary>
        public void ResetState()
        {
            // GameTimeSystem may not be activated yet on SetDefaults (vanilla calls
            // SetDefaults before OnGameLoaded). When unavailable, 0.0 baseline is
            // self-correcting: first OnUpdateImpl writes the real current time.
            m_GameTime = GameTimeSystem.TryGetGameHours(out var hours) ? hours : 0.0;
            // M4 FIX: Use UPDATE_INTERVAL (not 0) for immediate sync, consistent with ValidateAfterLoad/OnActChanged
            m_UpdateCounter = UPDATE_INTERVAL;
            if (m_DamageByBuilding.IsCreated) m_DamageByBuilding.Clear();
            m_DamageMapOrderToken = int.MinValue;
            if (m_InFrameEcbHits.IsCreated) m_InFrameEcbHits.Clear();
            if (m_DeletionQueued.IsCreated) m_DeletionQueued.Clear();
            if (m_DamageEntityDeletionQueued.IsCreated) m_DamageEntityDeletionQueued.Clear();
            m_PendingActTransitionCleanup = false;
            m_KnownAct = default;
            m_KnownActInitialized = false;
            m_threatGenerationClock = null!;
            Log.Info("ResetState: Reset to fresh state");
        }

        public void SetDefaults(Context context) => ResetState();

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SaveVersions.GLOBAL);
            try
            {
                var state = new OperationalDamagePersistState(m_GameTime);
                OperationalDamageCodec.Write(state, writer);
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
            SerializationGuard.LogSerialized(nameof(OperationalDamageSystem), SaveVersions.GLOBAL);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SaveVersions.GLOBAL, out var version, out var block, nameof(OperationalDamageSystem)))
            {
                ResetToBootDefaults(ResetReason.VersionMismatch);
                return;
            }
            try
            {
                OperationalDamageCodec.Read(reader, out var state);
                m_GameTime = state.GameTime;

                Log.Info($"Deserialized v{version}: GameTime={m_GameTime:F2}h");
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
