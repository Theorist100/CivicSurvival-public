namespace CivicSurvival.Core.Types
{
    /// <summary>
    /// Toast notification types for corruption events.
    /// Each type triggers specific UI and handler logic.
    /// </summary>
    public enum ToastType
    {
        /// <summary>Contractor offers procurement deal with potential kickback.</summary>
        ProcurementOffer = 0,

        /// <summary>Auditor warns about suspicious financial activity.</summary>
        AuditorWarning,

        /// <summary>Insurance company offers settlement (potentially fraudulent).</summary>
        InsuranceClaim,

        /// <summary>Safety incident report (may involve cover-up option).</summary>
        SafetyAccident
    }

    /// <summary>
    /// Toast priority levels.
    /// Higher priority toasts display immediately and stay longer.
    /// </summary>
    public enum ToastPriority
    {
        /// <summary>Background notifications, can be ignored.</summary>
        Low = 0,

        /// <summary>Standard notifications, normal display time.</summary>
        Normal = 1,

        /// <summary>Important notifications, longer display time.</summary>
        High = 2,

        /// <summary>Urgent notifications, display immediately and persist until dismissed.</summary>
        Critical = 3
    }
}
