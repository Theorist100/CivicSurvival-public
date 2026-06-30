using System;
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
        private readonly IEventBus m_EventBus;
        private readonly TelemetryRecorder m_Recorder;
        private readonly string m_SessionId;

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
            m_EventBus.Subscribe<DebtEvent>(OnDebtEvent);
            m_EventBus.Subscribe<OperationExecutedEvent>(OnOperationExecuted);
            m_EventBus.Subscribe<OperationCancelledEvent>(OnOperationCancelled);
            m_EventBus.Subscribe<SpotterActionEvent>(OnSpotterAction);
            m_EventBus.Subscribe<CounterOSINTToggledEvent>(OnCounterOSINTToggled);
            m_EventBus.Subscribe<IntelInsiderPurchasedEvent>(OnIntelInsiderPurchased);
            m_EventBus.Subscribe<IntelUpgradedEvent>(OnIntelUpgraded);
        }

        public void Dispose()
        {
            m_EventBus.Unsubscribe<CorruptionNarrativeEvent>(OnCorruptionNarrativeEvent);
            m_EventBus.Unsubscribe<CorruptionGainEvent>(OnCorruptionGain);
            m_EventBus.Unsubscribe<InvestigationStartedEvent>(OnInvestigationStarted);
            m_EventBus.Unsubscribe<ExportDeficitEvent>(OnExportDeficit);
            m_EventBus.Unsubscribe<CountermeasuresChoiceEvent>(OnCountermeasuresChoice);
            m_EventBus.Unsubscribe<ShadowNarrativeEvent>(OnShadowNarrativeEvent);
            m_EventBus.Unsubscribe<DebtEvent>(OnDebtEvent);
            m_EventBus.Unsubscribe<OperationExecutedEvent>(OnOperationExecuted);
            m_EventBus.Unsubscribe<OperationCancelledEvent>(OnOperationCancelled);
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
