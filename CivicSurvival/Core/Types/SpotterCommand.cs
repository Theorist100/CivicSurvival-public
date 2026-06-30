using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Types
{
    /// <summary>
    /// Command types for spotter pipeline (ingress → aggregate).
    /// </summary>
    public enum SpotterCommandType : byte
    {
        None = 0,
        PerformSBU,
        RequestEvacuation,
        EnableCounterOSINT,
        DisableCounterOSINT,
        RollbackSBU,
        RollbackEvacuation,
        FinalizeEvacuation,
        FinalizeSBU,
    }

    /// <summary>
    /// Single command struct for spotter pipeline mailbox (NativeQueue).
    /// Unused fields for a given command type are 0/default (ignored).
    /// </summary>
    public struct SpotterCommand
    {
        public SpotterCommandType Type;

        /// <summary>Entity.Index for SBU/Evac target commands.</summary>
        public int TargetIndex;

        /// <summary>Entity.Version (SBU/Evac target).</summary>
        public int TargetVersion;

        /// <summary>Pre-validated cost (for SBU logging).</summary>
        public int Cost;

        /// <summary>Narrative trigger to publish on drain (aggregate context).</summary>
        public NarrativeTrigger NarrativeHint;

        /// <summary>
        /// Explicit flag — NarrativeTrigger(0) = SatireBlackout, not None.
        /// Without this flag, default(NarrativeTrigger) would be silently swallowed.
        /// </summary>
        public bool HasNarrativeHint;
    }
}
