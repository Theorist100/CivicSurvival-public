using Colossal.Serialization.Entities;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Serialization;
using CivicSurvival.Core.Services.Economy;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;
using Unity.Entities;

using CivicSurvival.Core.Interfaces.Services;

namespace CivicSurvival.Domains.ShadowEconomy.Systems
{
    /// <summary>
    /// Persists DayChangedDedup + ShadowImportState/ShadowExportState components.
    /// System-level block first, then component-level block via ShadowTradeSerializer.
    /// </summary>
    public partial class ShadowTradeDailySystem : IBootDefaultsReset
    {
        [System.NonSerialized] private bool m_PendingBootDefaultComponentReset;

        public void ResetToBootDefaults(ResetReason reason)
        {
            m_DayDedup.Reset();
            m_ImportDeductFailed = false;
            m_PendingPostLoadImportProjection = false;
            m_LastShadowCapMW = int.MinValue;
            m_PendingBootDefaultComponentReset = true;
            InitializeGate();
        }

        private static readonly LogContext SerLog = new("ShadowTradeDaily.Serialization");

        public void ResetState()
        {
            m_DayDedup.Reset();
            m_ImportDeductFailed = false;
            m_PendingPostLoadImportProjection = false;
            m_LastShadowCapMW = int.MinValue;
            ResetComponentState();
            InitializeGate();
        }

        private void ResetComponentState()
        {
            ShadowImportState.EnsureExists(EntityManager);
            if (m_SingletonQuery.TryGetSingletonEntity<ShadowImportState>(out var entity))
            {
                EntityManager.SetComponentData(entity, ShadowImportState.CreateDefault());
                EntityManager.SetComponentData(entity, ShadowExportState.CreateDefault());
            }
        }

        public void SetDefaults(Context context) => ResetState();

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            // System-level state
            var sysBlock = SerializationGuard.BeginBlock(writer, SaveVersions.GLOBAL);
            try
            {
                var state = new ShadowTradeDailyPersistState(m_DayDedup.LastProcessedDay);
                ShadowTradeDailyCodec.Write(state, writer);
            }
            finally
            {
                SerializationGuard.EndBlock(writer, sysBlock);
            }
            SerializationGuard.LogSerialized(nameof(ShadowTradeDailySystem), SaveVersions.GLOBAL);

            // Component-level state (replaces old ShadowTradeState : ISerializable)
            ShadowImportState import;
            ShadowExportState export;

            ShadowImportState.EnsureExists(EntityManager);
            if (m_SingletonQuery.TryGetSingletonEntity<ShadowImportState>(out var entity))
            {
                import = EntityManager.GetComponentData<ShadowImportState>(entity);
                export = EntityManager.GetComponentData<ShadowExportState>(entity);
            }
            else
            {
                SerLog.Error("Serialize: ShadowTradeState singleton missing after EnsureExists; writing defaults as explicit error fallback");
                import = ShadowImportState.CreateDefault();
                export = ShadowExportState.CreateDefault();
            }

            ShadowTradeSerializer.WriteAll(writer, in import, in export);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            // System-level state
            if (!SerializationGuard.TryBeginBlock(reader, SaveVersions.GLOBAL, out var version, out var block, nameof(ShadowTradeDailySystem)))
            {
                ResetToBootDefaults(ResetReason.VersionMismatch);
                DrainComponentBlock(reader);
                return;
            }
            bool sysStateOk = false;
            try
            {
                ShadowTradeDailyCodec.Read(reader, out var state);
                m_DayDedup = DayChangedDedup.FromSave(state.LastProcessedDay);
                m_ImportDeductFailed = false;
                m_LastShadowCapMW = int.MinValue;
                SerLog.Info($"Deserialized v{version}: LastProcessedDay={m_DayDedup.LastProcessedDay}, ImportDeductFailed reset");
                sysStateOk = true;
            }
            catch (System.Exception ex)
            {
                SerLog.Error($"Deserialize system state failed: {ex}");
                ResetToBootDefaults(ResetReason.DeserializeFailed);
            }
            finally
            {
                SerializationGuard.EndBlock(reader, block);
            }

            if (!sysStateOk)
            {
                DrainComponentBlock(reader);
                return;
            }

            // Component-level state (replaces old ShadowTradeState : ISerializable)
            try
            {
                ShadowTradeSerializer.ReadAll(reader, out var import, out var export);

                ShadowImportState.EnsureExists(EntityManager);
                if (m_SingletonQuery.TryGetSingletonEntity<ShadowImportState>(out var entity))
                {
                    EntityManager.SetComponentData(entity, import);
                    EntityManager.SetComponentData(entity, export);
                }
                else
                {
                    SerLog.Error("Deserialize: ShadowTradeState singleton missing after EnsureExists; restored component payload could not be applied");
                    ResetComponentState();
                }

                SerLog.Info($"Deserialized components: Import={import.ImportMW}MW, Export={export.ExportPercentage}%");
            }
            catch (System.Exception ex)
            {
                SerLog.Error($"Deserialize component state failed: {ex}");
                ResetComponentState();
            }

            // Act gate is derived from CurrentActSingleton by runtime ingress; it is
            // not serialized. Reset to AwaitingActState for the next RefreshGate().
            InitializeGate();
        }

        private static void DrainComponentBlock<TReader>(TReader reader) where TReader : IReader
        {
            try
            {
                var block = reader.Begin(out _);
                reader.End(block);
            }
            catch (System.Exception ex)
            {
                SerLog.Error($"Drain component state failed: {ex}");
            }
        }
    }
}
