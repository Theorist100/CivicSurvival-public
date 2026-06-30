namespace CivicSurvival.Core.Types
{
    /// <summary>
    /// Backup power discharge policy - determines how batteries are used during blackouts.
    ///
    /// Triangle of Survival tradeoff:
    /// - Reserve: Suffer now, but insurance for Grid Collapse
    /// - FullDischarge: Comfort now, but Game Over if Grid Collapse
    ///
    /// See: BACKUP_POLICY_PLAN.md, TRIANGLE_OF_SURVIVAL.md
    /// </summary>
    public enum BackupPolicy : byte
    {
        /// <summary>
        /// Batteries are NOT used during blackouts.
        /// Preserved for Cold Start after Grid Collapse.
        /// Mental decay: 100% (full rate).
        /// </summary>
        Reserve = 0,

        /// <summary>
        /// Batteries power critical services only (Hospitals, Water, Schools).
        /// Mental decay: 50% (reduced).
        /// Batteries discharge slowly during blackouts.
        /// </summary>
        CriticalOnly = 1,

        /// <summary>
        /// Batteries power everything they can reach.
        /// Mental decay: 25% (minimal).
        /// WARNING: Cold Start capacity → 0 after prolonged blackout.
        /// </summary>
        FullDischarge = 2
    }
}
