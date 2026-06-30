namespace CivicSurvival.Core.Types
{
    /// <summary>
    /// Status of a request component for Data-Driven Commands.
    /// Used by processing systems to track request lifecycle.
    /// </summary>
    public enum RequestStatus : byte
    {
        /// <summary>No request in flight. Default for uninitialised DTO fields.</summary>
        Idle = 0,

        /// <summary>Request is waiting to be processed.</summary>
        Pending = 1,

        /// <summary>Request was processed successfully.</summary>
        Success = 2,

        /// <summary>Request processing failed (check FailReason if available).</summary>
        Failed = 3
    }
}
