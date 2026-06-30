using System;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Profiling;
using CivicSurvival.Core.Events;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.Diagnostics;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Interfaces.Domain.Power;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Forecast;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;
using CivicSurvival.Core.Components.Domain.Power;
using CivicSurvival.Core.Components.Domain.Mobilization;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Components.Threats;
using Game.Simulation;
using static CivicSurvival.Services.Telemetry.EventTypes;

namespace CivicSurvival.Services.Telemetry
{
    /// <summary>
    /// Periodic metrics sampler for telemetry.
    /// Handles FPS, Balance reports, and ECS-based snapshots.
    /// </summary>
    public sealed class TelemetryPulse : IDisposable
    {
        private static readonly LogContext Log = new("TelemetryPulse");

        // ===== Telemetry Constants =====
        private const double BALANCE_REPORT_INTERVAL_SECONDS = 300.0;
        private const double BALANCE_WINDOW_CAP_SECONDS = BALANCE_REPORT_INTERVAL_SECONDS;
        private const float MIN_DELTA_TIME = 0.0001f;
        private const float MS_PER_SECOND = 1000f;
        private const double SECONDS_PER_MINUTE_D = 60.0;
        private const int BLACKOUT_TARGET_PERCENT = 20;
        private const double BLACKOUT_TARGET_PERCENT_D = 20.0;
        private const double MONEY_READ_INTERVAL_SECONDS = 5.0;
        private readonly World m_World;
        private readonly EntityQuery m_PowerGridQuery;
        private readonly EntityQuery m_AirDefenseQuery;
        private readonly EntityQuery m_CurrentActQuery;
        private readonly EntityQuery m_WaveStateQuery;
        private readonly EntityQuery m_ThreatStatsQuery;
        private readonly EntityQuery m_CognitiveQuery;
        private readonly EntityQuery m_MobilizationQuery;
        private readonly TelemetryRecorder m_Recorder;
        private readonly IEventBus m_EventBus;
        private readonly string m_SessionId;
        // Not readonly: the diagnostics opt-in can toggle within a session while the pulse
        // lives (it is part of the Online-gated event pipeline, not torn down on a
        // diagnostics toggle), so the orchestrator swaps in the fresh config via
        // RefreshConfig. Only the effective-diagnostics gate (m_Config.Enabled) on the
        // analytics-only records (hardware snapshot, balance session report) depends on it.
        private TelemetryConfig m_Config;
        private readonly SimulationSystem m_SimulationSystem;

        // Timers
        private double m_TimeSinceLastPerformanceSample;
        private int m_PerformanceFrameCount;
        private double m_PerformanceFrameTimeSum;
#pragma warning disable CIVIC167 // Time accumulator, not monetary (name contains "Balance" but tracks seconds)
        private double m_TimeSinceLastBalanceReport;
#pragma warning restore CIVIC167

        // Blackout time tracking (for ~20% target validation)
        // FIX #191: double to avoid float precision drift in long sessions
        private double m_TotalSessionTime;
        private double m_BalanceWindowSessionTime;
        private double m_BalanceWindowBlackoutTime;

        /// <summary>Total real-time seconds in this session (for session_end telemetry).</summary>
        public double TotalSessionTime => m_TotalSessionTime;

        // City balance snapshotted while the world is alive, for session_end telemetry: that
        // event is read at teardown when CityBudgetService's facade is already unregistered, so a
        // live read there would fail. Sampled here on a throttle and exposed to the session-end
        // gatherer via LastKnownMoney (alongside TotalSessionTime — same session-end consumer).
        private long m_LastKnownMoney;
        private double m_TimeSinceLastBalanceRead;

        /// <summary>Last city balance sampled during play (for session_end telemetry).</summary>
        public long LastKnownMoney => m_LastKnownMoney;

        // Session start sent flag
        private bool m_SessionStartSent;

        // Reference to get active blackout count from listener
        private readonly Func<int> m_GetActiveBlackoutDistricts;

        // KW→MW conversion for nameplate/production telemetry.
        private const double KilowattsPerMegawatt = 1000.0;

        // Built-capacity reader for the surplus-strike numerator (NameplateMW telemetry).
        // IPowerCapacitySnapshotReader is [OwnedByFeatureId(Engineering)] only — feature-
        // mandatory (AlwaysOpen) — so it is always registered by the time this runs from
        // TelemetryService.OnUpdate; resolved once via Require and cached (CIVIC463).
        private IPowerCapacitySnapshotReader? m_CapacitySnapshotReader;

