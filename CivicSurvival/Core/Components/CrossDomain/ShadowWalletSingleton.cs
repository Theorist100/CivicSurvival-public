using CivicSurvival.Core.Components.Lifecycle;
using Unity.Entities;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Components.CrossDomain
{
    /// <summary>
    /// Global Shadow Money wallet state as ECS singleton.
    /// Centralized storage for all corruption funds.
    ///
    /// Access: SystemAPI.GetSingleton&lt;ShadowWalletSingleton&gt;()
    /// Modify: SystemAPI.GetSingletonRW&lt;ShadowWalletSingleton&gt;()
    ///
    /// Writer: ShadowWalletSystem (ShadowEconomy domain)
    /// Readers: ShadowExportSystem, Corruption systems, CountermeasuresSystem, UI
    ///
    /// Note: Locked operations mapping (operationId → amount) is stored in
    /// ShadowWalletSystem managed memory, not in this component.
    /// </summary>
    public struct ShadowWalletSingleton : IComponentData
    {
        /// <summary>Available balance (not locked in operations).</summary>
        public long Balance;

        /// <summary>Total balance locked in prepared operations.</summary>
        public long LockedBalance;

        /// <summary>
        /// FIX T3-1: Derived from FreezeReason flags.
        /// True when ANY freeze source is active.
        /// </summary>
        public readonly bool IsFrozen => FreezeReason != FreezeReason.None;

        /// <summary>
        /// Active freeze sources (flags enum).
        /// Multiple sources can be active simultaneously.
        /// Wallet unfreezes only when ALL sources are cleared.
        /// </summary>
        public FreezeReason FreezeReason;

        /// <summary>Lifetime income (for statistics).</summary>
        public long TotalIncome;

        /// <summary>Lifetime expenses (for statistics).</summary>
        public long TotalExpenses;

        /// <summary>
        /// Black market price markup from international sanctions (0 = none, 1.5 = +150%).
        /// Callers multiply their cost by (1 + SanctionsMarkup) before calling TryDeduct.
        /// NOT applied inside wallet — callers own the pricing logic.
        /// </summary>
        public float SanctionsMarkup;

        public void SetDefaults() => this = Default;

        // Helper methods
        public readonly long GetTotalBalance() => Balance + LockedBalance;

        public static ShadowWalletSingleton Default => new();

        public static void EnsureExists(EntityManager em)
        {
            CivicSingleton.Ensure(em, Default);
        }
    }
}


