using Colossal.Serialization.Entities;
using CivicSurvival.Core.Serialization;
using CivicSurvival.Core.Types;

using CivicSurvival.Core.Interfaces.Services;

namespace CivicSurvival.Domains.Scenario.Systems
{
    public partial class DefeatCheckSystem : IBootDefaultsReset
    {
        public void ResetToBootDefaults(ResetReason reason) => ResetState();

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SaveVersions.GLOBAL);
            try
            {
                var state = new DefeatCheckPersistState(m_IntegrityBelowThresholdHours);
                DefeatCheckCodec.Write(state, writer);
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SaveVersions.GLOBAL, out _, out var block, nameof(DefeatCheckSystem)))
            {
                ResetToBootDefaults(ResetReason.VersionMismatch);
                return;
            }
            try
            {
                DefeatCheckCodec.Read(reader, out var state);
                m_IntegrityBelowThresholdHours = state.IntegrityBelowThresholdHours;

                // Sync with current state to avoid false trigger on load
                m_LastPostVictoryMode = m_StateMachine?.PostVictoryMode ?? PostVictoryMode.None;
                m_LastCheckedAct = m_StateMachine?.CurrentAct ?? Act.PreWar;

                Log.Info($"Deserialized: IntegrityHours={m_IntegrityBelowThresholdHours:F1}");
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

        public void ResetState()
        {
            m_IntegrityBelowThresholdHours = 0f;
            m_AccumulatedTime = 0f;
            m_LastCheckedAct = Act.PreWar; // FIX S5-07
            m_LastPostVictoryMode = PostVictoryMode.None; // FIX S7-03
        }

        public void SetDefaults(Context context) => ResetState();
    }
}
