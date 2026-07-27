using CivicSurvival.Core.Interfaces.Core;

namespace CivicSurvival.Core.Events
{
    /// <summary>
    /// Debt event types for consolidated DebtEvent.
    /// </summary>
    public enum DebtEventType
    {
        DebtAdded = 0,
        MonthlyPaymentMade,
        PartialPaymentMade,
        PaymentMissed,
        DebtWarning,
        DebtRestructured,
        DebtRelief
    }

    /// <summary>
    /// Published when city debt changes.
    /// Published by: CityDebtService
    /// Consumed by: FinanceUIPanel, TelemetryService
    /// </summary>
    /// <param name="Type">Type of debt operation</param>
    /// <param name="Amount">Amount involved in this operation</param>
    /// <param name="TotalDebt">Total debt after operation</param>
    /// <param name="Category">Debt category (null for payments)</param>
    public record DebtEvent(
        DebtEventType Type,
        long Amount,
        long TotalDebt,
        string? Category = null
    ) : IGameEvent;
}
