using Game.Simulation;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using CivicSurvival.Core.Components.Domain.Cognitive;
using CivicSurvival.Core.Components.Domain.Spotters;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Components.Threats;
using CivicSurvival.Core.Events;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Systems.Scheduling;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;
using CivicSurvival.Domains.Cognitive.Core.Systems;
using CivicSurvival.Domains.Cognitive.Threats.Jobs;

namespace CivicSurvival.Domains.Cognitive.Threats.Systems
{
    /// <summary>
    /// Enemy IPSO (information-psychological operations) campaign controller.
    /// Calculates when and how strongly propaganda affects each district.
    ///
    /// Activates in Crisis act. Intensity grows with wave number.
    /// Per-district exposure modified by internet availability and attack proximity.
    ///
    /// Formula:
    ///   BaseIntensity = clamp(0.10 + WaveNumber × 0.05, 0, 0.8)
    ///   DistrictExposure = BaseIntensity × internetFactor × proximityFactor
    ///     internetFactor: 1.0 (online) or 0.2 (offline — leaflets only)
    ///     proximityFactor: 1.0 (attack phase), 0.8 (recovery), 0.5 (calm)
    ///
    /// Writer: IPSOState singleton + IPSODistrictExposureBuffer
    /// Reads: ScenarioSingleton, WaveStateSingleton, CognitiveState (district list),
    ///        SpotterCountermeasuresState (internet disabled)
    /// Events: WaveEndedEvent (post-wave spike)
    /// </summary>
    public partial class IPSOCampaignSystem : ThrottledSystemBase, IActGatedSystem
    {
        private static readonly LogContext Log = new("IPSOCampaignSystem");

        protected override int UpdateInterval => Engine.Timing.UPDATE_INTERVAL_1_SECOND;

        public Act MinActiveAct => Act.Crisis;
        public ActGateState GateState => m_Gate?.State ?? ActGateState.AwaitingActState;

        /// <summary>Maximum base intensity (reached at wave 16).</summary>
        private const float MAX_BASE_INTENSITY = 0.80f;

        /// <summary>Base intensity at wave 1 (ensures IPSO is visible from first wave).</summary>
        private const float INTENSITY_BASE = 0.10f;

        /// <summary>Intensity growth per wave number.</summary>
        private const float INTENSITY_PER_WAVE = 0.05f;

        /// <summary>Multiplier during active attack phase.</summary>
        private const float ATTACK_PHASE_MULTIPLIER = 1.5f;

        /// <summary>Internet-off residual factor (leaflets/physical propaganda).</summary>
        private const float OFFLINE_FACTOR = 0.2f;

        /// <summary>Proximity factor during attack (Alert/Attack phase).</summary>
        private const float PROXIMITY_ATTACK = 1.0f;

        /// <summary>Proximity factor during recovery.</summary>
        private const float PROXIMITY_RECOVERY = 0.8f;

        /// <summary>Proximity factor during calm.</summary>
        private const float PROXIMITY_CALM = 0.5f;

        /// <summary>Duration of post-wave intensity spike (seconds).</summary>
        private const float POST_WAVE_SPIKE_DURATION = 5.0f;

        /// <summary>Exposure threshold to count district as "affected" (for UI).</summary>
        private const float AFFECTED_THRESHOLD = 0.1f;

        private EntityQuery m_IPSOStateQuery;
        private EntityQuery m_CurrentActQuery;
#pragma warning disable CIVIC324 // Ephemeral act-gate controller; recreated by OnCreate, reset paths, and Deserialize.
        [System.NonSerialized] private ActGateController m_Gate = null!;
#pragma warning restore CIVIC324

        // IJob write lookups (eliminates GetComponentRW sync point)
#pragma warning disable CIVIC269 // Write via IJob, not direct indexer
        private ComponentLookup<IPSOState> m_IPSOWriteLookup;
#pragma warning restore CIVIC269
        private BufferLookup<IPSODistrictExposureBuffer> m_ExposureWriteLookup;

        // Read-only lookups (RO sync — only waits for write jobs, not all jobs)
        private ComponentLookup<IPSOState> m_IPSOReadLookup;
        private ComponentLookup<WaveStateSingleton> m_WaveReadLookup;
        private BufferLookup<InternetDisabledBuffer> m_InternetReadLookup;
        private BufferLookup<CognitiveIntegrityBuffer> m_DistrictReadLookup;

