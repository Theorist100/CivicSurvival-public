using Colossal.Serialization.Entities;
using CivicSurvival.Core.Components.Domain.AirDefense;
using CivicSurvival.Core.Serialization;
using CivicSurvival.Core.Interfaces.Services;

namespace CivicSurvival.Domains.AirDefense.Systems
{
    /// <summary>
    /// AirDefenseStateSystem owns the DURABLE credit truth (system serializer).
    ///
    /// Model: "system owns persistence, singleton is runtime read-model" (canon C1 —
    /// ScenarioStateMachine). AirDefenseCreditsSingleton is a plain IComponentData
    /// runtime projection (the engine never round-trips it). The 4 credit ints are
    /// persisted HERE, sourced from the system's authoritative live mirror
    /// (m_CreditsLatest). Deserialize only buffers them into m_RestoredCredits — it
    /// does NOT create the entity and does NOT call ResetState (no structural rebuild
    /// from the mid-load-pass; G1 doctrine). The runtime entity is (re)created and
    /// hydrated from m_RestoredCredits in OnLoadRestore (PLVS Phase 2). Policy is owned
    /// and persisted by AirDefensePolicySystem through AirDefensePolicyCodec — not written here (Policy A).
    /// </summary>
    public partial class AirDefenseStateSystem : IDefaultSerializable, IBootDefaultsReset
    {
        public void ResetToBootDefaults(ResetReason reason)
        {
            m_RestoredCredits = AirDefenseCreditsSingleton.Default;
            m_HasRestoredCredits = true;
            m_CreditsLatest = m_RestoredCredits;
        }

        // Durable credits restored from the save, applied to the runtime entity in
        // OnLoadRestore. Transient (reset in ResetState) — never a parallel truth;
        // the system codec block is the only persisted source.
        private AirDefenseCreditsSingleton m_RestoredCredits;
        private bool m_HasRestoredCredits;

        // New-game path only. NOT called from Deserialize (load path) — ResetState does
        // a structural EnsureExists which must not run mid-load-pass (G1 doctrine);
        // the load path recreates+hydrates the entity in OnLoadRestore instead.
        public void SetDefaults(Context context) => ResetState();

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SaveVersions.GLOBAL);
            try
            {
                // Authoritative source = the system's synchronous live mirror, never an
                // entity query (the entity is a runtime projection, not the truth).
                var s = m_CreditsLatest;
                // Policy A: CurrentPolicy intentionally NOT written — AirDefensePolicySystem
                // persists it through AirDefensePolicyCodec as the single policy owner.
                var state = new AirDefenseCreditsPersistState(
                    s.HeritageCredits,
                    s.HeritageCreditsMax,
                    s.DonorPatriotCredits,
                    s.DonorPatriotCreditsMax,
                    s.PatriotInterceptsDrones,
                    s.AutoResupplyEnabled,
                    s.LastResupplyHourHeritage,
                    s.LastResupplyHourBofors,
                    s.LastResupplyHourGepard,
                    s.LastResupplyHourPatriot,
                    s.LastResupplyWavePatriot);
                AirDefenseCreditsCodec.Write(state, writer);
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
            SerializationGuard.LogSerialized(nameof(AirDefenseStateSystem), SaveVersions.GLOBAL);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SaveVersions.GLOBAL, out _, out var block, nameof(AirDefenseStateSystem)))
            {
                ResetToBootDefaults(ResetReason.VersionMismatch);
                return;
            }
            try
            {
                AirDefenseCreditsCodec.Read(reader, out var state);
                var restored = AirDefenseCreditsSingleton.Default;
                restored.HeritageCredits = state.HeritageCredits;
                restored.HeritageCreditsMax = state.HeritageCreditsMax;
                restored.DonorPatriotCredits = state.DonorPatriotCredits;
                restored.DonorPatriotCreditsMax = state.DonorPatriotCreditsMax;
                restored.PatriotInterceptsDrones = state.PatriotInterceptsDrones;
                restored.AutoResupplyEnabled = state.AutoResupplyEnabled;
                restored.LastResupplyHourHeritage = state.LastResupplyHourHeritage;
                restored.LastResupplyHourBofors = state.LastResupplyHourBofors;
                restored.LastResupplyHourGepard = state.LastResupplyHourGepard;
                restored.LastResupplyHourPatriot = state.LastResupplyHourPatriot;
                restored.LastResupplyWavePatriot = state.LastResupplyWavePatriot;

                // Buffer only. No entity create, no ResetState (G1: no structural
                // rebuild from the mid-load-pass). OnLoadRestore (PLVS Phase 2) applies
                // it to the runtime entity. The snapshot is set NOW so UI/DonorConference
                // readers see the correct value before the entity is recreated.
                m_RestoredCredits = restored;
                m_HasRestoredCredits = true;
                m_CreditsLatest = restored;
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
