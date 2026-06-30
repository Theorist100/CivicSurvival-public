using CivicSurvival.Core.Types;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Components.Domain.GridWarfare;

namespace CivicSurvival.Domains.GridWarfare.Events
{
    /// <summary>Outcome of a paid/donor arsenal procurement batch.</summary>
    public enum ArsenalProcurementResult { Granted = 0, BudgetFailed, Dropped }

    /// <summary>
    /// Published when an arsenal procurement batch resolves (granted, budget-failed,
    /// or dropped). Narrative + telemetry subscribe. Mirror of <c>AAResupplyEvent</c>.
    /// </summary>
    public record ArsenalProcurementEvent(
        ArsenalProcurementResult Result,
        ArsenalKind Kind,
        int Count = 0,
        long Cost = 0
    ) : IGameEvent;

    /// <summary>
    /// Queued when player executes an operation, then drained in ModificationEnd
    /// where the enemy axis write is phase-owned. Published after the effect is
    /// applied so telemetry and arena listeners observe committed damage.
    /// WasBlocked/WasVulnerable are RPS leftovers (always false in the axis model);
    /// they are dropped together with PlayerAttackSystem's slot fields in a later phase.
    /// </summary>
    public record OperationExecutedEvent(
        string AttackType,
        AttackCategory Category,
        float BaseDamage,
        float ActualDamage,
        bool WasBlocked,
        bool WasVulnerable,
        long ShadowSpent,
        string OperationId,
        int ExecutionId
    ) : IGameEvent;

    /// <summary>
    /// Published when an enemy axis (physical/digital/social) changes significantly.
    /// UI/telemetry subscribe for visual feedback. <see cref="Axis"/> is the attack
    /// category whose axis moved (Kinetic→Physical, Cyber→Digital, Psyops→Social).
    /// </summary>
    public record EnemyAxisChangedEvent(
        float OldValue,
        float NewValue,
        AttackCategory Axis,
        string Cause
    ) : IGameEvent;

    /// <summary>
    /// Published when player starts preparing an operation.
    /// </summary>
    public record OperationPreparingEvent(
        string AttackType,
        long LockedAmount,
        float Duration
    ) : IGameEvent;

    /// <summary>
    /// Published when operation preparation completes (Ready state).
    /// </summary>
    public record OperationReadyEvent(
        string AttackType
    ) : IGameEvent;

    /// <summary>
    /// Published when player cancels an operation.
    /// </summary>
    public record OperationCancelledEvent(
        string AttackType,
        long RefundedAmount,
        bool IsConfiscated = false
    ) : IGameEvent;
}