        // "Patriot intercepts drones" player toggle for the wave-snapshot balance fields. AirDefense-
        // owned, so it may be closed: resolved lazily via the null-object fallback (false = the runtime
        // fail-closed default) instead of Require, since a wave can fire before AirDefense opens.
        private IPatriotDroneInterceptReader? m_PatriotDroneReader;

        public TelemetryPulse(
            World world,
            EntityQuery powerGridQuery,
            EntityQuery airDefenseQuery,
            EntityQuery currentActQuery,
            EntityQuery waveStateQuery,
            EntityQuery threatStatsQuery,
            EntityQuery cognitiveQuery,
            EntityQuery mobilizationQuery,
            TelemetryRecorder recorder,
            IEventBus eventBus,
            string sessionId,
            TelemetryConfig config,
            Func<int> getActiveBlackoutDistricts)
        {
            m_World = world ?? throw new ArgumentNullException(nameof(world));
            m_PowerGridQuery = powerGridQuery;
            m_AirDefenseQuery = airDefenseQuery;
            m_CurrentActQuery = currentActQuery;
            m_WaveStateQuery = waveStateQuery;
            m_ThreatStatsQuery = threatStatsQuery;
            m_CognitiveQuery = cognitiveQuery;
            m_MobilizationQuery = mobilizationQuery;
            m_Recorder = recorder ?? throw new ArgumentNullException(nameof(recorder));
            m_EventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
            m_SessionId = sessionId ?? throw new ArgumentNullException(nameof(sessionId));
            m_Config = config ?? throw new ArgumentNullException(nameof(config));
            m_GetActiveBlackoutDistricts = getActiveBlackoutDistricts ?? throw new ArgumentNullException(nameof(getActiveBlackoutDistricts));
            m_SimulationSystem = world.GetOrCreateSystemManaged<SimulationSystem>();

            // Subscribe to wave starting for balance snapshot
            m_EventBus.Subscribe<WaveStartingEvent>(OnWaveStarting);
            m_EventBus.Subscribe<GameLoadedEvent>(OnGameLoaded);

            Log.Debug(" Initialized");
        }

        /// <summary>
        /// Swap in a freshly loaded config after the diagnostics opt-in or Online state
        /// toggled. Only the effective-diagnostics gate (<see cref="TelemetryConfig.Enabled"/>)
        /// on the analytics-only records reads it; the functional FPS / wave-snapshot records
        /// are emitted unconditionally while the pulse runs. Sampling timers and accumulators
        /// are preserved across the swap.
        /// </summary>
        public void RefreshConfig(TelemetryConfig config)
        {
            m_Config = config ?? throw new ArgumentNullException(nameof(config));
        }

        public void Dispose()
        {
            m_EventBus.Unsubscribe<WaveStartingEvent>(OnWaveStarting);
            m_EventBus.Unsubscribe<GameLoadedEvent>(OnGameLoaded);
            Log.Debug(" Disposed");
        }

        /// <summary>
        /// Called every frame from TelemetryService.OnUpdate().
        /// </summary>
        public void Update(float deltaTime)
        {
            // Send hardware info once per session, but only while diagnostics is effectively
            // on — RecordSessionStart self-gates and returns without recording when
            // diagnostics is off, so the once-per-session latch is only armed on an actual
            // record. This lets the snapshot still fire if the player enables diagnostics
            // later in the same session (Online on, opt-in toggled afterwards).
            if (!m_SessionStartSent && m_Config.Enabled)
            {
                RecordSessionStart();
                m_SessionStartSent = true;
            }

            // Blackout time tracking
            m_TotalSessionTime += deltaTime;
            m_BalanceWindowSessionTime = Math.Min(BALANCE_WINDOW_CAP_SECONDS, m_BalanceWindowSessionTime + deltaTime);
            if (m_GetActiveBlackoutDistricts() > 0)
            {
                m_BalanceWindowBlackoutTime = Math.Min(BALANCE_WINDOW_CAP_SECONDS, m_BalanceWindowBlackoutTime + deltaTime);
            }

            // Performance sampling
            m_TimeSinceLastPerformanceSample += deltaTime;
            m_PerformanceFrameCount++;
            m_PerformanceFrameTimeSum += deltaTime;
            if (m_TimeSinceLastPerformanceSample >= m_Config.PerformanceSampleIntervalSeconds)
            {
                RecordPerformanceMetrics();
                m_PerformanceFrameCount = 0;
                m_PerformanceFrameTimeSum = 0.0;
                m_TimeSinceLastPerformanceSample = PreserveTimerOvershoot(
                    m_TimeSinceLastPerformanceSample,
                    m_Config.PerformanceSampleIntervalSeconds);
            }

            // Periodic balance report (every 5 minutes)
            m_TimeSinceLastBalanceReport += deltaTime;
            if (m_TimeSinceLastBalanceReport >= BALANCE_REPORT_INTERVAL_SECONDS)
            {
                RecordBalanceReport();
                m_TimeSinceLastBalanceReport = PreserveTimerOvershoot(
                    m_TimeSinceLastBalanceReport,
                    BALANCE_REPORT_INTERVAL_SECONDS);
            }

            SampleCityBalance(deltaTime);
        }

