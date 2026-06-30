using System;
using System.Threading;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Utils;
using CivicSurvival.Domains.GridWarfare.Events;
using CivicSurvival.Core.Services;

namespace CivicSurvival.Services.Arena
{
    /// <summary>
    /// Tracks GridWarfare activity and reports to server for leaderboards.
    /// Accumulates damage/floor_hits and sends every REPORT_INTERVAL.
    /// Persists data to disk to prevent loss on crash.
    /// </summary>
    public sealed class ArenaReporter : IDisposable
    {
        private static readonly LogContext Log = new("ArenaReporter");

        private const float REPORT_INTERVAL = 300f; // 5 minutes
        private const float SAVE_INTERVAL = 60f;    // Save to disk every minute
        // Streak-broken threshold: an axis climbing back above this counts as the enemy
        // re-establishing dominance on that axis. No remote-config field exists for it
        // (unlike PressureFloor), so it stays a named constant.
        private const float PRESSURE_HIGH = 80f;
        private const float CIRCUIT_BREAKER_COOLDOWN_SECONDS = 300f;

        private TelemetryConfig m_Config;
        private readonly TelemetryAuth m_Auth;
        private readonly IEventBus m_Events = null!;
        private readonly ArenaPersistence m_Persistence;

        // Accumulated since last report
        // WARN-8-4 fix: lock for thread-safe accumulator access (callback vs main thread)
        private readonly object m_AccumulatorLock = new();
        private int m_DamageDealt;
        private int m_ShadowSpent;
        // Count of axis "suppressions" — an enemy axis driven down to its floor (the soft-kill
        // signal from Phase 3.6). Replaces the dead vulnerable-window metric (RPS stances removed
        // in 3.0). Carried over the wire by ArenaReportRequest.VulnerableHits, whose field name is
        // kept until the server/DB schema is next touched (a rename there needs a migration).
        private int m_Suppressions;
        private bool m_FloorHit;
        private bool m_StreakBroken;

        private float m_TimeSinceLastReport;
        private float m_TimeSinceLastSave;
        private bool m_Disposed;
        private volatile bool m_SendInProgress;
        private readonly CircuitBreakerState m_ReportBreaker = new(failureThreshold: 3, cooldownSeconds: CIRCUIT_BREAKER_COOLDOWN_SECONDS);

        public ArenaReporter(TelemetryConfig config, TelemetryAuth auth)
        {
            m_Config = config ?? throw new ArgumentNullException(nameof(config));
            m_Auth = auth ?? throw new ArgumentNullException(nameof(auth));
            m_Persistence = new ArenaPersistence(config);

            m_Events = ServiceRegistry.Instance.Require<IEventBus>();

            // Recover any pending data from previous session
            RecoverPendingData();

            m_Events.Subscribe<OperationExecutedEvent>(OnOperationExecuted);
            m_Events.Subscribe<EnemyAxisChangedEvent>(OnEnemyAxisChanged);

            Log.Info(" Initialized — tracking GridWarfare activity");
        }

        /// <summary>
        /// Swap in a freshly loaded config (e.g. after the Online connection toggles).
        /// Only the immutable server URL / timeouts and the OnlineEnabled gate are read
        /// from it; accumulators and persistence are preserved across the swap.
        /// </summary>
        public void RefreshConfig(TelemetryConfig config)
        {
            m_Config = config ?? throw new ArgumentNullException(nameof(config));
        }

        private void RecoverPendingData()
        {
            var recovered = m_Persistence.Recover();
            if (recovered != null && recovered.HasData())
            {
#pragma warning disable CIVIC114 // Constructor context — no concurrent threads yet
                m_DamageDealt = recovered.DamageDealt;
                m_ShadowSpent = recovered.ShadowSpent;
                m_Suppressions = recovered.VulnerableHits;
                m_FloorHit = recovered.FloorHit;
                m_StreakBroken = recovered.StreakBroken;
#pragma warning restore CIVIC114

                Log.Info($" Recovered pending data from previous session");
            }
        }

        private void OnOperationExecuted(OperationExecutedEvent evt)
        {
            // CRIT-1 FIX: Lock required — EventBus callback (main thread) races with
            // HTTP failure callback (thread pool) that also writes these fields
            lock (m_AccumulatorLock)
            {
                AddSaturated(ref m_DamageDealt, ToNonNegativeInt(evt.ActualDamage));
                AddSaturated(ref m_ShadowSpent, ToNonNegativeInt(evt.ShadowSpent));
            }

            if (Log.IsDebugEnabled) Log.Debug($" Operation: +{evt.ActualDamage:F0} damage, ${evt.ShadowSpent:N0} spent");
        }

