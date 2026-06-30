using Colossal.Serialization.Entities;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Serialization;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;
using CivicSurvival.Core.Interfaces.Services;

namespace CivicSurvival.Domains.Attention.Systems
{
    /// <summary>
    /// ExodusSystem - Save/Load serialization (IDefaultSerializable).
    /// Persists exodus statistics and rate override.
    /// </summary>
    public partial class ExodusSystem : IDefaultSerializable, IBootDefaultsReset
    {
        public void ResetToBootDefaults(ResetReason reason)
        {
            m_TotalExodusThisSession = 0;
            m_BaseExodusRatePercentPerDay = 0f;
            m_EffectiveExodusRatePercentPerDay = 0f;
            m_PeakPopulation = 0;
            m_LastProcessedAct = Act.PreWar;
            m_DayDedup.Reset();
            m_ResidentHouseholdObserverVersion = 0;
            m_ResidentPopulationObserverVersion = 0;
            m_Random = new Unity.Mathematics.Random(1u);
            m_Singleton.Invalidate();
        }

        public void SetDefaults(Context context)
        {
            ResetState();
        }

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SaveVersions.GLOBAL);
            try
            {
                var state = new ExodusPersistState(
                    m_TotalExodusThisSession,
                    m_BaseExodusRatePercentPerDay,
                    m_EffectiveExodusRatePercentPerDay,
                    m_Random.state,
                    m_PeakPopulation,
                    m_LastProcessedAct,
                    m_DayDedup.LastProcessedDay);
                ExodusCodec.Write(state, writer);

            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
            SerializationGuard.LogSerialized(nameof(ExodusSystem), SaveVersions.GLOBAL);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SaveVersions.GLOBAL, out var version, out var block, nameof(ExodusSystem)))
            {
                ResetToBootDefaults(ResetReason.VersionMismatch);
                return;
            }
            try
            {
                ResetState();

                ExodusCodec.Read(reader, out var state);
                m_TotalExodusThisSession = state.TotalExodus;
                m_BaseExodusRatePercentPerDay = state.BaseRatePercentPerDay;
                m_EffectiveExodusRatePercentPerDay = state.EffectiveRatePercentPerDay;
                m_PeakPopulation = state.PeakPopulation;
                m_LastProcessedAct = state.LastProcessedAct;
                m_Random = default;
                m_Random.state = state.RandomState;
                m_DayDedup = DayChangedDedup.FromSave(state.LastProcessedDay);

                Log.Info($"Deserialized v{version}: TotalExodus={m_TotalExodusThisSession}, PeakPop={m_PeakPopulation}, LastProcessedDay={m_DayDedup.LastProcessedDay}, LastAct={m_LastProcessedAct}");
                UpdateSingleton();
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
