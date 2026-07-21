using Colossal.Serialization.Entities;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Serialization;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Domains.Finance.Systems
{
    public partial class WarDamageDebtSystem : IDefaultSerializable, IResettable, IBootDefaultsReset
    {
        public void ResetToBootDefaults(ResetReason reason) => ResetState();

        public void SetDefaults(Context context) => ResetState();

        void IResettable.ResetState() => ResetState();

        private void ResetState()
        {
            m_LastChargedWaveNumber = -1;
            m_LastSettledWaveNumber = -1;
            m_SettledWaveExceptions.Clear();
            m_PendingWaveNumber = -1;
            m_PendingDamageCost = 0;
        }

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SaveVersions.GLOBAL);
            try
            {
                var state = new WarDamageDebtPersistState(
                    m_LastChargedWaveNumber,
                    m_LastSettledWaveNumber,
                    m_PendingWaveNumber,
                    m_PendingDamageCost);
                WarDamageDebtCodec.Write(state, writer);
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
            SerializationGuard.LogSerialized(nameof(WarDamageDebtSystem), SaveVersions.GLOBAL);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SaveVersions.GLOBAL, out _, out var block, nameof(WarDamageDebtSystem)))
            {
                ResetToBootDefaults(ResetReason.VersionMismatch);
                return;
            }
            try
            {
                WarDamageDebtCodec.Read(reader, out var state);
                m_LastChargedWaveNumber = state.LastChargedWaveNumber;
                m_LastSettledWaveNumber = state.LastSettledWaveNumber;
                m_SettledWaveExceptions.Clear();
                m_PendingWaveNumber = state.PendingWaveNumber;
                m_PendingDamageCost = state.PendingDamageCost;
                Log.Info($"Deserialized: LastChargedWaveNumber={m_LastChargedWaveNumber}, LastSettledWaveNumber={m_LastSettledWaveNumber}, PendingWave={m_PendingWaveNumber}, PendingCost=${m_PendingDamageCost:N0}");
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
