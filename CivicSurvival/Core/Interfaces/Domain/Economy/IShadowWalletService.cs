using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Interfaces.Domain.Economy
{
    /// <summary>
    /// Cross-domain access to the Shadow Wallet — read-only state, affordability
    /// checks, pending-deduction tracking, sync deduct / escrow / freeze
    /// operations.
    ///
    /// Implementor: <c>ShadowWalletSystem</c> (ShadowEconomy domain). All state
    /// (balance, freeze, locks, sanctions markup, pending counters) is owned by
    /// that system; this interface is the single boundary other domains cross.
    ///
    /// Write paths:
    /// - Sync (no ECB delay): <see cref="TryDeduct"/>, <see cref="DeductBypassFreeze"/>,
    ///   <see cref="TryLock"/>/<see cref="ConfirmDeduct"/>/<see cref="Unlock"/>,
    ///   <see cref="Freeze"/>/<see cref="Unfreeze"/>. Main-thread only.
    /// - Async (ECB request entities): <c>ShadowIncomeRequest</c>,
    ///   <c>ShadowWalletDeductRequest</c>, <c>ShadowWalletControlRequest</c>.
    ///
    /// Pending tracking: producers that create a <c>BudgetDeductRequest</c> with
    /// <c>Category=ShadowOps</c> (or <c>ShadowWalletDeductRequest</c>) MUST call
    /// <see cref="CanAffordWithPending"/> first and <see cref="RegisterPendingDeduction"/>
    /// immediately after queueing — otherwise concurrent producers can double-spend
    /// the same balance in the same frame.
    ///
    /// Null-object semantics (when ShadowEconomy is closed or pre-load): every
    /// property returns <c>default</c>, every bool method returns false, every
    /// void method is a no-op, every struct return is <c>default</c>
    /// (<c>Affordable=false</c>, <c>Exists=false</c>). This is intentional
    /// fail-closed behaviour — kickback flows must not emit income/spend
    /// requests when no wallet exists.
    /// </summary>
    [GenerateNullObject]
    [OwnedByFeatureId(FeatureIds.ShadowEconomyName)]
    public interface IShadowWalletService
    {
        // =====================================================================
        // READ-ONLY STATE
        // =====================================================================

        /// <summary>Current available balance (not locked).</summary>
        long Balance { get; }

        /// <summary>
        /// True if assets are frozen (investigation in progress).
        /// Income producers must also check <see cref="IsOperational"/> before creating requests.
        /// </summary>
        bool IsFrozen { get; }

        /// <summary>
        /// True if the wallet system is active (enabled for current act).
        /// False in PreWar — income requests created while false become orphans.
        /// </summary>
        bool IsOperational { get; }

        /// <summary>
        /// Current sanctions markup (0 = none, 1.5 = +150%).
        /// For affordability checks that combine markup with pending, use
        /// <see cref="CanAffordWithPending"/>.
        /// </summary>
        float SanctionsMarkup { get; }

        /// <summary>
        /// True if the wallet singleton currently exists. Distinct from
        /// <see cref="IsFrozen"/>: <c>HasWallet=false</c> is the canonical
        /// "feature unavailable" signal. Income-request creation must gate on
        /// <see cref="IsOperational"/> so inactive-act requests do not become
        /// orphaned. A frozen wallet still exists.
        /// </summary>
        [NullReturn(false)]
        bool HasWallet { get; }

        /// <summary>Atomic snapshot of balance + freeze + markup.</summary>
        WalletSnapshot GetWalletSnapshot();

        // =====================================================================
        // AFFORDABILITY
        // =====================================================================

        /// <summary>
        /// Check if wallet can afford the effective cost (base + sanctions markup).
        /// Accounts for <see cref="IsFrozen"/> and <see cref="SanctionsMarkup"/>.
        /// Does NOT account for pending deductions — use
        /// <see cref="CanAffordWithPending"/> for spend decisions.
        /// </summary>
        [NullReturn(false)]
        bool CanAfford(long baseCost);

        /// <summary>
        /// Foolproof affordability for spend producers: <c>IsFrozen</c> +
        /// <c>SanctionsMarkup</c> + same-frame pending deductions, all in one
        /// call. Returns <see cref="AffordabilityResult.Affordable"/>=true plus
        /// the post-markup cost; pass <see cref="AffordabilityResult.EffectiveCost"/>
        /// directly to <see cref="RegisterPendingDeduction"/> and to the deduct
        /// request — the base cost is no longer load-bearing once markup applies.
        /// </summary>
        AffordabilityResult CanAffordWithPending(long baseCost);

        /// <summary>
        /// Affordability check that ignores freeze. Used only for the police
        /// bribe flow — wallet is frozen during the police phase, but the bribe
        /// is the player's escape mechanism out of that freeze, so the freeze
        /// gate must be bypassable. Pending and markup still apply.
        /// </summary>
        AffordabilityResult CanAffordBypassFreeze(long baseCost);

        // =====================================================================
        // PENDING-DEDUCTION TRACKING
        // =====================================================================

        /// <summary>
        /// Pending-deduction total — sum of effective costs reserved by
        /// producers that have queued a deduct request but whose request has
        /// not yet been drained by <c>BudgetResolutionSystem</c>. Reset to 0
        /// at the end of each drain pass.
        /// </summary>
        long PendingDeductions { get; }

        /// <summary>
        /// Reserve a pending deduction. Call IMMEDIATELY after queueing a
        /// <c>BudgetDeductRequest{Category=ShadowOps}</c> or
        /// <c>ShadowWalletDeductRequest</c> via ECB — otherwise the same balance
        /// can be reserved twice in the same frame. Pass the effective
        /// (post-markup) cost returned by <see cref="CanAffordWithPending"/>.
        /// </summary>
        void RegisterPendingDeduction(long amount);

        /// <summary>
        /// Release a previously reserved pending deduction. Called by
        /// <c>BudgetResolutionSystem</c> when a deduct request is dropped or
        /// fails before draining. Idempotent — clamps at 0.
        /// </summary>
        void RollbackPendingDeduction(long amount);

        /// <summary>
        /// Zero the pending counter. Called by
        /// <c>BudgetResolutionSystem.ValidateAfterLoad</c> and by the
        /// serialization partial after deserialize — both guarantee that any
        /// reserved-but-undrained amounts from the previous session are gone.
        /// </summary>
        void ResetPendingDeductions();

        // =====================================================================
        // SYNC DEDUCT
        // =====================================================================

        /// <summary>
        /// Synchronous wallet deduct. Called by
        /// <c>BudgetResolutionSystem.ProcessDeductRequests</c> for the
        /// ShadowOps category. <paramref name="amount"/> is the effective
        /// post-markup cost (sanctions markup applied by the producer through
        /// <see cref="CanAffordWithPending"/>). Respects freeze.
        /// </summary>
        [NullReturn(false)]
        bool TryDeduct(long amount, string reason);

        /// <summary>
        /// Synchronous wallet deduct that bypasses the freeze gate. Used only
        /// by the police bribe flow: ordering dependency between
        /// <c>ProcessDeductRequests</c> and <c>ProcessControlRequests</c>
        /// would otherwise reject the bribe before the unfreeze runs.
        /// <paramref name="effectiveCost"/> is already post-markup.
        /// </summary>
        [NullReturn(false)]
        bool DeductBypassFreeze(long effectiveCost, string reason);

        /// <summary>
        /// Idempotent refund/reversal credit. Bypasses act operational and freeze gates
        /// because it returns money already deducted; not for new income producers.
        /// </summary>
        [NullReturn(false)]
        bool TryApplyRefund(long amount, string reason, string operationKey);

        // =====================================================================
        // ESCROW — sync operations (PlayerAttackSystem only)
        // =====================================================================

        /// <summary>
        /// Lock funds for a pending operation (two-phase commit pattern).
        /// Respects pending deductions.
        /// </summary>
        [NullReturn(false)]
        bool TryLock(long amount, string operationId);

        /// <summary>
        /// Confirm deduction of previously locked funds.
        /// </summary>
        void ConfirmDeduct(string operationId);

        /// <summary>
        /// Release locked funds without deducting.
        /// </summary>
        void Unlock(string operationId);

        /// <summary>
        /// Check if a specific operation lock exists in the wallet.
        /// Used by post-load reconciliation to detect orphaned slots (S3-04).
        /// </summary>
        [NullReturn(false)]
        bool HasLock(string operationId);

        // =====================================================================
        // FREEZE CONTROL — sync operations (H37: eliminates ECB 1-frame delay)
        // Only for reputation-based freeze (LowTrustLevel).
        // Countermeasures freeze MUST use ECB (ShadowWalletControlRequest) —
        // ordering dependency: Income → Deduct → Control pipeline.
        // =====================================================================

        /// <summary>Add freeze source immediately (no ECB delay).</summary>
        void Freeze(FreezeReason reason);

        /// <summary>Remove freeze source immediately (no ECB delay).</summary>
        void Unfreeze(FreezeReason reason);
    }
}
