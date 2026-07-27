using System;

namespace CivicSurvival.Core.Types
{
    /// <summary>
    /// Reasons why shadow assets can be frozen.
    /// Flags enum: multiple freeze sources can be active simultaneously.
    /// Wallet unfreezes only when ALL sources are cleared.
    ///
    /// FIX T3-1: Changed from single-value to [Flags] to prevent
    /// second freeze source being silently dropped when wallet is already frozen.
    /// </summary>
    [Flags]
    public enum FreezeReason : byte
    {
        /// <summary>Assets not frozen.</summary>
        None = 0,

        /// <summary>Police investigation in progress.</summary>
        PoliceInvestigation = 1,

        /// <summary>Trust level with shadow network too low.</summary>
        LowTrustLevel = 2,

        /// <summary>Temporary freeze as punishment for failed operation.</summary>
        TemporaryPunishment = 4,

        // NOTE: Value 8 is intentionally skipped (bit 3 gap). Next power-of-2 after 4 is 8.
        // If adding a new reason, use 8 first and update AllFlags accordingly.

        /// <summary>Assets confiscated after arrest.</summary>
        Confiscated = 16,

        /// <summary>All valid flags combined (for deserialization bitmask stripping).</summary>
        AllFlags = PoliceInvestigation | LowTrustLevel | TemporaryPunishment | Confiscated,
    }
}