        /// <summary>
        /// Snapshot city balance for session_end telemetry (read at teardown when the budget
        /// facade is gone). Uses CityBudgetService.TryGetCachedBalance — reads vanilla
        /// CitySystem.moneyAmount (a managed int refreshed each sim-tick), so it does NOT force a
        /// PlayerMoney CompleteDependency: the sync is already paid by CitySystem, we piggyback on
        /// it. The throttle bounds the (now trivial) work. session_end is a diagnostics signal, so
        /// this self-gates on the effective diagnostics gate (m_Config.Enabled) — while the pulse
        /// runs, that flag carries opt-in AND Online, matching where the orchestrator gated it.
        /// </summary>
        private void SampleCityBalance(float deltaTime)
        {
            if (!m_Config.Enabled) return;

            m_TimeSinceLastBalanceRead += deltaTime;
            if (m_TimeSinceLastBalanceRead >= MONEY_READ_INTERVAL_SECONDS)
            {
                m_TimeSinceLastBalanceRead = 0.0;
                if (CityBudgetService.TryGetCachedBalance(m_World, out var balance))
                    m_LastKnownMoney = balance;
            }
        }

        private static double PreserveTimerOvershoot(double elapsed, double interval)
        {
            return interval > 0.0 ? elapsed % interval : 0.0;
        }

        private void Record(string type, object data)
        {
            m_Recorder.Record(m_SessionId, type, data);
        }

        /// <summary>
        /// Sync-free cognitive-integrity aggregate (mental health) for balance telemetry.
        /// Reads the CognitiveState singleton buffer (≤16 districts) on the main thread; the
        /// buffer's only writer (CognitiveStateSystem) mutates it main-thread, so no job is in
        /// flight to sync on — the same access shape DefeatCheckSystem.GetAverageIntegrity uses
        /// ~1/s. When cognitive warfare is not active (the mechanic is not engaged yet) the
        /// out-values are left null — "not measured" — so the wire emits JSON null instead of a
        /// misleading "fully intact" 1.0 the backend cannot distinguish from a real measurement.
        /// </summary>
        private void GetCognitiveSnapshot(out float? avgIntegrity, out float? minIntegrity, out int? compromisedDistricts)
        {
            avgIntegrity = null;
            minIntegrity = null;
            compromisedDistricts = null;

            if (!m_CognitiveQuery.TryGetSingleton<CognitiveState>(out var state) || !state.IsActive)
                return;

            var entity = m_CognitiveQuery.GetSingletonEntity();
            var em = m_World.EntityManager;
            if (!em.HasBuffer<CognitiveIntegrityBuffer>(entity))
                return;

            var buffer = em.GetBuffer<CognitiveIntegrityBuffer>(entity, isReadOnly: true);
            if (buffer.Length == 0)
                return;

            float total = 0f;
            float min = 1f;
            int compromised = 0;
            for (int i = 0; i < buffer.Length; i++)
            {
                float integrity = buffer[i].Integrity;
                total += integrity;
                min = Math.Min(min, integrity);
                if (buffer[i].IsCompromised)
                    compromised++;
            }

            avgIntegrity = total / buffer.Length;
            minIntegrity = min;
            compromisedDistricts = compromised;
        }

        #region Session Start

