using Colossal.Serialization.Entities;
using Unity.Entities;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Serialization;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Domains.Corruption.Systems
{
    /// <summary>
    /// CounterfeitBatteryFireSystem serialization.
    /// Persists DayChangedDedup to prevent double fire check on load.
    /// </summary>
    public partial class CounterfeitBatteryFireSystem : IBootDefaultsReset
    {
        public void ResetToBootDefaults(ResetReason reason) => ResetState();

        private static readonly LogContext SerLog = new("CounterfeitFire.Serialization");

        public void SetDefaults(Context context) => ResetState();

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SaveVersions.GLOBAL);
            try
            {
                var firedToday = new EntityPersistEntry[m_FiredTodayEntities.Count];
                int i = 0;
                foreach (var entity in m_FiredTodayEntities)
                {
                    firedToday[i++] = new EntityPersistEntry(entity.Index, entity.Version);
                }

                var state = new CounterfeitBatteryFirePersistState(
                    m_DayDedup.LastProcessedDay,
                    firedToday);
                CounterfeitBatteryFireCodec.Write(state, writer);
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
            SerializationGuard.LogSerialized(nameof(CounterfeitBatteryFireSystem), SaveVersions.GLOBAL);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SaveVersions.GLOBAL, out var version, out var block, nameof(CounterfeitBatteryFireSystem)))
            {
                ResetToBootDefaults(ResetReason.VersionMismatch);
                return;
            }
            try
            {
                CounterfeitBatteryFireCodec.Read(reader, out var state);
                ApplyPersistState(state);
                SerLog.Info($"Deserialized v{version}: LastProcessedDay={m_DayDedup.LastProcessedDay}, FiredToday={m_FiredTodayEntities.Count}");
            }
            catch (System.Exception ex)
            {
                SerLog.Error($"Deserialize failed: {ex}");
                ResetToBootDefaults(ResetReason.DeserializeFailed);
            }
            finally
            {
                SerializationGuard.EndBlock(reader, block);
            }

            // Act gate is derived from CurrentActSingleton by runtime ingress; it is
            // not serialized. Reset to AwaitingActState for the next OnUpdateImpl().
            InitializeGate();
        }

        private void ApplyPersistState(in CounterfeitBatteryFirePersistState state)
        {
            m_FiredTodayEntities.Clear();
            for (int i = 0; i < state.FiredToday.Count; i++)
            {
                var entity = state.FiredToday[i];
                m_FiredTodayEntities.Add(new Entity { Index = entity.Index, Version = entity.Version });
            }

            m_DayDedup = DayChangedDedup.FromSave(state.LastProcessedDay);
        }

        public void ResetState()
        {
            m_DayDedup.Reset();
            m_FiredTodayEntities.Clear();
            m_ActiveModernizationDistricts.Clear();
            m_ModernizationProgramsObserverCursor = -1;
            // V_REGRESSION Phase 8: per-system m_IgniteQueuedThisFrame retired;
            // shared IFrameMutationDedup is frame-cleared by
            // FrameMutationDedupClearSystem.
            m_NameService = null;
            InitializeGate();
        }
    }
}
