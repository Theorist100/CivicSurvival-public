using System;
using Colossal.Serialization.Entities;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Serialization;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Domains.Cognitive.Threats.Systems
{
    /// <summary>
    /// PsyOpsLaunchSystem — Save/Load serialization (IDefaultSerializable).
    /// Persists only the dispatcher RNG (in-flight attacks round-trip as their own
    /// <see cref="Core.Components.Domain.Cognitive.PsyOpsAttack"/> entities). Transient launcher
    /// latches (pending-raid flag, last-raid-wave guard, act gate) reset on load.
    ///
    /// RNG follows the ThreatSpawnSystem pattern: Deserialize stores the saved state, then
    /// <see cref="ApplySavedState"/> applies it after the fresh re-seed that would otherwise
    /// overwrite the loaded value. It is invoked from both OnCreate (fresh-world load, where
    /// Deserialize ran first) and OnStartRunning (instance-reuse load, where OnCreate is skipped).
    /// </summary>
    public partial class PsyOpsLaunchSystem : IDefaultSerializable, IResettable, IBootDefaultsReset
    {
        private ulong? m_SavedRandomState;
        // Staged bank-run interval anchor, applied after ResetTransients (which zeroes the live
        // fields) — same fresh-seed-then-apply ordering as the RNG. Null when no save was loaded.
        private (float hour, bool initialized)? m_SavedBankRun;

        public void SetDefaults(Context context) => ResetState();
        void IResettable.ResetState() => ResetState();

        public void ResetToBootDefaults(ResetReason reason)
        {
            m_SavedRandomState = null;
            m_SavedBankRun = null;
            ResetTransients();
        }

        private void ResetState()
        {
            m_SavedRandomState = null;
            m_Random = new SerializableRandom(Environment.TickCount + 0x5071);
            ResetTransients();
        }

        private void ResetTransients()
        {
            // Accepted edge: a save in the one-frame window between the PsyAttack-phase narrative
            // event and the forced compose loses the pending raid here — the wave's raid is skipped
            // after load. Raids are frequent, so a single miss is unnoticeable (no re-issue on load).
            m_PendingRaid = false;
            m_PendingRaidWave = 0;
            m_LastRaidWave = 0;
            // Zero the live bank-run interval fields; the persisted anchor (absolute TotalGameHours)
            // is restored by ApplySavedState after this, so the drain cadence survives reload rather
            // than re-anchoring to the load moment. The Food run re-derives from the rumor attacks.
            m_BankRunInitialized = false;
            m_LastBankRunHour = 0f;
            m_FoodRunActive = false;
            // The draft-fear lever re-derives from the live Propaganda attacks, same as the Food run.
            m_DraftFearActive = false;
            // Harm probes are diagnostics keyed to game-hour deadlines from the previous session —
            // after a reset those deadlines mean nothing. Drop them; the next landed raid queues
            // fresh ones.
            m_HarmProbes.Clear();
#if DEBUG
            // A dev-panel request queued in the frame a load began belongs to the world that is going
            // away — it must not fire into the world that replaces it.
            m_DebugRaidQueue.Clear();
            m_DebugLandRequested = false;
#endif
            InitializeGate();
        }

        internal void ApplySavedState()
        {
            if (m_SavedRandomState.HasValue)
            {
                m_Random = new SerializableRandom(m_SavedRandomState.Value);
                m_SavedRandomState = null;
            }
            if (m_SavedBankRun.HasValue)
            {
                m_LastBankRunHour = m_SavedBankRun.Value.hour;
                m_BankRunInitialized = m_SavedBankRun.Value.initialized;
                m_SavedBankRun = null;
            }
        }

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SaveVersions.GLOBAL);
            try
            {
                var state = new PsyOpsLaunchPersistState(m_Random.State, m_LastBankRunHour, m_BankRunInitialized);
                PsyOpsLaunchCodec.Write(state, writer);
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
            SerializationGuard.LogSerialized(nameof(PsyOpsLaunchSystem), SaveVersions.GLOBAL);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SaveVersions.GLOBAL, out var version, out var block, nameof(PsyOpsLaunchSystem)))
            {
                ResetToBootDefaults(ResetReason.VersionMismatch);
                return;
            }
            try
            {
                PsyOpsLaunchCodec.Read(reader, out var state);
                // Xorshift state is never 0; 0 = field missing → keep OnCreate's fresh seed.
                if (state.RandomState != 0)
                    m_SavedRandomState = state.RandomState;
                // Stage the persisted bank-run anchor; applied after ResetTransients. Old saves
                // without the keys read initialized=false → fresh anchor on first post-war tick.
                m_SavedBankRun = (state.LastBankRunHour, state.BankRunInitialized);
                ResetTransients();
                Log.Info($"Deserialized v{version}: dispatcher RNG staged for apply (OnCreate/OnStartRunning)");
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
