using System;
using Colossal.Serialization.Entities;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Serialization;
using CivicSurvival.Core.Utils;
using Unity.Collections;

namespace CivicSurvival.Domains.AirDefense.Systems
{
    /// <summary>
    /// Serialization partial for BallisticDefenseSystem.
    /// Persists random state for deterministic intercept rolls after save/load.
    /// </summary>
    public partial class BallisticDefenseSystem : IDefaultSerializable, IResettable, IBootDefaultsReset
    {
        public void ResetToBootDefaults(ResetReason reason) => ResetState();

        public void SetDefaults(Context context) => ResetState();
        void IResettable.ResetState() => ResetState();

        private void ResetState()
        {
            int seed = unchecked(Environment.TickCount ^ s_ProcessId ^ RANDOM_PRIME_SEED);
            m_Random = new SerializableRandom(seed);
            ResetTransientRuntimeState();
        }

        private void ResetTransientRuntimeState()
        {
            if (m_InterceptedThisFrame.IsCreated) m_InterceptedThisFrame.Clear();
            m_InterceptFiredThisFrame = false;
            m_BallisticSnapshotSource = null!;
            m_threatGenerationClock = null!;
            // Pairs the field null-out with the wire reset. Without this, EnsureWired's
            // Ready==true early-return skips re-resolution on the next OnStartRunning and
            // m_BallisticSnapshotSource stays null → NRE in OnUpdateImpl on a reuse-world load.
            m_DependencyWire.Reset();
            m_SpotterPenalty.Invalidate();
        }

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SaveVersions.GLOBAL);
            try
            {
                SerializableRandomCodec.Write(new SerializableRandomState(m_Random.State), writer);
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
            SerializationGuard.LogSerialized(nameof(BallisticDefenseSystem), SaveVersions.GLOBAL);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SaveVersions.GLOBAL, out _, out var block, nameof(BallisticDefenseSystem)))
            {
                ResetToBootDefaults(ResetReason.VersionMismatch);
                return;
            }
            try
            {
#pragma warning disable CIVIC144 // SerializableRandomState is scalar ulong RNG state, not a collection size/capacity
                SerializableRandomCodec.Read(reader, out var randomState);
                m_Random = new SerializableRandom(randomState.State);
#pragma warning restore CIVIC144
                ResetTransientRuntimeState();
                Log.Info("Deserialized: Random state restored");
            }
            catch (System.Exception ex)
            {
                Log.Error($"BallisticDefenseSystem.Deserialize failed: {ex} — resetting to defaults");
                ResetToBootDefaults(ResetReason.DeserializeFailed);
            }
            finally
            {
                SerializationGuard.EndBlock(reader, block);
            }
        }
    }
}
