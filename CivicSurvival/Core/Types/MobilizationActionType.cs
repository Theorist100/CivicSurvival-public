namespace CivicSurvival.Core.Types
{
    /// <summary>
    /// Types of mobilization actions.
    /// </summary>
    public enum MobilizationActionType : byte
    {
        None = 0,

        /// <summary>Activate conscription (start recruiting).</summary>
        ActivateConscription = 1,

        /// <summary>Deactivate conscription (stop recruiting).</summary>
        DeactivateConscription = 2,

        /// <summary>Emergency call to arms (recover casualties).</summary>
        CallToArms = 3
    }
}
