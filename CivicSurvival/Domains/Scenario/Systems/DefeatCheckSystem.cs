using System;
using Game.Simulation;
using Unity.Entities;
using Colossal.Serialization.Entities;
using CivicSurvival.Core.Components.Domain.Cognitive;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Events;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Interfaces.Domain.Population;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Logic;
using CivicSurvival.Core.Serialization;
using CivicSurvival.Core.Systems;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;
using CivicSurvival.Core.Attributes;

namespace CivicSurvival.Domains.Scenario.Systems
{
    /// <summary>
    /// Checks defeat conditions every ~1 second.
    /// Defeat triggers when:
    ///   1. A city that once carried a real population collapses below the scaled
    ///      population floor and stays there across a game-day boundary
    ///   2. Average cognitive integrity stays below threshold for N hours
    ///
    /// Skips checks if war hasn't started or PostVictoryMode == Endless.
    /// Publishes GameOverEvent on defeat.
    /// </summary>
    [ActIndependent]
    public partial class DefeatCheckSystem : CivicSystemBase, IDefaultSerializable, IResettable, IPostLoadValidation
    {
        private static readonly LogContext Log = new("DefeatCheckSystem");

        private const float THROTTLE_SECONDS = 1.0f;
        private const int DEFEAT_GRACE_DAYS = 3;

        // Sentinel for "the collapse condition is not currently held" — war day 0 is a real day.
        // Owned by the persist contract so the runtime value and the save default cannot drift.
        private const int NO_PENDING_COLLAPSE = DefeatCheckPersistState.NoPendingCollapseWarDay;

        [NonSerialized] private float m_AccumulatedTime;

        // Population source — the single owner of the measure. Ephemeral: a service handle,
        // meaningless outside the running producer.
        [NonSerialized] private ICityPopulationReader m_PopulationReader = null!;

        // Local state (serialized — only system-internal tracking)
        private float m_IntegrityBelowThresholdHours;

        // War day the population-collapse condition was first observed on. Serialized: a player
        // who reloads more often than once per game day would otherwise never reach the verdict,
        // because every load would restart the confirmation.
        private int m_PendingCollapseWarDay = NO_PENDING_COLLAPSE;

        // FIX S5-07: Track act to reset defeat timer on Crisis exit.
        // Synced from StateMachine in Deserialize (loaded game) and ValidatePostLoad (new game).
        // Reset guard: m_IntegrityBelowThresholdHours > 0f makes missed transitions harmless.
        [NonSerialized] private Act m_LastCheckedAct;

        // FIX S7-03: Track PostVictoryMode to reset defeat timer on OneMoreYear
#pragma warning disable CIVIC221 // Ephemeral — re-derived from StateMachine on load (Deserialize syncs)
        private PostVictoryMode m_LastPostVictoryMode;
#pragma warning restore CIVIC221

        // Dependencies — managed system ref, not serialized
        [System.NonSerialized] private ScenarioStateMachine m_StateMachine = null!;
        private ComponentLookup<CognitiveState> m_CogWarfareLookup;
        private BufferLookup<CognitiveIntegrityBuffer> m_CogIntegrityBufferLookup;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_CogWarfareLookup = GetComponentLookup<CognitiveState>(true);
            m_CogIntegrityBufferLookup = GetBufferLookup<CognitiveIntegrityBuffer>(true);


            Log.Info("Created");
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            m_StateMachine ??= FeatureRegistry.Instance.Require<ScenarioStateMachine>();
            // Registered in ResidentPopulationModelSystem.OnCreate, i.e. before any
            // OnStartRunning — the mandatory Require shape is safe here (CIVIC463).
            m_PopulationReader ??= ServiceRegistry.Instance.Require<ICityPopulationReader>();
        }

