using Colossal.Serialization.Entities;
using CivicSurvival.Core.Components.Threats;
using CivicSurvival.Core.Serialization;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;

using CivicSurvival.Core.Interfaces.Services;

namespace CivicSurvival.Domains.Waves.Systems
{
    /// <summary>
    /// WaveExecutor - Save/Load serialization (IDefaultSerializable).
    /// Persists wave state singleton data across save/load.
    /// </summary>
    public partial class WaveExecutor : IDefaultSerializable, IBootDefaultsReset
    {
        public void ResetToBootDefaults(ResetReason reason)
        {
            ResetBootDefaultsFields();
            m_ResetWaveStateAfterLoad = true;
            m_DiscardRestoredThreatsAfterLoad = true;
            ThreatLoadRestorePolicyLatch.ArmDiscardRestoredThreats();
            Log.Info($"[BOOT-RESET] WaveExecutor reason={reason} RestorePolicy=DiscardRestoredThreats");
        }

        public void SetDefaults(Context context) => ResetState();

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SaveVersions.GLOBAL);
            try
            {

                var state = GetSaveState();

                var payload = new WaveExecutorPersistState(
                    state.Phase,
                    state.TimeInPhase,
                    state.PhaseEndTime,
                    state.WaveNumber,
                    state.WaveRole,
                    state.WaveType,
                    state.ThreatsExpected,
                    state.ThreatsSpawned,
                    state.ScenarioStarted);
                WaveExecutorCodec.Write(payload, writer);

            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
            SerializationGuard.LogSerialized(nameof(WaveExecutor), SaveVersions.GLOBAL);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            m_Initialized = false;
            m_PendingScheduleCommands = null;
            m_PendingThreatsSpawned = -1;
            ResetProcessRuntimeRefs();
            ReResolveRuntimeRefs();

            if (!SerializationGuard.TryBeginBlock(reader, SaveVersions.GLOBAL, out var version, out var block, nameof(WaveExecutor)))
            {
                ResetToBootDefaults(ResetReason.VersionMismatch);
                return;
            }
            try
            {
                WaveExecutorCodec.Read(reader, out var payload);
                var phase = payload.Phase;
                float timeInPhase = payload.TimeInPhase;
                float phaseEndTime = payload.PhaseEndTime;
                int waveNumber = payload.WaveNumber;
                var waveRole = payload.WaveRole;
                var waveType = payload.WaveType;
                int threatsExpected = payload.ThreatsExpected;
                int threatsSpawned = payload.ThreatsSpawned;
                bool scenarioStarted = payload.ScenarioStarted;

                // Deserialize stays data-only (C1): threats persist across load, so an interrupted
                // Attack wave is RESUMED (not finalized) in ApplyInitialState once it can count the
                // restored live threats. Only a restore with 0 live threats falls back to Recovery.

                SetSaveState(new ExecutorSaveState(
                    phase,
                    timeInPhase,
                    phaseEndTime,
                    waveNumber,
                    waveRole,
                    waveType,
                    threatsExpected,
                    threatsSpawned,
                    scenarioStarted));
                ThreatLoadRestorePolicyLatch.Clear();
                m_DiscardRestoredThreatsAfterLoad = false;

                Log.Info($"Deserialized v{version}: Phase={phase}, Wave #{waveNumber}");
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
