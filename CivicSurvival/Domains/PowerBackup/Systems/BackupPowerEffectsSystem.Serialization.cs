using Colossal.Serialization.Entities;
using Unity.Mathematics;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Serialization;

namespace CivicSurvival.Domains.PowerBackup.Systems
{
    /// <summary>
    /// BackupPowerEffectsSystem - Save/Load serialization (IDefaultSerializable).
    /// Persists Random state for deterministic fire chance rolls.
    /// Stats (m_Stats) are not serialized - recalculated on each update.
    /// </summary>
    public partial class BackupPowerEffectsSystem : IDefaultSerializable, IResettable, IBootDefaultsReset
    {
        public void ResetToBootDefaults(ResetReason reason)
        {
            m_Random = new Random(DEFAULT_RANDOM_SEED);
            m_HasPendingResults = false;
            m_Stats = default;
            // -1f sentinel (not 0f): IsValidEffectBaseline treats negatives as invalid,
            // so RepairEffectBaselineAfterDeserialize will rebase from real game time
            // once GameTimeSystem activates. 0f would falsely pass validity and freeze
            // the fire-trigger timer at boot.
            m_LastGameTimeForEffects = -1f;
            m_EffectTick = 0;
            InitializeGate();
            // V_REGRESSION Phase 8: per-system m_IgniteQueuedThisFrame retired;
            // shared IFrameMutationDedup is process-lifetime and frame-cleared
            // by FrameMutationDedupClearSystem, no per-reset action needed.
        }

        public void SetDefaults(Context context) => ResetState();
        void IResettable.ResetState() => ResetState();

        private void ResetState()
        {
            Dependency.Complete();
            // FIX S10-01: Re-seed Random in SetDefaults (OnCreate may not re-run on new game)
            m_Random = new Random(DEFAULT_RANDOM_SEED);
            m_HasPendingResults = false;
            m_Stats = default;
            // GameTimeSystem may not be activated yet on SetDefaults (NewGame path before
            // OnGameLoaded). Use the safe accessor and fall back to the -1f sentinel —
            // RepairEffectBaselineAfterDeserialize / OnBecameEnabled will rebase later.
            if (!TryGetCurrentGameTimeSeconds(out m_LastGameTimeForEffects))
                m_LastGameTimeForEffects = -1f;
            m_EffectTick = 0;
            InitializeGate();
            // m_IgniteQueuedThisFrame retired in Phase 8 — see ResetToBootDefaults.
            Log.Info("SetDefaults called — Random re-seeded");
        }

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            // Complete dependency explicitly — cannot assume framework did it before Serialize.
            // Hot path remains non-blocking; save/load and reset are lifecycle boundaries.
            Dependency.Complete();

            var block = SerializationGuard.BeginBlock(writer, SaveVersions.GLOBAL);
            try
            {
                BackupPowerEffectsCodec.Write(
                    new BackupPowerEffectsState(m_Random.state, m_EffectTick, m_LastGameTimeForEffects),
                    writer);
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
            SerializationGuard.LogSerialized(nameof(BackupPowerEffectsSystem), SaveVersions.GLOBAL);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SaveVersions.GLOBAL, out var version, out var block, nameof(BackupPowerEffectsSystem)))
            {
                ResetToBootDefaults(ResetReason.VersionMismatch);
                RepairEffectBaselineAfterDeserialize();
                return;
            }
            try
            {
                BackupPowerEffectsCodec.Read(reader, out var state);
                // The codec normalizes zero RNG state before it reaches this assignment.
                m_Random = default;
                m_Random.state = state.RandomState;
                m_EffectTick = state.EffectTick;
                m_LastGameTimeForEffects = state.LastGameTimeSeconds;

                Log.Info($"Deserialized v{version}: Random state restored");
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

            InitializeGate();
            RepairEffectBaselineAfterDeserialize();
        }

        private void RepairEffectBaselineAfterDeserialize()
        {
            if (!IsValidEffectBaseline(m_LastGameTimeForEffects))
                RebaseEffectBaseline();
        }
    }
}
