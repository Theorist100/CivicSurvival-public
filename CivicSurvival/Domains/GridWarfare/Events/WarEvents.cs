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
    /// <see cref="TargetId"/> is the mirror-city target the operation aims at (the War Room pick
    /// or the Execute-time auto-selection; <c>EnemyTarget.NoTargetId</c> = resolve automatically) —
    /// it rides the launch onto the projectile's payload so the arrival lands where the UI promised.
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
        int ExecutionId,
        ushort TargetId
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
    /// Published when a player outbound counter-strike is resolved at arrival, on ALL three
    /// outcomes: intercepted by the enemy's defence (no axis change), landed (axis lowered), or
    /// landed with <see cref="NoEffect"/> (the axis was already stripped to its invulnerable
    /// reserve — nothing to suppress). The War Room UI subscribes for the STRIKE OUTCOMES ledger
    /// and the arrival/intercept toasts (visible even with the War Room closed). <see cref="Axis"/>
    /// is the targeted category (Kinetic→Physical, Cyber→Digital, Psyops→Social); <see cref="Seed"/>
    /// is the launch-frozen strike seed (the launch↔arrival correlator);
    /// <see cref="TargetId"/> is the mirror-city target the strike actually resolved onto
    /// (<see cref="EnemyTarget.NoTargetId"/> when nothing resolved), so the ledger can name the
    /// same target the strike card showed.
    /// </summary>
    public record OutboundStrikeResolvedEvent(
        AttackCategory Axis,
        bool Intercepted,
        float OldValue,
        float NewValue,
        uint Seed,
        bool NoEffect = false,
        ushort TargetId = EnemyTarget.NoTargetId
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

    /// <summary>
    /// Published once per enemy-beachhead collapse: all three enemy axes suppressed below the
    /// objective threshold and the collapse claimed (Shadow Cash loot queued, or latch-only when
    /// the configured loot is 0). <see cref="CollapseId"/> is the monotonic collapse counter —
    /// the same number the wallet idempotency key <c>GwObjective:{id}</c> carries. Telemetry
    /// subscribes.
    /// </summary>
    public record BeachheadCollapsedEvent(
        int CollapseId,
        long LootShadowCash
    ) : IGameEvent;
}