        // G10-6: act-gate intent. HandleGateTransition queues the desired IPSOState
        // write inside ShouldSkipUpdate's reconcile; OnThrottledUpdate applies it
        // via a job on Dependency inside the owning update. [NonSerialized]: pending
        // intent is a runtime edge only and must not persist across save/load.
        [System.NonSerialized] private bool m_PendingActApply;
        [System.NonSerialized] private bool m_PendingActIsActive;
        [System.NonSerialized] private bool m_PendingActClearExposure;
        [System.NonSerialized] private bool m_PendingInitialActReconcile;
        [System.NonSerialized] private bool m_PendingInitialActIsActive;
        [System.NonSerialized] private bool m_PendingWaveSpike;

        protected override void OnCreate()
        {
            base.OnCreate();

            IPSOState.EnsureExists(EntityManager);

            m_IPSOStateQuery = GetEntityQuery(ComponentType.ReadOnly<IPSOState>());
            m_CurrentActQuery = GetEntityQuery(ComponentType.ReadOnly<CurrentActSingleton>());
            InitializeGate();

            // Write lookups (for IJob)
            m_IPSOWriteLookup = GetComponentLookup<IPSOState>(false);
            m_ExposureWriteLookup = GetBufferLookup<IPSODistrictExposureBuffer>(false);

            // Read-only lookups (RO sync only — eliminates RW sync from GetBuffer/TryGetSingleton)
            m_IPSOReadLookup = GetComponentLookup<IPSOState>(true);
            m_WaveReadLookup = GetComponentLookup<WaveStateSingleton>(true);
            m_InternetReadLookup = GetBufferLookup<InternetDisabledBuffer>(true);
            m_DistrictReadLookup = GetBufferLookup<CognitiveIntegrityBuffer>(true);

            SubscribeRequired<WaveEndedEvent>(OnWaveEnded);

            Log.Info("Created (IJob write + read-only lookups, sync-point-free)");
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            IPSOState.EnsureExists(EntityManager);
        }

        protected override bool ShouldSkipUpdate()
        {
            m_Gate.ReconcileFromSingleton(m_CurrentActQuery);

            // Pending batch keep-alive. If reconcile just queued a deactivation,
            // this frame must reach OnThrottledUpdate so the read model is cleared
            // before the inactive gate lets the system sleep.
            if (m_PendingActApply)
                return false;

            if (m_PendingInitialActReconcile)
                return false;

            if (m_PendingWaveSpike)
                return false;

            return m_Gate.State != ActGateState.Active;
        }

