using Colossal.Serialization.Entities;
using CivicSurvival.Core.Serialization;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Domains.Scenario.Systems
{
    /// <summary>
    /// CrisisActCoordinator - Save/Load serialization (IDefaultSerializable).
    /// Persists crisis act state (pure orchestration only).
    /// </summary>
    public partial class CrisisActCoordinator : IDefaultSerializable, IResettable, IBootDefaultsReset
    {
        public void ResetToBootDefaults(ResetReason reason) => ResetState();

        public void SetDefaults(Context context) => ResetState();

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SaveVersions.GLOBAL);
            try
            {
                var state = new CrisisActCoordinatorPersistState(
                    m_CrisisActive,
                    m_CrisisStartDay,
                    m_HasCrisisStartDay,
                    m_CurrentDay,
                    m_WavesSurvived,
                    m_PopulationAtStart,
                    m_IntroWaveThreatCount,
                    m_HasPendingTransition,
                    m_PreviousAct,
                    m_BankingChirpSent);
                CrisisActCoordinatorCodec.Write(state, writer);
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
            SerializationGuard.LogSerialized(nameof(CrisisActCoordinator), SaveVersions.GLOBAL);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SaveVersions.GLOBAL, out var version, out var block, nameof(CrisisActCoordinator)))
            {
                ResetToBootDefaults(ResetReason.VersionMismatch);
                return;
            }
            try
            {
                CrisisActCoordinatorCodec.Read(reader, out var state);
                m_CrisisActive = state.CrisisActive;
                m_CrisisStartDay = state.CrisisStartDay;
                m_HasCrisisStartDay = state.HasCrisisStartDay;
                m_CurrentDay = state.CurrentDay;
                m_WavesSurvived = state.WavesSurvived;
                m_PopulationAtStart = state.PopulationAtStart;
                m_IntroWaveThreatCount = state.IntroWaveThreatCount;
                m_HasPendingTransition = state.HasPendingTransition;
                m_PreviousAct = state.PreviousAct;
                m_BankingChirpSent = state.BankingChirpSent;

                if (m_HasPendingTransition)
                {
                    Log.Info("Clearing orphaned pending act transition after load; transition will be re-evaluated");
                    m_HasPendingTransition = false;
                }

                Log.Info($"Deserialized v{version}: Active={m_CrisisActive}, Day={m_CurrentDay}");

                // Defer ActChangedEvent replay until post-load validation (subscribers not ready during Deserialize).
                // M-29 FIX: re-publish for ALL acts, not just crisis — systems like MilestoneTutorialSystem
                // subscribe to ActChangedEvent for Exodus/Adaptation modals.
                m_NeedRePublishAct = true;
                Log.Info($"Will re-publish ActChangedEvent after load (active={m_CrisisActive})");
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
            m_CrisisActive = false;
            m_CrisisStartDay = 0;
            m_HasCrisisStartDay = false;
            m_CurrentDay = 0;
            m_WavesSurvived = 0;
            m_PopulationAtStart = 0f;
            m_CurrentPopulation = 0f;
            m_HasPendingTransition = false;
            m_PreviousAct = Act.PreWar;
            m_NeedRePublishAct = false;
            m_IntroWaveThreatCount = 0;
            m_BankingChirpSent = false;
            Log.Info("State reset");
        }
    }
}
