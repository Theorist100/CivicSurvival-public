using Game;
using Game.Events;
using Unity.Entities;
using CivicSurvival.Core.Utils;
using CivicSurvival.Core.Events;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Components.Domain.Power;
using CivicSurvival.Core.Components.Threats;
using CivicSurvival.Core.Components.Domain.Economy;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Systems.Base;
using Game.Simulation;
using CivicSurvival.Core.Attributes;

namespace CivicSurvival.Core.Systems
{
    /// <summary>
    /// Periodically logs diagnostic reports: memory usage, state, errors.
    /// Automatically collects data from EventBus, ServiceRegistry, and ECS singletons.
    ///
    /// Output example:
    /// ════ Report (60s) ════
    /// Memory: EventBus.Subscribers=45, EventBus.Types=12, Services=8
    /// State: Grid=Warning (Prod=850MW, Cons=920MW), Threats=2, Shadow=$5000
    /// Errors: NullService=0, FailedAttack=2 (total=5)
    /// </summary>
    [ActIndependent]
    public partial class DiagnosticReportSystem : CivicSystemBase
    {
        private static readonly LogContext Log = new("DiagnosticReportSystem");

        private const float REPORT_INTERVAL_SECONDS = 60f;
        // ~17s at 60 ticks/s. Must be power of 2 (CS2 requirement).
        private const int UPDATE_INTERVAL_TICKS = 1024;

        public override int GetUpdateInterval(SystemUpdatePhase phase) => UPDATE_INTERVAL_TICKS;

        private float m_TimeSinceLastReport;

#if DEBUG
        private bool m_SubscriptionValidated;
#endif

        // ECS queries for singletons
        private EntityQuery m_PowerGridQuery;
        private EntityQuery m_ThreatStatsQuery;
        private EntityQuery m_ShadowWalletQuery;

        // Accumulation-watch queries (native-memory leak hunt). These entity classes grow
        // during combat and are expected to drain back toward a between-wave floor once a
        // wave ends. A class whose live count ratchets up wave-over-wave is the source of the
        // big-city late-game native pressure behind the crash family (coarse native-crash
        // breadcrumbs scatter across Burst jobs under memory pressure, so the faulting marker
        // does not localize the leak — this count does).
        private EntityQuery m_OnFireQuery;
        private EntityQuery m_FireEventQuery;
        private EntityQuery m_FallingDebrisQuery;
        private EntityQuery m_CivDamageQuery;
        private EntityQuery m_PlantDamageQuery;
        private EntityQuery m_ThreatPosQuery;
        // Combat render/aftermath classes — candidates for the wave-onset native pressure that
        // is NOT in our own buffers (NativeFootprintTracker shows those flat at ~3 MB): AA tracer
        // and interceptor render entities, and destroyed buildings (vanilla destruction render
        // state we trigger). Counts only — no behaviour change.
        private EntityQuery m_TracerQuery;
        private EntityQuery m_InterceptorQuery;
        private EntityQuery m_DestroyedBuildingQuery;

        // Cached services (CIVIC018: avoid ServiceRegistry.Get in OnUpdate)
        private EventBus? m_EventBus;

        protected override void OnCreate()
        {
            base.OnCreate();

            // Create singleton queries
            m_PowerGridQuery = GetEntityQuery(ComponentType.ReadOnly<PowerGridSingleton>());
            m_ThreatStatsQuery = GetEntityQuery(ComponentType.ReadOnly<ThreatStatsSingleton>());
            m_ShadowWalletQuery = GetEntityQuery(ComponentType.ReadOnly<ShadowWalletSingleton>());

            m_OnFireQuery = GetEntityQuery(ComponentType.ReadOnly<OnFire>());
            m_FireEventQuery = GetEntityQuery(ComponentType.ReadOnly<Fire>());
            m_FallingDebrisQuery = GetEntityQuery(ComponentType.ReadOnly<FallingDebris>());
            m_CivDamageQuery = GetEntityQuery(ComponentType.ReadOnly<CivilianWarDamage>());
            m_PlantDamageQuery = GetEntityQuery(ComponentType.ReadOnly<PowerPlantDamage>());
            m_ThreatPosQuery = GetEntityQuery(ComponentType.ReadOnly<ThreatPosition>());
            m_TracerQuery = GetEntityQuery(ComponentType.ReadOnly<CivicSurvival.Core.Components.Domain.AirDefense.Tracer>());
            m_InterceptorQuery = GetEntityQuery(ComponentType.ReadOnly<CivicSurvival.Core.Components.AirDefense.Interceptor>());
            m_DestroyedBuildingQuery = GetEntityQuery(
                ComponentType.ReadOnly<Game.Common.Destroyed>(),
                ComponentType.ReadOnly<Game.Buildings.Building>(),
                ComponentType.Exclude<Game.Common.Deleted>());

            Log.Info($"Initialized — reporting every {REPORT_INTERVAL_SECONDS:F0}s (throttle={UPDATE_INTERVAL_TICKS} ticks)");
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            // Cache EventBus for diagnostic reports (concrete class needed for metrics).
            m_EventBus ??= ServiceRegistry.Instance.Require<IEventBus>() as EventBus;
        }