        protected override void OnThrottledUpdate()
        {
            if (m_PendingInitialActReconcile)
            {
                m_PendingInitialActReconcile = false;
                bool currentSingletonActive = IsIpsoSingletonActive();
                if (m_PendingInitialActIsActive != currentSingletonActive)
                {
                    QueueActApply(
                        isActive: m_PendingInitialActIsActive,
                        clearExposure: !m_PendingInitialActIsActive,
                        m_PendingInitialActIsActive
                            ? "[IPSO] Initial active gate: activation batch queued"
                            : "[IPSO] Initial inactive gate: deactivation batch queued");
                }
            }

            // G10-6: apply a pending act-gate write inside the owning update
            // (legal ComponentLookup.Update + Dependency).
            if (m_PendingActApply)
            {
                m_PendingActApply = false;
                if (m_IPSOStateQuery.TryGetSingletonEntity<IPSOState>(out var pendingEntity))
                    ScheduleIPSOEvent(pendingEntity, setActive: true, isActive: m_PendingActIsActive,
                        setPostWaveSpikeTimer: false, postWaveSpikeTimer: 0f,
                        clearExposure: m_PendingActClearExposure);
                if (!m_PendingActIsActive)
                {
                    m_PendingWaveSpike = false;
                    return; // deactivated: skip per-tick compute (would re-populate the cleared buffer)
                }
            }

            bool restartSpike = false;
            if (m_PendingWaveSpike)
            {
                m_PendingWaveSpike = false;
                if (m_Gate.State != ActGateState.Active)
                    return;

                restartSpike = true;
            }

            // ════════════════════════════════════════════════════════════════
            // READ PHASE (read-only lookups — RO sync only, no RW stalls)
            // ════════════════════════════════════════════════════════════════

            // Update read-only lookups (just pointer refresh, zero sync cost)
            m_IPSOReadLookup.Update(this);
            m_WaveReadLookup.Update(this);
            m_InternetReadLookup.Update(this);
            m_DistrictReadLookup.Update(this);

            Entity ipsoEntity;
            WaveStateSingleton wave;
            using (PerformanceProfiler.Measure("IPSO.Singletons"))
            {
                // TryGetSingletonEntity = structural only, no data sync
                if (!SystemAPI.TryGetSingletonEntity<WaveStateSingleton>(out var waveEntity))
                    return;
                wave = m_WaveReadLookup[waveEntity];

                if (!SystemAPI.TryGetSingletonEntity<IPSOState>(out ipsoEntity))
                    return;
            }

            // ════════════════════════════════════════════════════════════════
            // COMPUTE PHASE (all local variables, no ECS writes)
            // ════════════════════════════════════════════════════════════════

            // 1. BASE INTENSITY (from wave progression)
            float baseIntensity = math.clamp(INTENSITY_BASE + wave.WaveNumber * INTENSITY_PER_WAVE, 0f, MAX_BASE_INTENSITY);

            // Post-wave spike counts down in game-time (ThrottledDeltaSeconds), not wall-clock.
            // Infection in ResolveHouseholdPsyJob integrates exposure over DeltaHours, so a
            // wall-clock window injected ~game-speed× more cognitive damage at higher speeds;
            // a game-time countdown keeps the spike's accumulated effect identical at any
            // speed and freezes it on pause (the system does not tick while paused).
            // PostWaveSpikeTimer on the serialized IPSOState singleton is the sole source of
            // truth — it survives save/load and is zeroed on deactivation by ApplyIpsoEventJob.
            float prevSpikeTimer = m_IPSOReadLookup.HasComponent(ipsoEntity)
                ? m_IPSOReadLookup[ipsoEntity].PostWaveSpikeTimer
                : 0f;
            float spikeTimer = restartSpike
                ? POST_WAVE_SPIKE_DURATION
                : math.max(0f, prevSpikeTimer - ThrottledDeltaSeconds);
            if (spikeTimer > 0f)
                baseIntensity *= ATTACK_PHASE_MULTIPLIER;

            // 2. PROXIMITY FACTOR
            float proximityFactor = wave.CurrentPhase switch
            {
                GamePhase.Calm => PROXIMITY_CALM,
                GamePhase.Alert => PROXIMITY_ATTACK,
                GamePhase.Attack => PROXIMITY_ATTACK,
                GamePhase.Recovery => PROXIMITY_RECOVERY,
                _ => PROXIMITY_CALM
            };

            // 3. INTERNET-DISABLED LOOKUP (BufferLookup RO — no RW stall)
            // FIX W2-M3: Single SystemAPI call for CognitiveState (was two — double sync point)
            if (!SystemAPI.TryGetSingletonEntity<CognitiveState>(out var cwEntity))
                return;
            // FIX W2-M3: Single SystemAPI call — entity guaranteed to have the component
#pragma warning disable CIVIC051 // Reuses cwEntity from TryGetSingletonEntity above
            var cwState = EntityManager.GetComponentData<CognitiveState>(cwEntity);
#pragma warning restore CIVIC051
            bool isGlobalBlackout = cwState.InternetMode == GlobalInternetMode.Blackout;

            bool hasInternetData = false;
            DynamicBuffer<InternetDisabledBuffer> internetBuffer = default;

            if (!isGlobalBlackout
                && SystemAPI.TryGetSingletonEntity<SpotterCountermeasuresState>(out var cmEntity)
                && m_InternetReadLookup.HasBuffer(cmEntity))
            {
                internetBuffer = m_InternetReadLookup[cmEntity];
                hasInternetData = internetBuffer.Length > 0;
            }

            // 4. PER-DISTRICT EXPOSURE CALCULATION (BufferLookup RO)
            DynamicBuffer<CognitiveIntegrityBuffer> districtBuffer;
            using (PerformanceProfiler.Measure("IPSO.Buffers"))
            {
                if (!m_DistrictReadLookup.HasBuffer(cwEntity)) return;
                districtBuffer = m_DistrictReadLookup[cwEntity];
            }

            float totalExposure = 0f;
            int affectedCount = 0;
            int totalCount = districtBuffer.Length;

            // Build exposure data into NativeArray for IJob write
            var exposureData = new NativeArray<IPSODistrictExposureBuffer>(totalCount, Allocator.TempJob);
            if (Log.IsDebugEnabled)
                PerformanceProfiler.RecordAllocation("IPSO.ExposureArray", totalCount * 8);

            // FIX W2-M8 + S5-07 + M12: During blackout, skip computation entirely — zero-fill buffer
            if (isGlobalBlackout)
            {
                for (int i = 0; i < districtBuffer.Length; i++)
                {
                    exposureData[i] = new IPSODistrictExposureBuffer
                    {
                        DistrictIndex = districtBuffer[i].DistrictIndex,
                        Exposure = 0f
                    };
                }
            }
            else
            {
                using (PerformanceProfiler.Measure("IPSO.DistrictLoop"))
                {
                    for (int i = 0; i < districtBuffer.Length; i++)
                    {
                        int districtIndex = districtBuffer[i].DistrictIndex;

                        float internetFactor = 1.0f;
                        if (hasInternetData)
                        {
                            for (int j = 0; j < internetBuffer.Length; j++)
                            {
                                if (internetBuffer[j].DistrictIndex == districtIndex)
                                {
                                    internetFactor = OFFLINE_FACTOR;
                                    break;
                                }
                            }
                        }

                        float exposure = math.clamp(baseIntensity * internetFactor * proximityFactor, 0f, 1f);

                        exposureData[i] = new IPSODistrictExposureBuffer
                        {
                            DistrictIndex = districtIndex,
                            Exposure = exposure
                        };

                        totalExposure += exposure;
                        if (exposure > AFFECTED_THRESHOLD)
                            affectedCount++;
                    }
                }
            }

            float avgExposure = totalCount > 0 ? totalExposure / totalCount : 0f;
            float globalExposure = isGlobalBlackout ? 0f : avgExposure;

            // ════════════════════════════════════════════════════════════════
            // WRITE PHASE (IJob on worker — no sync point)
            // ════════════════════════════════════════════════════════════════

            ScheduleIPSOWrite(ipsoEntity, baseIntensity, spikeTimer,
                globalExposure, affectedCount, totalCount, exposureData);
        }

