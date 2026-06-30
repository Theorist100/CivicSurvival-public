namespace CivicSurvival.Core.Types
{
    /// <summary>
    /// Defense policy — "The Hard Choice" mechanic.
    /// Player must choose what AA prioritizes.
    /// </summary>
    public enum DefensePolicy : byte
    {
        /// <summary>
        /// Policy owner unavailable. Used only by null-object readers so cross-domain
        /// consumers can fail closed instead of silently choosing a player policy.
        /// </summary>
        Unavailable = 0,

        /// <summary>
        /// "Humanitarian Shield" — protect people first.
        /// Priority: Critical > Energy > Service > Civilian
        /// Effect: High reputation, citizens love the mayor.
        /// Risk: Higher chance of blackouts.
        /// </summary>
        HumanitarianShield = 1,

        /// <summary>
        /// "Grid Integrity" — protect power generation first.
        /// Priority: Energy > Critical > Service > Civilian
        /// Effect: Stable grid, fewer blackouts.
        /// Risk: If hospital hit while AA defended power plant,
        /// Marianna (journalist) will destroy your reputation:
        /// "Mayor protects his transformers while patients burn!"
        /// </summary>
        GridIntegrity = 2
    }
}