        protected override void OnUpdateImpl()
        {
            // Debug-gated accumulation watch for the native-memory hunt — emitted on every update
            // (the system's own 1024-tick throttle = ~17s spacing), independent of the 60s report
            // timer below so the series has fine resolution when Debug is on. No-ops at Info, so it
            // adds no player-facing log noise. The 60s timer below accumulates the per-frame
            // World.Time.DeltaTime, which on a 1024-tick-throttled system is the small frame delta
            // (not the interval), so it takes far longer than 60s of play to trip.
            LogAccumulationReport();

            float deltaTime = World.Time.DeltaTime;
            m_TimeSinceLastReport += deltaTime;

            if (m_TimeSinceLastReport < REPORT_INTERVAL_SECONDS)
                return;

            m_TimeSinceLastReport = 0f;

            if (Log.IsDebugEnabled)
                LogReport();
            else
                DiagnosticTracker.GetSnapshotAndReset();
        }

        /// <summary>
        /// Debug-level accumulation watch: once per update (~17s), logs the live count of the
        /// entity classes that grow during combat and are expected to drain when a wave ends —
        /// burning targets (<c>OnFire</c>), fire event entities (<c>Fire</c>), falling debris,
        /// and civilian/plant damage sidecars — alongside total native allocation (the same
        /// <c>GetTotalAllocatedMemoryLong</c> that the native-crash breadcrumb records). A count
        /// that ratchets up wave-over-wave (does not fall back toward its between-wave floor)
        /// localizes the source of the big-city late-game native pressure. Read the trend by
        /// grepping the series of <c>[ACCUM]</c> lines across a session.
        /// </summary>
        private void LogAccumulationReport()
        {
            // Debug-gated: opt-in diagnostic, not player-facing Info noise (it would emit ~1 line
            // per ~17s of play). Flip the mod log level to Debug to capture the [ACCUM] series.
            if (!Log.IsDebugEnabled)
                return;

            // WithoutFiltering: these are plain (non-enableable) IComponentData, so the count is
            // identical to the filtered count but skips the enableable-types sync point a plain
            // CalculateEntityCount() would force (CIVIC220) — a diagnostic must not stall combat.
            int onFire = m_OnFireQuery.CalculateEntityCountWithoutFiltering();
            int fireEvents = m_FireEventQuery.CalculateEntityCountWithoutFiltering();
            int debris = m_FallingDebrisQuery.CalculateEntityCountWithoutFiltering();
            int civDamage = m_CivDamageQuery.CalculateEntityCountWithoutFiltering();
            int plantDamage = m_PlantDamageQuery.CalculateEntityCountWithoutFiltering();
            int threats = m_ThreatPosQuery.CalculateEntityCountWithoutFiltering();
            int tracers = m_TracerQuery.CalculateEntityCountWithoutFiltering();
            int interceptors = m_InterceptorQuery.CalculateEntityCountWithoutFiltering();
            int destroyed = m_DestroyedBuildingQuery.CalculateEntityCountWithoutFiltering();
            long nativeMb = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() / (1024 * 1024);

            Log.Debug($"[ACCUM] NativeMB={nativeMb} OnFire={onFire} FireEvents={fireEvents} " +
                $"Debris={debris} CivDmg={civDamage} PlantDmg={plantDamage} Threats={threats} " +
                $"Tracers={tracers} Interceptors={interceptors} DestroyedBld={destroyed} " +
                CivicSurvival.Core.Diagnostics.NativeFootprintTracker.Format());
        }

        private void LogReport()
        {
            Log.Info($"════ Report ({REPORT_INTERVAL_SECONDS:F0}s) ════");

#if DEBUG
            if (!m_SubscriptionValidated)
            {
                m_SubscriptionValidated = true;
                m_EventBus?.ValidateSubscriptions(typeof(CivicSurvival.Mod).Assembly);
            }
#endif

            // Memory: EventBus and ServiceRegistry
            LogMemoryMetrics();

            // State: ECS singletons
            LogStateMetrics();

            // User-tracked metrics from DiagnosticTracker
            LogTrackerMetrics();
        }