        private void RecordSessionStart()
        {
            // Hardware snapshot (CPU cores / GPU / RAM / OS) is developer analytics for perf
            // bucketing — it feeds no player-facing feature. Gate it on the effective
            // diagnostics gate so it is NOT recorded when Online is on but diagnostics is off
            // (§2 functional/analytics split). The chronicle reads performance.sample, not
            // this hardware event.
            if (!m_Config.Enabled) return;

            Record(Performance.SessionStart, new PerformanceSessionStartData
            {
                // Privacy: CPU core count (not the CPU model) and OS family (not the build
                // version) — enough for perf bucketing and platform triage without fingerprinting.
                // GPU model is kept: render perf and GPU-specific crashes need it.
                CpuCores = SystemInfo.processorCount,
                Gpu = SystemInfo.graphicsDeviceName,
                GpuVramMb = SystemInfo.graphicsMemorySize,
                RamMb = SystemInfo.systemMemorySize,
                Os = SystemInfo.operatingSystemFamily.ToString()
            });

            Log.Info($" Session start: {SystemInfo.processorCount} CPU cores, {SystemInfo.graphicsMemorySize}MB VRAM, {SystemInfo.systemMemorySize}MB RAM");
        }

        #endregion

        #region Performance Metrics

        private void RecordPerformanceMetrics()
        {
            var frameCount = Math.Max(1, m_PerformanceFrameCount);
            var totalFrameTime = Math.Max(MIN_DELTA_TIME, (float)m_PerformanceFrameTimeSum);

#pragma warning disable CIVIC100 // False positive: guard is the ternary deltaTime > MIN_DELTA_TIME
            var fps = totalFrameTime > MIN_DELTA_TIME ? frameCount / totalFrameTime : 0f;
#pragma warning restore CIVIC100
            var frameTimeMs = totalFrameTime * MS_PER_SECOND / frameCount;
            var memoryMB = (int)(GC.GetTotalMemory(false) / (1024 * 1024));
            var nativeMemoryMB = (int)(Profiler.GetTotalAllocatedMemoryLong() / (1024 * 1024));

            // Game context.
            // Wire-format convention: when the Scenario/Waves producers are unavailable
            // (boot window before ScenarioStateMachine's first snapshot, or feature closed)
            // we emit the telemetry-only sentinel "unknown" for the categorical act /
            // game-phase fields instead of falling back to the default singleton's pre_war /
            // calm. Falling back to pre_war / calm falsely pollutes those buckets when an
            // advanced save (Crisis/Exodus/Routine) loads before the state machine ticks.
            // "unknown" is a wire + contract string sentinel ONLY — it is deliberately NOT a
            // member of the C# Act enum (that would bloat every exhaustive OnActChanged switch,
            // CIVIC334). The numeric WaveNumber keeps the default (0): 0 is a correct value, not
            // a misleading category, so only the enum-valued fields use the sentinel.
            const string UnknownState = "unknown";
            var population = 0;
            var act = m_CurrentActQuery.TryGetSingleton<CurrentActSingleton>(out var ca)
                ? ca.CurrentAct.ToString().ToSnakeCase()
                : UnknownState;
            var hasWaveState = m_WaveStateQuery.TryGetSingleton<WaveStateSingleton>(out var ws);
            var waveState = hasWaveState ? ws : WaveStateSingleton.Default;
            var gamePhase = hasWaveState
                ? ws.CurrentPhase.ToString().ToSnakeCase()
                : UnknownState;
            var activeThreats = 0;
            var gameSpeed = 1f;

            var threatStats = m_ThreatStatsQuery.TryGetSingleton<ThreatStatsSingleton>(out var ts)
                ? ts : ThreatStatsSingleton.Default;
            activeThreats = threatStats.TotalActiveCount;

            gameSpeed = m_SimulationSystem.selectedSpeed;

            // Population: expensive (lock + CalculateEntityCount), safe at 1/min
            if (m_World.IsCreated)
            {
                try
                {
                    population = PopulationUtils.GetCitizenCount();
                }
                catch (Exception ex)
                {
                    if (Log.IsDebugEnabled) Log.Debug($" Population query failed: {ex.Message}");
                }
            }

            // Wave-balance context: built nameplate (surplus-strike numerator, degradation-
            // independent), effective production, and wave escalation number. All read here
            // so the server can correlate FPS / ActiveThreats against grid over-build.
            var nameplateMW = 0;
            // IPowerCapacitySnapshotReader is [OwnedByFeatureId(Engineering)] (AlwaysOpen,
            // feature-mandatory): consume via Require, not defensive TryGet (CIVIC463).
            // Resolved once and cached; this runs from TelemetryService.OnUpdate, after every
            // system's OnCreate (where Engineering registers the reader), so it's present.
            m_CapacitySnapshotReader ??= ServiceRegistry.Instance.Require<IPowerCapacitySnapshotReader>();
            if (m_CapacitySnapshotReader.TryGetSnapshot(out var capSnap))
            {
                nameplateMW = (int)Math.Round(capSnap.NameplateKW / KilowattsPerMegawatt);
            }

            var productionMW = 0;
            if (m_PowerGridQuery.TryGetSingleton<PowerGridSingleton>(out var powerGrid))
            {
                productionMW = (int)Math.Round(powerGrid.Production / KilowattsPerMegawatt);
            }

            var gameDay = Core.Systems.GameTimeSystem.TryGetDay(out var day) ? day : 0;
            // Crash-heartbeat: refresh the live timestamp on the pulse cadence. The ActiveSim phase itself
            // is owned by GameTimeSystem (set at the first/every GameSimulation tick), NOT here — that is
            // the authoritative "sim is ticking" point, and it correctly stays Loaded while paused. The pulse
            // and GameTimeSystem both live in GameSimulation, so this Update only runs when the sim ticks;
            // after a Saving boundary the next GameTimeSystem tick returns the phase to ActiveSim.
            CrashContextProvider.Update(new CrashContextSnapshot(
                gameDay,
                act,
                activeThreats,
                waveState.WaveNumber,
                population,
                memoryMB,
                nativeMemoryMB,
                DateTime.UtcNow));

            // Functional sample (UNGATED, under Online): only the player-facing context the
            // chronicle and balance pipelines consume — population, act, game phase, grid
            // build-out and wave number. PRIVACY.md classifies these as functional telemetry.
            // Mental-health time-series: sync-free aggregate of district cognitive integrity.
            GetCognitiveSnapshot(out var cogIntegrityAvg, out _, out _);

            Record(Performance.Sample, new PerformanceSampleData
            {
                Population = population,
                Act = act,
                GamePhase = gamePhase,
                NameplateMW = nameplateMW,
                ProductionMW = productionMW,
                WaveNumber = waveState.WaveNumber,
                CognitiveIntegrityAvg = cogIntegrityAvg.HasValue ? (float?)Math.Round(cogIntegrityAvg.Value, 3) : null
            });

            // Diagnostic sample (GATED on the diagnostics opt-in): FPS / frame time / memory are
            // developer diagnostics per PRIVACY.md, so they must NOT leave the machine when Online
            // is on but diagnostics is off. The load-correlation context (game speed, active
            // threats, blackouts, population, act, wave) rides along so the backend can explain a
            // low FPS by city size, hardware and threat load rather than blaming the mod.
            if (m_Config.Enabled)
            {
                Record(Performance.DiagnosticSample, new PerformanceDiagnosticSampleData
                {
                    FpsAvg = (float)Math.Round(fps, 1),
                    FrameTimeMs = (float)Math.Round(frameTimeMs, 2),
                    MemoryUsageMb = memoryMB,
                    GameSpeed = (float)Math.Round(gameSpeed, 1),
                    ActiveThreats = activeThreats,
                    BlackoutDistricts = m_GetActiveBlackoutDistricts(),
                    Population = population,
                    Act = act,
                    WaveNumber = waveState.WaveNumber
                });
            }
        }

