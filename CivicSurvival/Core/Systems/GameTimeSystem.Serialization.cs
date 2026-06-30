using Colossal.Serialization.Entities;
using CivicSurvival.Core.Serialization;

using CivicSurvival.Core.Interfaces.Services;

namespace CivicSurvival.Core.Systems
{
    public partial class GameTimeSystem : IBootDefaultsReset
    {
        public void ResetToBootDefaults(ResetReason reason) => ResetState();

        public void ResetState()
        {
            DeactivateTimePipeline("ResetState");
            m_LastGameHour = 0f;
            m_LastNormalizedTime = 0f;
            m_CurrentDay = 0;
            m_WarStarted = false;
            m_WarStartGameDay = 0;
            m_WarDay = 0;
            m_SkipFirstMidnightCheck = true;
            m_LastVanillaDay = -1; // Re-seed on first frame
            // FIX #70: Rebuild snapshot so Current reflects defaults
            UpdateStateSnapshot();
        }

        public void SetDefaults(Context context) => ResetState();

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SaveVersions.GLOBAL);
            try
            {
                GameTimeCodec.Write(
                    new GameTimePersistState(
                        m_LastGameHour,
                        m_LastNormalizedTime,
                        m_CurrentDay,
                        m_WarStarted,
                        m_WarStartGameDay),
                    writer);
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
            Log.Info($"[GameTimeSystem] Serialized: hour={m_LastGameHour:F2}, day={m_CurrentDay}, warDay={m_WarDay}");
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SaveVersions.GLOBAL, out var version, out var block, nameof(GameTimeSystem)))
            {
                ResetToBootDefaults(ResetReason.VersionMismatch);
                return;
            }
            try
            {
                GameTimeCodec.Read(reader, out var state);
                m_LastGameHour = state.LastGameHour;
                m_LastNormalizedTime = state.LastNormalizedTime;
                m_CurrentDay = state.CurrentDay;
                m_WarStarted = state.WarStarted;
                m_WarStartGameDay = state.WarStartGameDay;

                // Recompute derived field before snapshot (m_WarDay is not persisted)
                if (m_WarStarted)
                    m_WarDay = m_CurrentDay - m_WarStartGameDay;

                Log.Info($"[GameTimeSystem] Deserialized v{version}: hour={m_LastGameHour:F2}, day={m_CurrentDay}, warStarted={m_WarStarted}, warDay={m_WarDay}");

                m_SkipFirstMidnightCheck = true;
                m_LastVanillaDay = -1; // Re-seed on first frame after load

                // FIX #70: Rebuild snapshot immediately so systems reading Current get correct values
                UpdateStateSnapshot();
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