        private void OnEnemyAxisChanged(EnemyAxisChangedEvent evt)
        {
            // Crossing detection is per-axis and stateless: the event carries this axis's own
            // OldValue→NewValue, so each of the three independent axes (Physical/Digital/Social)
            // is judged against its own prior value — no shared last-value that bleeds one axis's
            // movement into another's threshold check. Being stateless, it also survives save/load
            // without a phantom crossing on the first post-load event.
            float floor = Core.Config.BalanceConfig.Current.GridWarfare.PressureFloor;

            bool floorHit = false;
            bool streakBroken = false;

            // CRIT-1 FIX: Lock required — same race as OnOperationExecuted
            lock (m_AccumulatorLock)
            {
                // Suppression / floor-hit: this axis crossed the floor downward. Each crossing is
                // one "suppression" — the meaningful arena tally after RPS vulnerable windows were
                // removed (the old m_VulnerableHits is now this count).
                if (evt.OldValue > floor && evt.NewValue <= floor)
                {
                    m_FloorHit = true;
                    floorHit = true;
                    AddSaturated(ref m_Suppressions, 1);
                }

                // Streak broken detection: this axis recovered past the high threshold upward.
                if (evt.OldValue < PRESSURE_HIGH && evt.NewValue >= PRESSURE_HIGH)
                {
                    m_StreakBroken = true;
                    streakBroken = true;
                }
            }
            if (floorHit) Log.Info($" FLOOR HIT! {evt.Axis} axis reached {floor:F0}%");
            if (streakBroken) Log.Info($" Streak broken — {evt.Axis} axis exceeded {PRESSURE_HIGH:F0}%");
        }

        /// <summary>
        /// Called from TelemetryService.OnUpdate(). Sends report if interval elapsed.
        /// </summary>
        public void Update(float deltaTime)
        {
            if (m_Disposed) return;

            m_TimeSinceLastReport += deltaTime;
            m_TimeSinceLastSave += deltaTime;

            // Periodic save to disk (crash protection)
            if (m_TimeSinceLastSave >= SAVE_INTERVAL && HasPendingData())
            {
                SaveToDisk();
                m_TimeSinceLastSave = 0;
            }

            // Send to server
            if (m_TimeSinceLastReport >= REPORT_INTERVAL && HasPendingData() && !m_SendInProgress)
            {
                if (SendReport())
                    m_TimeSinceLastReport = 0;
            }
        }

        private bool HasPendingData()
        {
            // FIX H80: Lock required — HTTP failure callback (ThreadPool, :265) writes these fields
            // under m_AccumulatorLock. Reading without lock = cross-thread race (torn reads on int fields).
            lock (m_AccumulatorLock)
            {
                return m_DamageDealt > 0 || m_ShadowSpent > 0 || m_FloorHit || m_StreakBroken || m_Suppressions > 0;
            }
        }

        private void SaveToDisk()
        {
            // FIX H80: Lock required — same race as HasPendingData (ThreadPool writes under lock)
            PendingArenaData data;
            lock (m_AccumulatorLock)
            {
                data = new PendingArenaData
                {
                    DamageDealt = m_DamageDealt,
                    ShadowSpent = m_ShadowSpent,
                    VulnerableHits = m_Suppressions,
                    FloorHit = m_FloorHit,
                    StreakBroken = m_StreakBroken,
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                };
            }

            m_Persistence.Save(data);
        }

