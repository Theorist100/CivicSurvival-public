using Colossal.Serialization.Entities;
using CivicSurvival.Core.Serialization;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.Types;
using CivicSurvival.Domains.Scenario.Data;
using CivicSurvival.Core.Interfaces.Services;

namespace CivicSurvival.Domains.Scenario.Systems
{
    /// <summary>
    /// IntroScenarioSystem - Save/Load serialization (IDefaultSerializable).
    /// Persists intro sequence state including timer for mid-intro saves.
    /// </summary>
    public partial class IntroScenarioSystem : IDefaultSerializable, IBootDefaultsReset
    {
        public void ResetToBootDefaults(ResetReason reason)
        {
            m_State = default;
            m_SavedSpeed = 1f;
            m_ChirperStorm = null;
            m_NeedDeferredIntroComplete = false;
            m_NeedIntroAttackReplay = false;
            m_NeedIntroModalRestore = false;
            m_NeedIntroModalDismiss = false;
            m_NeedSpeedRestore = false;
            m_JustLoaded = false;
            m_NeedBootDefaultIntroSelfHeal = false;
        }

        public void SetDefaults(Context context) => ResetState();

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SaveVersions.GLOBAL);
            try
            {
                bool hasChirperStorm = m_ChirperStorm != null;
                var chirperStormState = default(IntroChirperStormPersistState);
                if (hasChirperStorm)
                {
                    var (elapsed, nextIndex, active) = m_ChirperStorm!.GetState();
                    chirperStormState = new IntroChirperStormPersistState(elapsed, nextIndex, active);
                }

                var state = new IntroScenarioPersistState(
                    m_State.IntroCompleted,
                    m_State.IsIntroPlaying,
                    m_State.IntroPhase,
                    m_State.IntroTimer,
                    m_SavedSpeed,
                    m_State.SkipIntro,
                    hasChirperStorm,
                    chirperStormState);
                IntroScenarioCodec.Write(state, writer);
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
            SerializationGuard.LogSerialized(nameof(IntroScenarioSystem), SaveVersions.GLOBAL);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SaveVersions.GLOBAL, out _, out var block, nameof(IntroScenarioSystem)))
            {
                ResetToBootDefaults(ResetReason.VersionMismatch);
                m_NeedBootDefaultIntroSelfHeal = true;
                return;
            }
            try
            {
                m_ChirperStorm = null;
                m_NeedIntroAttackReplay = false;
                m_NeedBootDefaultIntroSelfHeal = false;

                IntroScenarioCodec.Read(reader, out var state);
                m_State.IntroCompleted = state.IntroCompleted;
                m_State.IsIntroPlaying = state.IsIntroPlaying;
                m_State.IntroPhase = state.IntroPhase;
                m_State.IntroTimer = state.IntroTimer;
                m_SavedSpeed = state.SavedSpeed;
                m_State.SkipIntro = state.SkipIntro;
                if (state.HasChirperStorm)
                {
                    m_ChirperStorm = new Logic.ChirperStormScheduler();
                    m_ChirperStorm.RestoreState(
                        state.ChirperStorm.Elapsed,
                        state.ChirperStorm.NextIndex,
                        state.ChirperStorm.Active);
                }

                // G10-13: defer the modal show + pause to the first post-load update
                // (deferred-flag pattern, sibling of m_NeedIntroAttackReplay /
                // m_NeedSpeedRestore). Deserialize must stay data-only — no
                // ModalCoordinator / selectedSpeed runtime side effects here. Re-show
                // semantics are unchanged (a save during an unaccepted intro resumes
                // it); only the side-effect location moves.
                if (m_State.IsIntroPlaying && m_State.IntroPhase == IntroPhase.Modal)
                    m_NeedIntroModalRestore = true;

                // BUG-S-012 FIX: Complete mid-intro saves instead of re-showing modal.
                if (m_State.IsIntroPlaying && m_State.IntroPhase != IntroPhase.Modal)
                {
                    if (m_State.IntroPhase < IntroPhase.Attack)
                        m_NeedIntroAttackReplay = true;

                    m_State.IsIntroPlaying = false;
                    m_State.IntroCompleted = true;
                    m_State.IntroPhase = IntroPhase.Done;
                    // G10-13: Dismiss is a ModalCoordinator runtime side effect — defer
                    // it out of Deserialize (data-only doctrine), like m_NeedSpeedRestore.
                    m_NeedIntroModalDismiss = true;
                    m_NeedSpeedRestore = true;
                    Log.Info("Mid-intro save detected, skipping intro on load");
                }

                // BUG-S-016 FIX: Sync all UI bindings based on intro state
                m_IntroPhaseBinding.Update((int)m_State.IntroPhase);
                if (m_State.IsIntroPlaying)
                {
                    m_IntroHudVisibleBinding.Update(m_State.IntroPhase >= IntroPhase.Reveal);
                }
                else
                {
                    m_IntroHudVisibleBinding.Update(true);
                }

                m_JustLoaded = true;

                Log.Info("Deserialized");
            }
            catch (System.Exception ex)
            {
                Log.Error($"Deserialize failed: {ex}");
                ResetToBootDefaults(ResetReason.DeserializeFailed);
                m_NeedBootDefaultIntroSelfHeal = true;
            }
            finally
            {
                SerializationGuard.EndBlock(reader, block);
            }
        }
    }
}