        #endregion

        #region Balance Metrics

        /// <summary>
        /// Record periodic balance report.
        /// Key metric: blackout_percent — target is ~20% per CRISIS_MODEL.md
        /// </summary>
        private void RecordBalanceReport()
        {
            // balance.session_report is a developer-tuning signal (blackout%-vs-target) — the
            // chronicle reads balance.wave_snapshot, not this. It feeds no player-facing
            // feature, so it is analytics: gate it on the effective diagnostics gate and do
            // not emit it when Online is on but diagnostics is off (§2). The window
            // accumulators below still reset so the next window is clean once diagnostics
            // returns.
            if (!m_Config.Enabled)
            {
                m_BalanceWindowSessionTime = 0.0;
                m_BalanceWindowBlackoutTime = 0.0;
                return;
            }

            if (m_BalanceWindowSessionTime < SECONDS_PER_MINUTE_D) return; // Skip if less than 1 minute

            double blackoutPercent = m_BalanceWindowSessionTime > 0.0
                ? m_BalanceWindowBlackoutTime / m_BalanceWindowSessionTime * 100.0
                : 0.0;

            Record(Balance.SessionReport, new BalanceSessionReportData
            {
                SessionMinutes = (float)Math.Round(m_BalanceWindowSessionTime / SECONDS_PER_MINUTE_D, 1),
                BlackoutMinutes = (float)Math.Round(m_BalanceWindowBlackoutTime / SECONDS_PER_MINUTE_D, 1),
                BlackoutPercent = (float)Math.Round(blackoutPercent, 1),
                TargetPercent = BLACKOUT_TARGET_PERCENT,
                BalanceDelta = (float)Math.Round(blackoutPercent - BLACKOUT_TARGET_PERCENT_D, 1)
            });

            Log.Info($" Balance report: {blackoutPercent:F1}% blackout time (target: 20%, delta: {blackoutPercent - 20f:+0.0;-0.0}%)");
            m_BalanceWindowSessionTime = 0.0;
            m_BalanceWindowBlackoutTime = 0.0;
        }

