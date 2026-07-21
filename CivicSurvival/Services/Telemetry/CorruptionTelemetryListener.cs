using System;
using System.Collections.Generic;
using CivicSurvival.Core.Events;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Utils;
using CivicSurvival.Domains.GridWarfare.Events;
using static CivicSurvival.Services.Telemetry.EventTypes;

namespace CivicSurvival.Services.Telemetry
{
    /// <summary>
    /// Telemetry sub-listener for the corruption/economy axis: corruption surfaces,
    /// shadow economy, debt, GridWarfare operations, spotters, intel.
    /// </summary>
    internal sealed class CorruptionTelemetryListener : IDisposable
    {
        // Shadow income accrues per simulation tick (ShadowExport: ~$14 every few
        // seconds), and recording each accrual flooded the events table — one
        // active player produced ~50k rows/day, 74% of the table's daily intake
        // (prod, 2026-07-16). Telemetry batches per-Reason sums over a real-time
        // window instead; the game-side ShadowIncomeAppliedEvent stays per-tick
        // for live consumers. Balance analytics and the shadow ladder read sums,
        // so batching loses no money — only immediacy.
        //
        // Immediacy is exactly what the ladder needs, though: a player who just
        // switched a scheme on wants to appear on the board, not wait out a window
        // (they hit this the same day batching landed). So the first payout of a
        // Reason after a quiet stretch is sent AS IS, and batching engages only
        // while payouts keep coming — a flood is by definition many events in a
        // row, and its 2nd..Nth events are what the window has to swallow.
        private const long INCOME_FLUSH_WINDOW_MS = 300_000; // 5 min real time
        // Below this gap a Reason counts as "still streaming" and its payout joins
        // the window; above it the payout is a fresh start and goes out at once.
        // One game hour is ~40s real at 1x, so hourly schemes stream, while a
        // one-off (a scheme just enabled, a manual raid) reports immediately.
        private const long INCOME_STREAK_GAP_MS = 90_000; // 1.5 min real time

        private readonly IEventBus m_EventBus;
        private readonly TelemetryRecorder m_Recorder;
        private readonly string m_SessionId;
        private readonly Dictionary<string, long> m_PendingIncome = new();
        // Last time each Reason produced a payout — decides stream vs fresh start.
        private readonly Dictionary<string, long> m_LastIncomeMs = new();
        private long m_IncomeWindowStartMs = -1;

        // Monotonic milliseconds (Environment.TickCount64 is absent from the
        // Unity .NET profile; Stopwatch is monotonic and always available).
        private static long NowMs()
            => System.Diagnostics.Stopwatch.GetTimestamp() * 1000 / System.Diagnostics.Stopwatch.Frequency;

        public CorruptionTelemetryListener(IEventBus eventBus, TelemetryRecorder recorder, string sessionId)
        {
            m_EventBus = eventBus;
            m_Recorder = recorder;
            m_SessionId = sessionId;

            m_EventBus.Subscribe<CorruptionNarrativeEvent>(OnCorruptionNarrativeEvent);
            m_EventBus.Subscribe<CorruptionGainEvent>(OnCorruptionGain);
            m_EventBus.Subscribe<InvestigationStartedEvent>(OnInvestigationStarted);
            m_EventBus.Subscribe<ExportDeficitEvent>(OnExportDeficit);
            m_EventBus.Subscribe<CountermeasuresChoiceEvent>(OnCountermeasuresChoice);
            m_EventBus.Subscribe<ShadowNarrativeEvent>(OnShadowNarrativeEvent);
            m_EventBus.Subscribe<ShadowIncomeAppliedEvent>(OnShadowIncomeApplied);
            m_EventBus.Subscribe<DebtEvent>(OnDebtEvent);
            m_EventBus.Subscribe<OperationExecutedEvent>(OnOperationExecuted);
            m_EventBus.Subscribe<OperationCancelledEvent>(OnOperationCancelled);
            m_EventBus.Subscribe<BeachheadCollapsedEvent>(OnBeachheadCollapsed);
            m_EventBus.Subscribe<SpotterActionEvent>(OnSpotterAction);
            m_EventBus.Subscribe<CounterOSINTToggledEvent>(OnCounterOSINTToggled);
            m_EventBus.Subscribe<IntelInsiderPurchasedEvent>(OnIntelInsiderPurchased);
            m_EventBus.Subscribe<IntelUpgradedEvent>(OnIntelUpgraded);
        }