        /// <summary>
        /// Schedule IJob to write IPSOState + exposure buffer on worker thread.
        /// Separate method: mirrors BackupPowerRuntimeSystem.ScheduleSingletonWrite() pattern.
        /// </summary>
        private void ScheduleIPSOWrite(Entity ipsoEntity, float baseIntensity,
            float spikeTimer, float globalExposure, int affectedCount,
            int totalCount, NativeArray<IPSODistrictExposureBuffer> exposureData)
        {
            using (PerformanceProfiler.Measure("SP:IPSO.WriteLookupSync"))
            {
                m_IPSOWriteLookup.Update(this);
                m_ExposureWriteLookup.Update(this);
            }

            var writeJob = new WriteIPSOStateJob
            {
                StateLookup = m_IPSOWriteLookup,
                ExposureLookup = m_ExposureWriteLookup,
                SingletonEntity = ipsoEntity,
                BaseIntensity = baseIntensity,
                PostWaveSpikeTimer = spikeTimer,
                GlobalExposure = globalExposure,
                AffectedDistrictCount = affectedCount,
                TotalDistrictCount = totalCount,
                ExposureData = exposureData
            };

            if (CivicSurvival.Core.Diagnostics.BurstLogBootstrap.MarkersEnabled)
                CivicSurvival.Core.Diagnostics.BurstLogBootstrap.Info($"[BURSTMARK] pre WriteIPSOStateJob.Schedule singleton={ipsoEntity} affected={affectedCount} total={totalCount} exposureData={exposureData.IsCreated}/{exposureData.Length}");
            var writeHandle = writeJob.Schedule(Dependency);
            if (CivicSurvival.Core.Diagnostics.BurstLogBootstrap.MarkersEnabled)
                CivicSurvival.Core.Diagnostics.BurstLogBootstrap.Info($"[BURSTMARK] post WriteIPSOStateJob.Schedule singleton={ipsoEntity} affected={affectedCount} total={totalCount} exposureData={exposureData.IsCreated}/{exposureData.Length}");
            if (exposureData.IsCreated)
                Dependency = exposureData.Dispose(writeHandle);
            else
                Dependency = writeHandle;
        }