        protected override void OnUpdateImpl()
        {
            m_CogWarfareLookup.Update(this);
            m_CogIntegrityBufferLookup.Update(this);

            if (m_StateMachine.IsDefeated) return;

            // Throttle to ~1Hz (game-time aware — respects speed multiplier)
            m_AccumulatedTime += SystemAPI.Time.DeltaTime;
            if (m_AccumulatedTime < THROTTLE_SECONDS) return;
            float elapsedSeconds = Math.Min(m_AccumulatedTime, THROTTLE_SECONDS * 2f);
            m_AccumulatedTime = 0f;

            // Skip if war hasn't started
            if (m_StateMachine.WarDay < 1) return;

            // Skip if Endless mode
            if (m_StateMachine.PostVictoryMode == PostVictoryMode.Endless) return;

            // FIX S7-03: Reset defeat timer when player chooses OneMoreYear
            if (m_StateMachine.PostVictoryMode != m_LastPostVictoryMode)
            {
                m_LastPostVictoryMode = m_StateMachine.PostVictoryMode;

                if (m_StateMachine.PostVictoryMode == PostVictoryMode.OneMoreYear && m_IntegrityBelowThresholdHours > 0f)
                {
                    Log.Info($"OneMoreYear selected: defeat timer reset ({m_IntegrityBelowThresholdHours:F1}h → 0)");
                    m_IntegrityBelowThresholdHours = 0f;
                }
            }

            // FIX S5-07: Reset integrity defeat timer on act transition from Crisis.
            // Player survived the crisis → fresh start. Without this, timer accumulated
            // during Crisis bleeds into Adaptation, causing defeat after "successful" survival.
            // W4-LATENT2 FIX: Read managed CurrentAct (always fresh) instead of ECS singleton (up to 1s stale).
            var currentAct = m_StateMachine.CurrentAct;
            if (currentAct != m_LastCheckedAct)
            {
                Act previousAct = m_LastCheckedAct;
                m_LastCheckedAct = currentAct;

                if (previousAct == Act.Crisis && m_IntegrityBelowThresholdHours > 0f)
                {
                    Log.Info($"Act transition Crisis → {currentAct}: defeat timer reset ({m_IntegrityBelowThresholdHours:F1}h → 0)");
                    m_IntegrityBelowThresholdHours = 0f;
                }
            }

            var config = BalanceConfig.Current.Scenario;

            // Check 1: Population collapse (floor scaled to the starting city, grace period at
            // war start, and a day-boundary confirmation on top — see the method)
            if (EvaluatePopulationCollapse(config.DefeatPopulationThreshold))
                return;

            // Check 2: Cognitive integrity below threshold for extended period
            float avgIntegrity = GetAverageIntegrity();
            if (avgIntegrity >= 0f) // -1 means CognitiveWarfare not active
            {
                if (avgIntegrity < config.DefeatIntegrityThreshold)
                {
                    // Accumulate hours below threshold (game-time aware)
                    float deltaHours = GameRate.HoursDelta(elapsedSeconds);
                    m_IntegrityBelowThresholdHours += deltaHours;

                    if (m_IntegrityBelowThresholdHours >= config.DefeatIntegrityHours)
                    {
                        TriggerDefeat(DefeatCause.LostControl);
                    }
                }
                else
                {
                    // Reset timer when integrity recovers
                    m_IntegrityBelowThresholdHours = 0f;
                }
            }
        }

        /// <summary>
        /// Population-collapse leg of the defeat check. Returns true when defeat was declared.
        ///
        /// A collapse verdict needs four things, in this order: something to measure with
        /// (a ready resident count), something to measure against (a captured baseline), a
        /// city that once carried a population (otherwise there was nothing to collapse), and
        /// the condition surviving a game-day boundary (a single sample of the live counter
        /// routinely dips through zero while citizens stream in).
        /// </summary>
        private bool EvaluatePopulationCollapse(int configuredThreshold)
        {
            // The owned measure, not the raw citizen count: the raw one includes tourists,
            // commuters and dead-but-not-yet-deleted citizens. A refusal means there is no city
            // entity to ask — no measurement, no verdict, and the pending confirmation is left
            // exactly as it was rather than being cleared by an absent city. A city that is
            // genuinely empty answers with zero and is judged on that zero, which is what the
            // peak guard below exists to qualify.
            if (!m_PopulationReader.TryGetResidentCount(out int population))
                return false;

            if (!TryGetPopulationDefeatThreshold(configuredThreshold, out int populationThreshold))
            {
                m_PendingCollapseWarDay = NO_PENDING_COLLAPSE;
                return false;
            }

            // The city must have carried a real population at some point. Without this, a city
            // that has not grown to the floor yet reads identically to one that fell to it.
            //
            // This stays a comparison of magnitudes and is NOT replaced by the settled-city
            // flag: the flag is strictly weaker (a city whose peak was thirty, under a floor of
            // a hundred, gets no verdict today and would get one from the flag), so swapping it
            // in would hand out defeats this guard currently prevents. The flag exists for
            // decisions that have no protection of their own.
            //
            // The peak is a recorded measurement compared against a live one, so it has to be on
            // the same counter: a peak inherited from the superseded one yields no verdict at
            // all, exactly like an uncaptured one, and the pending confirmation is cleared.
            if (!m_StateMachine.PeakPopulation.TryGetOn(PopulationMeasure.VanillaCityRecord, out int peakPopulation)
                || peakPopulation < populationThreshold)
            {
                m_PendingCollapseWarDay = NO_PENDING_COLLAPSE;
                return false;
            }

            int warDay = m_StateMachine.WarDay;

            if (population >= populationThreshold || warDay <= DEFEAT_GRACE_DAYS)
            {
                m_PendingCollapseWarDay = NO_PENDING_COLLAPSE;
                return false;
            }

            // Day-boundary confirmation. Arming also re-arms on a war-day rewind, which is how
            // a new city started on this reused system instance looks from here.
            if (m_PendingCollapseWarDay == NO_PENDING_COLLAPSE || warDay < m_PendingCollapseWarDay)
            {
                m_PendingCollapseWarDay = warDay;
                Log.Info($"Population below defeat floor ({population} < {populationThreshold}, peak {peakPopulation}) on war day {warDay} — awaiting day-boundary confirmation");
                return false;
            }

            if (warDay == m_PendingCollapseWarDay)
                return false;

            Log.Info($"Population collapse confirmed: {population} < {populationThreshold} held from war day {m_PendingCollapseWarDay} to {warDay}");
            TriggerDefeat(DefeatCause.PopulationCollapse);
            return true;
        }

