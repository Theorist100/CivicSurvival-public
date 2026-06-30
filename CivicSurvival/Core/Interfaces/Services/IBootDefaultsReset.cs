namespace CivicSurvival.Core.Interfaces.Services
{
    /// <summary>
    /// Reason a subsystem is being reset to the state it would have had if no
    /// save block existed for it.
    /// </summary>
    public enum ResetReason : byte
    {
        /// <summary>
        /// No reset reason was supplied.
        /// </summary>
        None = 0,

        /// <summary>
        /// The deserialize block raised while fields may already be partially
        /// mutated and engine load state may be incomplete.
        /// </summary>
        DeserializeFailed = 1,

        /// <summary>
        /// SerializationGuard rejected the block header before payload fields
        /// were read.
        /// </summary>
        VersionMismatch = 2,
    }

    /// <summary>
    /// Recovery contract for corrupted or rejected save blocks.
    /// Implementations must not assume update-time engine state is available:
    /// avoid GameTimeSystem.Instance, required service resolves, and structural
    /// EntityManager work that depends on sibling singletons already existing.
    /// </summary>
    public interface IBootDefaultsReset
    {
        void ResetToBootDefaults(ResetReason reason);
    }
}