        /// <summary>
        /// Reset balance-only blackout accounting at save-load boundaries.
        /// TotalSessionTime remains process-session playtime for session_end telemetry.
        /// </summary>
        private void OnGameLoaded(GameLoadedEvent evt)
        {
            m_BalanceWindowSessionTime = 0.0;
            m_BalanceWindowBlackoutTime = 0.0;
            m_TimeSinceLastBalanceReport = 0.0;

            // City changed: drop the previous city's crash-context snapshot. CS2 reuses the
            // process, so a stale snapshot would otherwise mislabel a crash early in the new
            // city (before its first performance pulse re-populates the context).
            CrashContextProvider.Reset();
        }

        /// <summary>
        /// Capture player state snapshot before wave starts.
        /// Key metrics: money, MW capacity/production, AA systems/ammo.
        /// </summary>
        private void OnWaveStarting(WaveStartingEvent evt)
        {
            // Sync-free balance read (vanilla CitySystem.moneyAmount) — avoids forcing a
            // PlayerMoney CompleteDependency on the wave-starting event path. Falls back to 0
            // on the boot window before CitySystem exists, which never coincides with a wave.
            long money = CityBudgetService.TryGetCachedBalance(m_World, out var balance) ? balance : 0;

            // TOCTOU FIX: Use TryGetSingleton instead of IsEmpty + GetSingleton
            int productionMW = 0;
            int demandMW = 0;
            int balanceMW = 0;
            var grid = m_PowerGridQuery.TryGetSingleton<PowerGridSingleton>(out var pg)
                ? pg : PowerGridSingleton.Default;
            productionMW = grid.Production / 1000;
            demandMW = grid.Demand / 1000;
            balanceMW = grid.Balance / 1000;

            // Get AA stats — count + per-type fleet composition for balance telemetry, both read from
            // the same one-shot Temp array (a wave fires minutes apart, so the survey cost is trivial).
            int aaCount = 0;
            int aaAmmoTotal = 0;
            int aaAmmoMax = 0;
            int aaHeritage = 0;
            int aaBofors = 0;
            int aaGepard = 0;
            int aaPatriot = 0;
            if (!m_AirDefenseQuery.IsEmpty)
            {
                var aaInstallations = m_AirDefenseQuery.ToComponentDataArray<AirDefenseInstallation>(Allocator.Temp);
                try
                {
                    aaCount = aaInstallations.Length;
                    foreach (var aa in aaInstallations)
                    {
                        aaAmmoTotal += aa.CurrentAmmo;
                        aaAmmoMax += aa.MaxAmmo;
                        switch (aa.Type)
                        {
                            case AAType.HeritageBofors: aaHeritage++; break;
                            case AAType.Bofors40mm: aaBofors++; break;
                            case AAType.Gepard: aaGepard++; break;
                            case AAType.PatriotSAM: aaPatriot++; break;
                            default: break;
                        }
                    }
                }
                finally
                {
                    if (aaInstallations.IsCreated) aaInstallations.Dispose();
                }
            }

            // Manpower pool + the crew-gated operational AA count it can man. OperationalAa reuses the
            // exact forecast crew-gate (AirDefenseForecast.OperationalAaFleet) so the telemetry value
            // matches the model that drives the sweep. TOTAL (not Available) manpower: the crew gate
            // re-applies the fleet's crew cost, so feeding it the already-net Available double-subtracts
            // and zeroes a fully-manned fleet (Used == Total => Available 0 => opAA 0) — the same bug
            // fixed in CrisisSweepRunner. Manpower singleton absent before the war starts.
            int manpowerTotal = 0;
            int manpowerUsed = 0;
            int operationalAa = 0;
            if (m_MobilizationQuery.TryGetSingleton<MobilizationStateSingleton>(out var mob))
            {
                manpowerTotal = mob.TotalManpower;
                manpowerUsed = mob.UsedManpower;
                var fleet = new AirDefenseForecast.FleetComposition(aaHeritage, aaBofors, aaGepard, aaPatriot);
                operationalAa = AirDefenseForecast.OperationalAaFleet(BalanceConfig.Current, manpowerTotal, in fleet);
            }

            // "Patriot intercepts drones" toggle — AirDefense-owned, null-object false when closed.
            m_PatriotDroneReader ??= ServiceRegistryFeatureExtensions.TryGetOrNullObject(NullPatriotDroneInterceptReader.Instance);
            bool patriotInterceptsDrones = m_PatriotDroneReader.PatriotInterceptsDrones;

            // Mental-health + stress context co-located per wave for balance correlation:
            // how close was the city to a LostControl collapse when this wave hit.
            GetCognitiveSnapshot(out var cogAvg, out var cogMin, out var compromisedDistricts);
            int blackoutDistricts = m_GetActiveBlackoutDistricts();
            int population = 0;
            // Wave start is a rare event (minutes apart), so the one-off population read here is
            // acceptable at the same cost the periodic performance sample already pays.
            if (m_World.IsCreated)
            {
                try
                {
                    population = PopulationUtils.GetCitizenCount();
                }
                catch (Exception ex)
                {
                    if (Log.IsDebugEnabled) Log.Debug($" Population query failed: {ex.Message}");
                }
            }

            Record(Balance.WaveSnapshot, new BalanceWaveSnapshotData
            {
                WaveNumber = evt.WaveNumber,
                ThreatsExpected = evt.ThreatsExpected,
                Money = money,
                ProductionMw = productionMW,
                DemandMw = demandMW,
                BalanceMw = balanceMW,
                ReservePercent = demandMW > 0 ? (float)Math.Round((float)(productionMW - demandMW) / demandMW * 100, 1) : 0f,
                AACount = aaCount,
                AAAmmoCurrent = aaAmmoTotal,
                AAAmmoMax = aaAmmoMax,
                AAAmmoPercent = aaAmmoMax > 0 ? (float)Math.Round((float)aaAmmoTotal / aaAmmoMax * 100, 1) : 0f,
                CognitiveIntegrityAvg = cogAvg.HasValue ? (float?)Math.Round(cogAvg.Value, 3) : null,
                CognitiveIntegrityMin = cogMin.HasValue ? (float?)Math.Round(cogMin.Value, 3) : null,
                CompromisedDistricts = compromisedDistricts,
                Population = population,
                BlackoutDistricts = blackoutDistricts,
                AaHeritage = aaHeritage,
                AaBofors = aaBofors,
                AaGepard = aaGepard,
                AaPatriot = aaPatriot,
                ManpowerTotal = manpowerTotal,
                ManpowerUsed = manpowerUsed,
                OperationalAa = operationalAa,
                PatriotInterceptsDrones = patriotInterceptsDrones
            });

            string integrityStr = cogAvg.HasValue
                ? $"avg {cogAvg.Value:F2}/min {cogMin.GetValueOrDefault():F2} ({compromisedDistricts.GetValueOrDefault()} compromised)"
                : "not measured";
            Log.Info($" Wave #{evt.WaveNumber} snapshot ({evt.ThreatsExpected} threats): ${money:N0}, {productionMW}MW/{demandMW}MW (bal:{balanceMW}), {aaCount} AA ({aaAmmoTotal}/{aaAmmoMax} rounds), integrity {integrityStr}, pop {population}, {blackoutDistricts} blackout");
        }

        #endregion
    }
}