        public void Dispose()
        {
            // Session is ending — the partial income window must not be lost.
            FlushPendingIncome(NowMs());
            // Bounded by the number of income sources in game code (a handful), but
            // the listener is per-session: clear so a long process with many loaded
            // cities cannot accumulate stale Reasons (CIVIC152).
            m_LastIncomeMs.Clear();

            m_EventBus.Unsubscribe<CorruptionNarrativeEvent>(OnCorruptionNarrativeEvent);
            m_EventBus.Unsubscribe<CorruptionGainEvent>(OnCorruptionGain);
            m_EventBus.Unsubscribe<InvestigationStartedEvent>(OnInvestigationStarted);
            m_EventBus.Unsubscribe<ExportDeficitEvent>(OnExportDeficit);
            m_EventBus.Unsubscribe<CountermeasuresChoiceEvent>(OnCountermeasuresChoice);
            m_EventBus.Unsubscribe<ShadowNarrativeEvent>(OnShadowNarrativeEvent);
            m_EventBus.Unsubscribe<ShadowIncomeAppliedEvent>(OnShadowIncomeApplied);
            m_EventBus.Unsubscribe<DebtEvent>(OnDebtEvent);
            m_EventBus.Unsubscribe<OperationExecutedEvent>(OnOperationExecuted);
            m_EventBus.Unsubscribe<OperationCancelledEvent>(OnOperationCancelled);
            m_EventBus.Unsubscribe<BeachheadCollapsedEvent>(OnBeachheadCollapsed);
            m_EventBus.Unsubscribe<SpotterActionEvent>(OnSpotterAction);
            m_EventBus.Unsubscribe<CounterOSINTToggledEvent>(OnCounterOSINTToggled);
            m_EventBus.Unsubscribe<IntelInsiderPurchasedEvent>(OnIntelInsiderPurchased);
            m_EventBus.Unsubscribe<IntelUpgradedEvent>(OnIntelUpgraded);
        }

        private void Record(string type, object data) => m_Recorder.Record(m_SessionId, type, data);

        private void OnCorruptionNarrativeEvent(CorruptionNarrativeEvent evt)
        {
            Record(Corruption.Narrative, new CorruptionNarrativeData
            {
                Subtype = TelemetryMappers.MapCorruptionNarrativeSubtype(evt.Type),
                Percent = evt.Percent > 0 ? evt.Percent : null,
                ChargesCount = evt.ChargesCount > 0 ? evt.ChargesCount : null,
                StolenAmount = evt.StolenAmount > 0 ? evt.StolenAmount : null,
                Participants = evt.Participants > 0 ? evt.Participants : null
            });
        }

        private void OnCorruptionGain(CorruptionGainEvent evt)
        {
            Record(Corruption.Gain, new CorruptionGainData
            {
                Amount = evt.Amount,
                Source = evt.Source
            });
        }

        private void OnInvestigationStarted(InvestigationStartedEvent evt)
        {
            Record(Corruption.InvestigationStarted, new CorruptionInvestigationStartedData
            {
                FineAmount = evt.FineAmount > 0 ? evt.FineAmount : null
            });
        }

        private void OnExportDeficit(ExportDeficitEvent evt)
        {
            Record(Corruption.ExportDeficit, new CorruptionExportDeficitData
            {
                DeficitMw = evt.ExportedMW
            });
        }

        private void OnCountermeasuresChoice(CountermeasuresChoiceEvent evt)
        {
            Record(Corruption.CountermeasuresChoice, new CorruptionCountermeasuresChoiceData
            {
                ChoiceType = evt.ChoiceType,
                Choice = evt.Choice,
                Result = evt.Result
            });
        }

        private void OnShadowNarrativeEvent(ShadowNarrativeEvent evt)
        {
            Record(Shadow.Action, new ShadowActionData
            {
                Subtype = evt.Type.ToString().ToSnakeCase(),
                DistrictIndex = evt.DistrictIndex >= 0 ? evt.DistrictIndex : null,
                Cost = evt.Cost > 0 ? (int)Math.Min(evt.Cost, int.MaxValue) : null,
                KickbackAmount = evt.KickbackAmount > 0 ? evt.KickbackAmount : null,
                ContractType = evt.ContractType,
                SanctionDays = evt.SanctionDays > 0 ? evt.SanctionDays : null
            });
        }