        private bool SendReport()
        {
            if (!m_Config.OnlineEnabled)
            {
                Log.Debug(" Online disabled, skipping server report");
                return false;
            }

            if (!m_Auth.IsRegistered || string.IsNullOrEmpty(m_Auth.AuthToken))
            {
                Log.Debug(" Not registered, skipping server report");
                return false;
            }

            float requestTime = UnityEngine.Time.realtimeSinceStartup;
            if (!m_ReportBreaker.CanProceed(requestTime))
            {
                if (Log.IsDebugEnabled) Log.Debug(" Report skipped: circuit breaker open");
                return false;
            }

            if (!m_ReportBreaker.TryBeginProbe(requestTime, out var probe))
            {
                if (Log.IsDebugEnabled) Log.Debug(" Report skipped: probe already in flight");
                return false;
            }

            // WARN-8-4 fix: capture values under lock for thread safety
            int damage, shadow, vulnerable;
            bool floor, broken;
            lock (m_AccumulatorLock)
            {
                damage = m_DamageDealt;
                shadow = m_ShadowSpent;
                vulnerable = m_Suppressions;
                floor = m_FloorHit;
                broken = m_StreakBroken;

                // Reset accumulators immediately (before async call)
                ResetAccumulatorsUnsafe();
            }
            var sentGeneration = m_Persistence.Save(new PendingArenaData
            {
                DamageDealt = damage,
                ShadowSpent = shadow,
                VulnerableHits = vulnerable,
                FloorHit = floor,
                StreakBroken = broken,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });
            m_SendInProgress = true;

            var json = BuildReportJson(damage, shadow, vulnerable, floor, broken);
            var url = m_Config.ServerUrl + "/arena/report";
            var authToken = m_Auth.AuthToken;
            var terminalDelivered = new int[1];

            HttpUtils.PostAsync<ArenaReportResponse>(
                url,
                json,
                authToken: authToken,
                timeoutMs: m_Config.HttpTimeoutMs,
                maxRetries: m_Config.HttpMaxRetries,
                parser: ArenaWireReader.ParseArenaReportResponse,
                onComplete: result =>
                {
                    if (Interlocked.Exchange(ref terminalDelivered[0], 1) != 0)
                        return;

                    try
                    {
                        if (result.Success)
                        {
                            var response = result.Parsed!;
                            var position = response.Position;
                            var weeklyPosition = response.WeeklyPosition;
                            var rank = response.NewRank;

                            m_Persistence.Clear(sentGeneration);
                            m_ReportBreaker.RecordSuccess(probe);
                            Log.Info($" Report sent (attempts={result.AttemptsUsed}): damage={damage}, shadow={shadow}, floor={floor}, streak={broken}, vulnerable={vulnerable}, rank={rank}, pos={position}, weekly={weeklyPosition}");
                        }
                        else
                        {
                            Log.Warn($" Report failed after {result.AttemptsUsed} attempts: {result.ErrorMessage}");
                            m_ReportBreaker.RecordFailure(probe);
                            RequeueReport(damage, shadow, vulnerable, floor, broken);
                        }
                    }
                    finally
                    {
                        m_SendInProgress = false;
                    }
                },
                onFinished: () =>
                {
                    if (Interlocked.CompareExchange(ref terminalDelivered[0], 1, 0) != 0)
                        return;

                    Log.Warn(" Report send skipped during unload; re-queued pending Arena data");
                    m_ReportBreaker.CancelProbe(probe);
                    RequeueReport(damage, shadow, vulnerable, floor, broken);
                    m_SendInProgress = false;
                });
            return true;
        }

        private void RequeueReport(int damage, int shadow, int vulnerable, bool floor, bool broken)
        {
            lock (m_AccumulatorLock)
            {
                AddSaturated(ref m_DamageDealt, damage);
                AddSaturated(ref m_ShadowSpent, shadow);
                AddSaturated(ref m_Suppressions, vulnerable);
                m_FloorHit |= floor;
                m_StreakBroken |= broken;
            }
            SaveToDisk();
        }

        /// <summary>
        /// Reset accumulators. Must be called under m_AccumulatorLock.
        /// </summary>
        [CallerHoldsLock(nameof(m_AccumulatorLock))]
        private void ResetAccumulatorsUnsafe()
        {
            m_DamageDealt = 0;
            m_ShadowSpent = 0;
            m_Suppressions = 0;
            m_FloorHit = false;
            m_StreakBroken = false;
        }

        private string BuildReportJson(int damage, int shadow, int vulnerable, bool floor, bool broken)
        {
            var request = new ArenaReportRequest
            {
                // AuthToken now travels in the Authorization: Bearer header; the body
                // field stays in the contract as a transitional fallback and is left
                // empty (the server reads the header with priority).
                PlayerId = m_Auth.PlayerId,
                DamageDealt = damage,
                ShadowSpent = shadow,
                FloorHit = floor,
                VulnerableHits = vulnerable,
                StreakBroken = broken
            };
            return ArenaWireWriter.BuildArenaReportRequestJson(request);
        }

        private static int ToNonNegativeInt(float value)
        {
            if (!float.IsFinite(value) || value <= 0f) return 0;
            return value >= int.MaxValue ? int.MaxValue : checked((int)value);
        }

        private static int ToNonNegativeInt(long value)
        {
            if (value <= 0) return 0;
            return value >= int.MaxValue ? int.MaxValue : checked((int)value);
        }

        private static void AddSaturated(ref int target, int delta)
        {
            if (delta <= 0) return;
            long next = (long)target + delta;
            target = next >= int.MaxValue ? int.MaxValue : checked((int)next);
        }

        public void Dispose()
        {
            if (m_Disposed) return;
            m_Disposed = true;

            if (m_Events != null)
            {
                m_Events.Unsubscribe<OperationExecutedEvent>(OnOperationExecuted);
                m_Events.Unsubscribe<EnemyAxisChangedEvent>(OnEnemyAxisChanged);
            }

            // Final save to disk before shutdown
            if (HasPendingData())
            {
                Log.Debug(" Final save on dispose");
                SaveToDisk();
            }

            Log.Info(" Disposed");
        }
    }
}
