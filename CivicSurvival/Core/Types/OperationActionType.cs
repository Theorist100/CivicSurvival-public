namespace CivicSurvival.Core.Types
{
    /// <summary>
    /// Types of GridWarfare operation actions.
    /// </summary>
    public enum OperationActionType : byte
    {
        /// <summary>Prepare operation (reserve resources).</summary>
        Prepare = 0,

        /// <summary>Execute prepared operation.</summary>
        Execute,

        /// <summary>Cancel prepared operation (refund resources).</summary>
        Cancel
    }
}
