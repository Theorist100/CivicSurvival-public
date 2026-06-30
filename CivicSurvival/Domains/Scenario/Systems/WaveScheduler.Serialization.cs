using Colossal.Serialization.Entities;
using CivicSurvival.Core.Serialization;
using CivicSurvival.Core.Systems;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;

using CivicSurvival.Core.Interfaces.Services;

namespace CivicSurvival.Domains.Scenario.Systems
{
    /// <summary>
    /// WaveScheduler - Save/Load serialization (IDefaultSerializable).
    /// Persists timing state and Random state across save/load.
    /// </summary>
    public partial class WaveScheduler : IDefaultSerializable, IBootDefaultsReset
    {
        public void ResetToBootDefaults(ResetReason reason)
        {
            m_Initialized = false;
            m_ScenarioStarted = false;
            m_WarStartedReceived = false;
            m_IntroAttackFired = false;
            m_HasSavedState = false;
            m_SavedState = null;
            m_CurrentPhase = GamePhase.Calm;
            m_TimeInPhase = 0f;
            m_PhaseEndTime = 0f;
            m_WaveNumber = 0;
            m_WaveRole = WaveRole.None;
            m_WaveType = WaveType.Harassment;
            m_ThreatsExpected = 0;
            m_LightingMissingWarned = false;
            m_Random = new Unity.Mathematics.Random(0x2001u);
        }

        public void SetDefaults(Context context) => ResetState();

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SaveVersions.GLOBAL);
            try
            {

                var state = GetSaveState();
                var persistState = new WaveSchedulerPersistState(
                    state.Phase,
                    state.TimeInPhase,
                    state.PhaseEndTime,
                    state.WaveNumber,
                    state.WaveRole,
                    state.WaveType,
                    state.ThreatsExpected,
                    state.RandomState,
                    state.ScenarioStarted,
                    state.WarStartedReceived,
                    state.IntroAttackFired);
                WaveSchedulerCodec.Write(persistState, writer);
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
            SerializationGuard.LogSerialized(nameof(WaveScheduler), SaveVersions.GLOBAL);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            m_Initialized = false;

            if (!SerializationGuard.TryBeginBlock(reader, SaveVersions.GLOBAL, out var version, out var block, nameof(WaveScheduler)))
            {
                ResetToBootDefaults(ResetReason.VersionMismatch);
                return;
            }
            try
            {
#pragma warning disable CIVIC144 // WaveSchedulerPersistState is a scalar DTO; bounded fields are validated inside WaveSchedulerCodec.
                WaveSchedulerCodec.Read(reader, out var persistState);
#pragma warning restore CIVIC144
                var phase = persistState.Phase;
                float timeInPhase = persistState.TimeInPhase;
                float phaseEndTime = persistState.PhaseEndTime;
                uint randomState = persistState.RandomState;
                if (randomState == 0)
                {
                    randomState = 0x57415645u;
                    Log.Warn("Deserialized RandomState=0 — using deterministic WaveScheduler recovery seed");
                }

                // C1: Attack now survives save/load. WaveExecutor is the sole owner of the
                // resume-vs-Recovery decision after ThreatLoadRenderReinitSystem publishes the
                // restored live count; scheduler restores its saved phase unchanged.

                SetSaveState(new SchedulerSaveState(
                    phase,
                    timeInPhase,
                    phaseEndTime,
                    persistState.WaveNumber,
                    persistState.WaveRole,
                    persistState.WaveType,
                    persistState.ThreatsExpected,
                    randomState,
                    persistState.ScenarioStarted,
                    persistState.WarStartedReceived,
                    persistState.IntroAttackFired));

                Log.Info($"Deserialized v{version}: Phase={phase}, Wave #{persistState.WaveNumber}, ScenarioStarted={persistState.ScenarioStarted}");
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