        private void LogMemoryMetrics()
        {
            // Guard against shutdown order (ServiceRegistry may be disposed first)
            if (!ServiceRegistry.IsInitialized)
                return;

#pragma warning disable CIVIC050 // debug-only (IsDebugEnabled guard)
            var parts = new System.Collections.Generic.List<string>();
#pragma warning restore CIVIC050

            // EventBus metrics
            var eventBus = m_EventBus;
            if (eventBus != null)
            {
                int subscribers = eventBus.GetTotalSubscriberCount();
                int eventTypes = eventBus.GetEventTypeCount();
                parts.Add($"EventBus.Subscribers={subscribers}");
                parts.Add($"EventBus.Types={eventTypes}");

                // Track for trend analysis
                DiagnosticTracker.TrackMemory("EventBus.Subscribers", subscribers);
                DiagnosticTracker.TrackMemory("EventBus.Types", eventTypes);
            }

            // ServiceRegistry count
            if (ServiceRegistry.IsInitialized)
            {
                int serviceCount = ServiceRegistry.Instance.Count;
                parts.Add($"Services={serviceCount}");
                DiagnosticTracker.TrackMemory("Services", serviceCount);
            }

            if (parts.Count > 0)
            {
                Log.Info($"Memory: {string.Join(", ", parts)}");
            }
        }

        private void LogStateMetrics()
        {
#pragma warning disable CIVIC050 // debug-only (IsDebugEnabled guard)
            var parts = new System.Collections.Generic.List<string>();
#pragma warning restore CIVIC050

            // TOCTOU FIX: Use TryGetSingleton instead of IsEmpty + GetSingleton
            // PowerGrid state
            if (m_PowerGridQuery.TryGetSingleton<PowerGridSingleton>(out var grid))
            {
                parts.Add($"Grid={grid.Status} (Prod={grid.Production / 1000}MW, Cons={grid.Consumption / 1000}MW, Bal={grid.Balance / 1000}MW)");
                DiagnosticTracker.TrackState("Grid.Status", grid.Status.ToString());
                DiagnosticTracker.TrackState("Grid.Balance", grid.Balance / 1000);
            }

            // Threat stats
            if (m_ThreatStatsQuery.TryGetSingleton<ThreatStatsSingleton>(out var threats))
            {
                parts.Add($"Threats={threats.TotalActiveCount} (Shahed={threats.ActiveShahedCount}, Ballistic={threats.ActiveBallisticCount})");
                DiagnosticTracker.TrackState("Threats.Active", threats.TotalActiveCount);
            }

            // Shadow wallet
            if (m_ShadowWalletQuery.TryGetSingleton<ShadowWalletSingleton>(out var wallet))
            {
                parts.Add($"Shadow=${wallet.Balance:N0}");
                DiagnosticTracker.TrackState("Shadow.Balance", (int)wallet.Balance);
            }

            if (parts.Count > 0)
            {
                Log.Info($"State: {string.Join(", ", parts)}");
            }
        }

        private void LogTrackerMetrics()
        {
            var snapshot = DiagnosticTracker.GetSnapshotAndReset();

            // Additional memory from tracker (beyond auto-collected)
            if (snapshot.Memory.Count > 0)
            {
#pragma warning disable CIVIC050 // debug-only (IsDebugEnabled guard)
                var parts = new System.Collections.Generic.List<string>();
#pragma warning restore CIVIC050
                foreach (var kvp in snapshot.Memory)
                {
                    // Skip auto-collected (already logged above)
                    if (kvp.Key.StartsWith("EventBus.") || kvp.Key == "Services")
                        continue;

                    var (current, peak) = kvp.Value;
                    if (current != peak)
                        parts.Add($"{kvp.Key}={current} (peak={peak})");
                    else
                        parts.Add($"{kvp.Key}={current}");
                }
                if (parts.Count > 0)
                {
                    Log.Info($"Memory+: {string.Join(", ", parts)}");
                }
            }

            // Errors
            if (snapshot.ErrorsPeriod.Count > 0 || snapshot.ErrorsTotal.Count > 0)
            {
#pragma warning disable CIVIC050 // debug-only (IsDebugEnabled guard)
                var parts = new System.Collections.Generic.List<string>();
#pragma warning restore CIVIC050
                foreach (var kvp in snapshot.ErrorsTotal)
                {
                    snapshot.ErrorsPeriod.TryGetValue(kvp.Key, out int period);
                    if (period > 0)
                        parts.Add($"{kvp.Key}=+{period} (total={kvp.Value})");
                    else
                        parts.Add($"{kvp.Key}=0 (total={kvp.Value})");
                }
                Log.Info($"Errors: {string.Join(", ", parts)}");
            }
            else
            {
                Log.Info("Errors: none");
            }
        }

        protected override void OnDestroy()
        {
            if (Log.IsDebugEnabled)
            {
                Log.Debug("════ Final Report (shutdown) ════");
                LogMemoryMetrics();
                LogStateMetrics();
            }
            base.OnDestroy();
        }
    }
}