        // ════════════════════════════════════════════════════════════════
        // EVENT HANDLERS
        // ════════════════════════════════════════════════════════════════

        private void InitializeGate()
        {
            m_Gate = new ActGateController(
                isOpenFor: act => act >= Act.Crisis,
                onTransition: HandleGateTransition);
        }

        private void HandleGateTransition(ActGateState old, ActGateState next, bool isInitial)
        {
            if (next == ActGateState.Active)
            {
                if (isInitial)
                {
                    QueueInitialActReconcile(isActive: true);
                    return;
                }

                QueueActApply(
                    isActive: true,
                    clearExposure: false,
                    "[IPSO] Gate opened: activation batch queued");
                return;
            }

            if (next == ActGateState.Inactive)
            {
                if (isInitial)
                {
                    QueueInitialActReconcile(isActive: false);
                    return;
                }

                QueueActApply(
                    isActive: false,
                    clearExposure: true,
                    "[IPSO] Gate closed: deactivation batch queued");
            }
        }

        private void QueueInitialActReconcile(bool isActive)
        {
            m_PendingInitialActReconcile = true;
            m_PendingInitialActIsActive = isActive;
            ForceNextUpdate();
        }

        private void QueueActApply(bool isActive, bool clearExposure, string logMessage)
        {
            m_PendingActApply = true;
            m_PendingActIsActive = isActive;
            m_PendingActClearExposure = clearExposure;
            ForceNextUpdate();
            Log.Info(logMessage);
        }

        private bool IsIpsoSingletonActive()
        {
            m_IPSOReadLookup.Update(this);
            return m_IPSOStateQuery.TryGetSingletonEntity<IPSOState>(out var ipsoEntity)
                && m_IPSOReadLookup.HasComponent(ipsoEntity)
                && m_IPSOReadLookup[ipsoEntity].IsActive;
        }

        private void OnWaveEnded(WaveEndedEvent evt)
        {
            // G10-6: this is an EventBus callback running inside WaveExecutor's update,
            // NOT IPSO's own update window — it must not touch ComponentLookup.Update /
            // schedule a job / write Dependency (Axiom 8 torn read). It only records
            // intent in the managed flag m_PendingWaveSpike; the owning update
            // (OnThrottledUpdate) restarts PostWaveSpikeTimer to POST_WAVE_SPIKE_DURATION
            // on the IPSOState singleton, and the per-tick game-time countdown drives the
            // ×1.5 multiplier via the normal ScheduleIPSOWrite job on Dependency.
            // No entity query here either — the owning update re-resolves the IPSOState entity.
            //
            m_PendingWaveSpike = true;

            // T4-5: force next-frame throttle fire so exposure reflects the spike within
            // ~1 frame (~16 ms) instead of waiting up to 1 s for the scheduled tick.
            ForceNextUpdate();

            Log.Debug($"IPSO post-wave spike triggered (wave {evt.WaveNumber}, " +
                      $"hits: {evt.Hits}, duration: {POST_WAVE_SPIKE_DURATION}s)");
        }

        private void ScheduleIPSOEvent(Entity ipsoEntity, bool setActive, bool isActive,
            bool setPostWaveSpikeTimer, float postWaveSpikeTimer, bool clearExposure)
        {
            m_IPSOWriteLookup.Update(this);
            m_ExposureWriteLookup.Update(this);

            var eventJob = new ApplyIpsoEventJob
            {
                StateLookup = m_IPSOWriteLookup,
                ExposureLookup = m_ExposureWriteLookup,
                SingletonEntity = ipsoEntity,
                SetActive = setActive,
                IsActive = isActive,
                SetPostWaveSpikeTimer = setPostWaveSpikeTimer,
                PostWaveSpikeTimer = postWaveSpikeTimer,
                ClearExposure = clearExposure
            };
            Dependency = eventJob.Schedule(Dependency);
        }

        protected override void OnDestroy()
        {
            UnsubscribeSafe<WaveEndedEvent>(OnWaveEnded);
            Log.Info("Destroyed");
            base.OnDestroy();
        }
    }
}
