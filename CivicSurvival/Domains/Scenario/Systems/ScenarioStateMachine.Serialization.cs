using Colossal.Serialization.Entities;
using Unity.Entities;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Events;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Serialization;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Interfaces.Services;

namespace CivicSurvival.Domains.Scenario.Systems
{
    public partial class ScenarioStateMachine : IDefaultSerializable, IResettable, IPostLoadValidation, IBootDefaultsReset
    {
        public void ResetToBootDefaults(ResetReason reason)
        {
            Log.Info($"[ScenarioStateMachine] Boot-default reset ({reason}) (was: type={m_State.Type}, act={m_State.CurrentAct}, peak={m_State.PeakPopulation.Value} on {m_State.PeakPopulation.Measure})");
            m_State = ScenarioState.CreateDefault();
            m_Initialized = false;
            m_HasSaveData = false;
            m_MilestoneWeekShown = false;
            m_MilestoneMonthShown = false;
            m_MilestoneQuarterShown = false;
            m_PostCrisisActStartDay = 0;
            m_RoutineTransitionDeferred = false;
            m_ExodusRecoverySinceDay = 0;
            m_HasExodusRecoverySince = false;
            m_ScenarioTypeDeferralLogged = false;
            ClearPendingRuntimeTransitions();
            m_Singleton.Invalidate();
        }

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SaveVersions.GLOBAL);
            try
            {
                var snapshot = new ScenarioStateMachinePersistState(
                    Mod.VERSION,
                    m_State,
                    m_PostCrisisActStartDay,
                    m_MilestoneWeekShown,
                    m_MilestoneMonthShown,
                    m_MilestoneQuarterShown,
                    m_ExodusRecoverySinceDay,
                    m_HasExodusRecoverySince);
                ScenarioStateMachineCodec.Write(snapshot, writer);
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            // C-5: load IS an epoch boundary. Advance so every preserved pre-load
            // transient (TMS arrivals/snapshots, TAS pending, debris buffers) is stale
            // by construction regardless of its stamped value.
            EnsureEpochClock();
            m_actEpochClock?.AdvanceForLoad();
            EnsureThreatGenerationClock();
            m_threatGenerationClock?.AdvanceForLoadBoundary();
            ClearPendingRuntimeTransitions();

            if (!SerializationGuard.TryBeginBlock(reader, SaveVersions.GLOBAL, out var version, out var block, "ScenarioStateMachine"))
            {
                ResetToBootDefaults(ResetReason.VersionMismatch);
                return;
            }
            try
            {
                ScenarioStateMachineCodec.Read(reader, out var snapshot);
                m_State = snapshot.State;
                m_PostCrisisActStartDay = snapshot.PostCrisisActStartDay;
                m_MilestoneWeekShown = snapshot.MilestoneWeekShown;
                m_MilestoneMonthShown = snapshot.MilestoneMonthShown;
                m_MilestoneQuarterShown = snapshot.MilestoneQuarterShown;
                m_ExodusRecoverySinceDay = snapshot.ExodusRecoverySinceDay;
                m_HasExodusRecoverySince = snapshot.HasExodusRecoverySince;

                m_HasSaveData = true;

                // Write directly to singleton (entity created in OnCreate)
                WriteSingletonFromState();

                Log.Info($"Deserialized v{version} (mod {snapshot.ModVersion}): Act={m_State.CurrentAct}, Day={m_State.WarDay}, Waves={m_State.WavesDefended}, Defeated={m_State.IsDefeated}");
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

        public void SetDefaults(Context context) => ResetState();

        public void ResetState()
        {
            // Logged like every neighbouring system: a new city in a running process must not
            // inherit the previous city's peak population — that peak is the denominator of the
            // ghost-town readout and of the victory retention check, so a silent carry-over is
            // indistinguishable from a real collapse.
            Log.Info($"[ScenarioStateMachine] State reset (was: type={m_State.Type}, act={m_State.CurrentAct}, peak={m_State.PeakPopulation.Value} on {m_State.PeakPopulation.Measure})");
            m_State = ScenarioState.CreateDefault();
            m_Initialized = false;
            m_HasSaveData = false;
            m_MilestoneWeekShown = false;
            m_MilestoneMonthShown = false;
            m_MilestoneQuarterShown = false;
            m_PostCrisisActStartDay = 0;
            m_RoutineTransitionDeferred = false;
            m_ExodusRecoverySinceDay = 0;
            m_HasExodusRecoverySince = false;
            m_ScenarioTypeDeferralLogged = false;
            ClearPendingRuntimeTransitions();
            m_Singleton.Invalidate();
            // FIX S7-04: Ensure singleton reflects defaults immediately.
            WriteSingletonFromState();
            // S26-#12 FIX: Reset modal coordinator to prevent stale lock after load
            CivicSurvival.Core.Services.ModalCoordinator.Instance?.Reset();
            // C-5: new-game / failed-deserialize is also an epoch boundary.
            EnsureEpochClock();
            m_actEpochClock?.AdvanceForLoad();
            EnsureThreatGenerationClock();
            m_threatGenerationClock?.AdvanceForLoadBoundary();
        }

        private void ClearPendingRuntimeTransitions()
        {
            m_PendingStartWar = false;
            m_HasPendingActTransition = false;
            m_PendingActTransition = default;
        }

        /// <summary>
        /// FIX G-S5-02: Explicit post-load validation.
        /// Ensures singleton matches m_State and re-publishes ScenarioTypeDetectedEvent
        /// so dependent systems (OminousSigns, CrisisActCoordinator) have fresh state.
        /// </summary>
        public void ValidateAfterLoad()
        {
            SeedCityEverSettledFromSave();

            if (m_HasSaveData
                && (m_State.CurrentAct == Act.Adaptation || m_State.CurrentAct == Act.Exodus)
                && m_PostCrisisActStartDay == 0)
            {
                m_PostCrisisActStartDay = m_State.WarDay;
                Log.Warn($"PostCrisisActStartDay healed after load: act={m_State.CurrentAct}, day={m_State.WarDay}");
            }

            WriteSingletonFromState();

            // Detection decides for itself what this state needs: a save that already carries
            // a classified type and a captured OriginalPopulation only re-announces it, while
            // a new game — or a save whose population was never captured — is classified here
            // if the resident model has published a scalar, and deferred to the throttled
            // retry if it has not.
            //
            // The population is NOT guaranteed to be readable at this point. This comment used
            // to claim the opposite ("already deserialized, decompile-verified") and classify
            // unconditionally on that basis; production refutes it — 4573 scenario detections
            // with population 0 across 2918 sessions, which is also where the persisted
            // OriginalPopulation = 0 in 2918 sessions comes from. Readiness is asked for now,
            // not assumed.
            m_Initialized = DetectScenarioType();

            Log.Info($"ValidateAfterLoad: Act={m_State.CurrentAct}, Day={m_State.WarDay}, Type={m_State.Type}, hasSave={m_HasSaveData}");
        }

        /// <summary>
        /// Give a save written before the settled flag existed its value, from the measurements
        /// it already carries. A living city would latch the flag on its first tick anyway; a
        /// city that died out would not — nothing is left to raise it — and it is exactly there
        /// that a flag stuck at false takes protection away from the decisions that need it,
        /// by making the corpse look like a city created a minute ago.
        ///
        /// Any recorded size, peak or crisis baseline is proof that people once lived here: all
        /// three refuse to record a zero, and the measure they were taken on does not matter for
        /// this question — "was anyone ever here" is not a quantity being divided.
        /// </summary>
        private void SeedCityEverSettledFromSave()
        {
            if (m_State.CityEverSettled)
                return;

            if (!m_State.OriginalPopulation.HasValue
                && !m_State.PeakPopulation.HasValue
                && !m_State.CrisisStartPopulation.HasValue)
            {
                return;
            }

            m_State.CityEverSettled = true;
            Log.Info($"[ScenarioStateMachine] CityEverSettled seeded from the save's own measurements (original={m_State.OriginalPopulation.Value}, peak={m_State.PeakPopulation.Value}, crisisBaseline={m_State.CrisisStartPopulation.Value})");
        }

        private void WriteSingletonFromState()
        {
            var singletonEntity = EnsureSingletonEntity(EntityManager);
            m_SingletonLookup.Update(this);
            m_CurrentActLookup.Update(this);

            var current = EntityManager.GetComponentData<ScenarioSingleton>(singletonEntity);
            current.ScenarioType = m_State.Type;
            if (TryResolveCurrentGameDay(out var gameDay))
                current.GameDay = gameDay.Value;
            current.WarDay = ResolveCurrentWarDay();
            current.PopulationPeak = m_State.PeakPopulation;
            current.CrisisStartPopulation = m_State.CrisisStartPopulation;
            current.CityEverSettled = m_State.CityEverSettled;
            current.IsWarStarted = m_State.CurrentAct != Act.PreWar;
            current.IsDefeated = m_State.IsDefeated;
            current.ExodusRateOverrideFraction = m_State.ExodusRateOverrideFraction;
            current.ShownModals = m_State.ShownModals;
            current.DonorAidReceived = m_State.DonorAidReceived;
            EntityManager.SetComponentData(singletonEntity, current);

            EntityManager.SetComponentData(singletonEntity, new CurrentActSingleton { CurrentAct = m_State.CurrentAct });
        }
    }
}