        /// <summary>
        /// Population floor the city counts as collapsed under, scaled to the city the player
        /// started with. Returns false when there is no usable measurement: absent means
        /// "nothing to measure against", not "use the largest possible floor" — the raw
        /// configured value applied to an uncaptured baseline is what declared defeat to cities
        /// that were still growing. A size recorded on the superseded counter is refused for the
        /// same reason it is not converted: the floor would be a tenth of a number the live
        /// reading is not comparable to.
        /// </summary>
        private bool TryGetPopulationDefeatThreshold(int configuredThreshold, out int threshold)
        {
            if (!m_StateMachine.State.OriginalPopulation.TryGetOn(PopulationMeasure.VanillaCityRecord, out int originalPopulation)
                || originalPopulation <= 0)
            {
                threshold = 0;
                return false;
            }

            int scaledThreshold = Math.Max(1, (int)Math.Round(originalPopulation * 0.1f));
            threshold = Math.Min(configuredThreshold, scaledThreshold);
            return true;
        }

        /// <summary>
        /// Get average cognitive integrity across all districts.
        /// Returns -1 if CognitiveWarfare is not active.
        /// </summary>
        private float GetAverageIntegrity()
        {
#pragma warning disable CIVIC070 // Integrity changes gradually; 1-frame lag invisible for defeat checks
            if (!SystemAPI.TryGetSingletonEntity<CognitiveState>(out var stateEntity))
#pragma warning restore CIVIC070
                return -1f;

            if (!m_CogWarfareLookup.TryGetComponent(stateEntity, out var state) || !state.IsActive)
                return -1f;

            if (!m_CogIntegrityBufferLookup.TryGetBuffer(stateEntity, out var buffer))
                return -1f;
            if (buffer.Length == 0)
                return -1f;

            float total = 0f;
            for (int i = 0; i < buffer.Length; i++)
            {
                total += buffer[i].Integrity;
            }

            return total / buffer.Length;
        }

        private void TriggerDefeat(DefeatCause cause)
        {
            m_StateMachine.SetDefeated(cause);

            int daysSurvived = m_StateMachine.WarDay;
            EventBus?.SafePublish(new GameOverEvent(cause, daysSurvived), "DefeatCheckSystem");

            Log.Info($"DEFEAT: {cause}, survived {daysSurvived} days");
        }

        /// <summary>
        /// S10-01: Force immediate defeat check after load.
        /// Sets accumulator to throttle threshold so next OnUpdateImpl fires immediately.
        /// </summary>
        public void ValidateAfterLoad()
        {
            m_StateMachine ??= FeatureRegistry.Instance.Require<ScenarioStateMachine>();
            m_AccumulatedTime = THROTTLE_SECONDS;

            // Sync act tracking regardless of deserialization order
            m_LastCheckedAct = m_StateMachine.CurrentAct;
            m_LastPostVictoryMode = m_StateMachine.PostVictoryMode;

            // The collapse confirmation itself is restored by Deserialize and deliberately kept:
            // the check only concludes on a live reading of the city, and a confirmation armed
            // before the save still has to be re-observed on a later war day to fire.
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
        }
    }
}
