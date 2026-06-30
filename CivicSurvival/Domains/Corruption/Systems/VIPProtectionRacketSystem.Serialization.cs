using Colossal.Serialization.Entities;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Serialization;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;

using CivicSurvival.Core.Interfaces.Services;

namespace CivicSurvival.Domains.Corruption.Systems
{
    /// <summary>
    /// FIX S4-F05: VIPProtectionRacketSystem serialization.
    /// Persists DayChangedDedup to prevent double VIP income on load.
    /// </summary>
#pragma warning disable S101
    public partial class VIPProtectionRacketSystem : IBootDefaultsReset
#pragma warning restore S101
    {
        private static readonly LogContext SerLog = new("VIPRacket.Serialization");

        public void ResetToBootDefaults(ResetReason reason) => ResetState();

        public void SetDefaults(Context context) => ResetState();

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SaveVersions.GLOBAL);
            try
            {
                VipProtectionRacketCodec.Write(
                    new VipProtectionRacketState(m_DayDedup.LastProcessedDay, m_PendingPayoutDay),
                    writer);
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
            SerializationGuard.LogSerialized(nameof(VIPProtectionRacketSystem), SaveVersions.GLOBAL);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SaveVersions.GLOBAL, out var version, out var block, nameof(VIPProtectionRacketSystem)))
            {
                ResetToBootDefaults(ResetReason.VersionMismatch);
                return;
            }
            try
            {
                VipProtectionRacketCodec.Read(reader, out var state);
                m_DayDedup = DayChangedDedup.FromSave(state.LastProcessedDay);
                m_PendingPayoutDay = state.PendingPayoutDay;
                m_Gate = CreateGate();

                // Always defer the act gate until ScenarioStateMachine has restored
                // CurrentActSingleton; OnCreate may have left a stale PreWar value.
                m_NeedsActReconcile = true;

                SerLog.Info($"Deserialized v{version}: LastProcessedDay={m_DayDedup.LastProcessedDay}, PendingPayoutDay={m_PendingPayoutDay}, Gate={m_Gate.State}");
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
        }
    }
}