        private void OnShadowIncomeApplied(ShadowIncomeAppliedEvent evt)
        {
            long now = NowMs();
            if (m_IncomeWindowStartMs < 0) m_IncomeWindowStartMs = now;

            bool isFreshStart = !m_LastIncomeMs.TryGetValue(evt.Reason, out var lastMs)
                || now - lastMs > INCOME_STREAK_GAP_MS;
            m_LastIncomeMs[evt.Reason] = now;

            if (isFreshStart)
            {
                // First payout after a quiet stretch: report it now so the player
                // shows up on the ladder within a minute of enabling the scheme.
                // Any pending sum for this Reason rides along, so nothing is lost.
                m_PendingIncome.TryGetValue(evt.Reason, out var carried);
                m_PendingIncome.Remove(evt.Reason);
                RecordIncome(evt.Reason, carried + evt.Amount);
                return;
            }

            m_PendingIncome.TryGetValue(evt.Reason, out var sum);
            m_PendingIncome[evt.Reason] = sum + evt.Amount;

            if (now - m_IncomeWindowStartMs >= INCOME_FLUSH_WINDOW_MS)
                FlushPendingIncome(now);
        }

        private void RecordIncome(string reason, long amount)
        {
            if (amount <= 0) return;
            Record(Shadow.Income, new ShadowIncomeData
            {
                Reason = reason,
                Amount = (int)Math.Min(amount, int.MaxValue)
            });
        }

        /// <summary>
        /// Emit one shadow.income record per accumulated Reason and open a new
        /// window. Amount semantics on the wire are unchanged — it is the sum
        /// credited, just covering the window instead of a single tick.
        /// </summary>
        private void FlushPendingIncome(long nowMs)
        {
            foreach (var pair in m_PendingIncome)
                RecordIncome(pair.Key, pair.Value);

            m_PendingIncome.Clear();
            m_IncomeWindowStartMs = nowMs;
        }

        private void OnDebtEvent(DebtEvent evt)
        {
            Record(Economy.Debt, new EconomyDebtData
            {
                Type = evt.Type.ToString().ToSnakeCase(),
                Amount = evt.Amount,
                TotalDebt = evt.TotalDebt,
                Category = evt.Category
            });
        }

        private void OnOperationExecuted(OperationExecutedEvent evt)
        {
            Record(Gridwarfare.OperationExecuted, new GridwarfareOperationExecutedData
            {
                AttackType = evt.AttackType,
                Category = evt.Category.ToString().ToSnakeCase(),
                BaseDamage = evt.BaseDamage,
                ActualDamage = evt.ActualDamage,
                WasBlocked = evt.WasBlocked,
                WasVulnerable = evt.WasVulnerable,
                ShadowSpent = evt.ShadowSpent
            });
        }

        private void OnBeachheadCollapsed(BeachheadCollapsedEvent evt)
        {
            Record(Gridwarfare.BeachheadCollapsed, new GridwarfareBeachheadCollapsedData
            {
                CollapseId = evt.CollapseId,
                LootShadowCash = evt.LootShadowCash
            });
        }

        private void OnOperationCancelled(OperationCancelledEvent evt)
        {
            Record(Gridwarfare.OperationCancelled, new GridwarfareOperationCancelledData
            {
                AttackType = evt.AttackType,
                RefundedAmount = evt.RefundedAmount
            });
        }

        private void OnSpotterAction(SpotterActionEvent evt)
        {
            Record(Spotter.Action, new SpotterActionData
            {
                ActionType = evt.ActionType,
                Cost = evt.Cost,
                Succeeded = evt.Succeeded
            });
        }

        private void OnCounterOSINTToggled(CounterOSINTToggledEvent evt)
        {
            Record(Spotter.CounterOsint, new SpotterCounterOsintData { Enabled = evt.Enabled });
        }

        private void OnIntelInsiderPurchased(IntelInsiderPurchasedEvent evt)
            => Record(Intel.InsiderPurchased, new IntelInsiderPurchasedData { Cost = evt.Cost });

        private void OnIntelUpgraded(IntelUpgradedEvent evt)
            => Record(Intel.Upgraded, new IntelUpgradedData { NewLevel = evt.NewLevel, Cost = evt.Cost });
    }
}
