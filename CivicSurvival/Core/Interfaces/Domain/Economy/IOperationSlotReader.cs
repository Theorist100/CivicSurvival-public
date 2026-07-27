using System.Collections.Generic;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Interfaces.Domain.Economy
{
    /// <summary>
    /// Read-only view of active operation slot IDs.
    /// Used by ShadowWalletSystem.ValidateAfterLoad to detect orphaned wallet locks (S8-07).
    ///
    /// Implementor: PlayerAttackSystem (GridWarfare domain)
    /// Consumer: ShadowWalletSystem (ShadowEconomy domain) — post-load reconciliation only.
    /// When GridWarfare is closed, the null-object returns no active slots, so
    /// reconciliation safely unlocks any orphaned wallet locks as the S8-07 fallback.
    /// </summary>
    [OwnedByFeatureId(FeatureIds.GridWarfareName)]
    [GenerateNullObject]
    public interface IOperationSlotReader
    {
        [NullReturnNull]
        IVersionedView<OperationSlotsSnapshot>? SlotsView { get; }

        /// <summary>
        /// Clears and fills <paramref name="target"/> with all non-Idle slot OperationIds.
        /// Caller owns the buffer; this avoids post-load reconciliation allocations.
        /// </summary>
        void CopyActiveOperationIds(ICollection<string> target);
    }
}
