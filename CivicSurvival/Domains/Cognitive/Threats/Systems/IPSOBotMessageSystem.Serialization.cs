using Colossal.Serialization.Entities;
using Unity.Mathematics;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Serialization;

namespace CivicSurvival.Domains.Cognitive.Threats.Systems
{
    /// <summary>
    /// FIX W2-H4: Reset ephemeral latches on load/reset (m_WasActive, m_TimeSinceLastPost) so stale
    /// values can't bypass the reactivation guard and produce instant bot posts.
    ///
    /// The bot RNG (m_Random) IS persisted so the propaganda sequence continues across a save/load
    /// instead of re-seeding. Fresh-world load order is Deserialize → OnCreate, and OnCreate
    /// unconditionally re-seeds m_Random; so Deserialize only STAGES the saved state into
    /// <see cref="m_SavedRandomState"/>, and <see cref="ApplySavedState"/> applies it AFTER the fresh
    /// re-seed — invoked from OnCreate (fresh-world) and OnStartRunning (instance-reuse, OnCreate skipped).
    /// Mirrors PsyOpsLaunchSystem / ThreatSpawnSystem staged-apply.
    /// </summary>
    public partial class IPSOBotMessageSystem : IDefaultSerializable, IResettable, IBootDefaultsReset
    {
        [System.NonSerialized] private uint? m_SavedRandomState;

        public void ResetToBootDefaults(ResetReason reason) => ((IResettable)this).ResetState();

        public void SetDefaults(Context context) => ((IResettable)this).ResetState();

        void IResettable.ResetState()
        {
            m_WasActive = false;
            m_TimeSinceLastPost = 0f;
            m_SavedRandomState = null;
            m_Random = CreateRandom();
        }

        /// <summary>
        /// Applies the RNG state staged by Deserialize after the fresh re-seed that would otherwise
        /// overwrite it. Idempotent — clears the staged value after use.
        /// </summary>
        internal void ApplySavedState()
        {
            if (m_SavedRandomState is uint s)
            {
                m_Random.state = s;
                m_SavedRandomState = null;
            }
        }

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SaveVersions.GLOBAL);
            try
            {
                var state = new IpsoBotMessagePersistState(m_Random.state);
                IpsoBotMessageCodec.Write(state, writer);
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
            SerializationGuard.LogSerialized(nameof(IPSOBotMessageSystem), SaveVersions.GLOBAL);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SaveVersions.GLOBAL, out _, out var block, nameof(IPSOBotMessageSystem)))
            {
                ResetToBootDefaults(ResetReason.VersionMismatch);
                return;
            }
            try
            {
                ((IResettable)this).ResetState();
                IpsoBotMessageCodec.Read(reader, out var state);
                // Xorshift state is never 0; 0 = field missing → keep the fresh seed. Stage only;
                // ApplySavedState (OnCreate/OnStartRunning) applies after OnCreate's re-seed.
                if (state.RandomState != 0)
                    m_SavedRandomState = state.RandomState;
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
