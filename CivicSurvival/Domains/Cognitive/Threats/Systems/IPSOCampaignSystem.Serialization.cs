using Colossal.Serialization.Entities;
using Unity.Entities;
using CivicSurvival.Core.Components.Domain.Cognitive;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Serialization;

namespace CivicSurvival.Domains.Cognitive.Threats.Systems
{
    /// <summary>
    /// Persists the post-wave spike countdown; runtime gate state is rebuilt by the
    /// next ShouldSkipUpdate reconcile, and every other IPSOState field is recomputed
    /// each tick, so only the in-flight spike timer is carried across save/load.
    ///
    /// IPSOState is a plain IComponentData with no engine serializer, so the singleton
    /// is stripped and recreated at Default on load. Deserialize therefore only STAGES
    /// the saved spike into <see cref="m_SavedSpikeTimer"/>; <see cref="ApplySavedState"/>
    /// writes it back onto the (re-created) IPSOState singleton AFTER EnsureExists —
    /// invoked from OnCreate (fresh-world load, Deserialize → OnCreate) and OnStartRunning
    /// (instance-reuse, OnCreate skipped). Mirrors IPSOBotMessageSystem staged-apply.
    /// </summary>
    public partial class IPSOCampaignSystem : IDefaultSerializable, IResettable, IBootDefaultsReset
    {
        [System.NonSerialized] private float? m_SavedSpikeTimer;

        public void ResetToBootDefaults(ResetReason reason)
        {
            InitializeGate();
            m_PendingActApply = false;
            m_PendingActIsActive = false;
            m_PendingActClearExposure = false;
            m_PendingInitialActReconcile = false;
            m_PendingInitialActIsActive = false;
            m_PendingWaveSpike = false;
            m_SavedSpikeTimer = null;
        }

        public void SetDefaults(Context context) => ResetState();
        void IResettable.ResetState() => ResetState();

        private void ResetState()
        {
            InitializeGate();
            // Only the transient managed gate/intent latches reset here. The post-wave
            // spike countdown is carried across load via m_SavedSpikeTimer (staged by
            // Deserialize, re-applied to the singleton by ApplySavedState).
            m_PendingActApply = false;
            m_PendingActIsActive = false;
            m_PendingActClearExposure = false;
            m_PendingInitialActReconcile = false;
            m_PendingInitialActIsActive = false;
            m_PendingWaveSpike = false;
            m_SavedSpikeTimer = null;
        }

        /// <summary>
        /// Writes the staged spike countdown onto the IPSOState singleton after it has
        /// been re-created by EnsureExists. Idempotent — clears the staged value after use.
        /// </summary>
        internal void ApplySavedState()
        {
            if (m_SavedSpikeTimer is not float timer)
                return;

            if (m_IPSOStateQuery.TryGetSingletonEntity<IPSOState>(out var entity)
                && EntityManager.HasComponent<IPSOState>(entity))
            {
                var state = EntityManager.GetComponentData<IPSOState>(entity);
                state.PostWaveSpikeTimer = timer;
                EntityManager.SetComponentData(entity, state);
            }
            m_SavedSpikeTimer = null;
        }

        /// <summary>
        /// Resolves the spike countdown to persist. A wave-end event may have queued a
        /// spike that OnThrottledUpdate has not applied yet (m_PendingWaveSpike); applying
        /// it restarts the timer to full duration, so fold that pending intent into the
        /// saved value — restarting is exactly what would have happened next tick.
        /// </summary>
        private float ResolveSpikeTimerForSave()
        {
            if (m_PendingWaveSpike)
                return POST_WAVE_SPIKE_DURATION;

            if (m_IPSOStateQuery.TryGetSingletonEntity<IPSOState>(out var entity)
                && EntityManager.HasComponent<IPSOState>(entity))
                return EntityManager.GetComponentData<IPSOState>(entity).PostWaveSpikeTimer;

            return 0f;
        }

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SaveVersions.GLOBAL);
            try
            {
                var state = new IpsoCampaignPersistState(ResolveSpikeTimerForSave());
                IpsoCampaignCodec.Write(state, writer);
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
            SerializationGuard.LogSerialized(nameof(IPSOCampaignSystem), SaveVersions.GLOBAL);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SaveVersions.GLOBAL, out _, out var block, nameof(IPSOCampaignSystem)))
            {
                ResetToBootDefaults(ResetReason.VersionMismatch);
                return;
            }
            try
            {
                ResetState();
                IpsoCampaignCodec.Read(reader, out var state);
                // Stage only; ApplySavedState (OnCreate/OnStartRunning) writes it onto the
                // singleton after EnsureExists re-creates it at Default.
                m_SavedSpikeTimer = state.PostWaveSpikeTimer;
                Log.Info("Deserialized: IPSO act gate reset; spike countdown staged; next update will reconcile CurrentActSingleton");
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
