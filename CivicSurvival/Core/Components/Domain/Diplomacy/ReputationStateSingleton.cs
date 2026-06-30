using CivicSurvival.Core.Components.Lifecycle;
using Unity.Entities;

namespace CivicSurvival.Core.Components.Domain.Diplomacy
{
    /// <summary>
    /// Singleton: UI-facing shadow reputation data.
    /// Written by: ShadowReputationSystem
    /// Read by: ShadowReputationUIPanel
    ///
    /// Note: Action methods (ModifyTrust, OnOfferAccepted, etc.) stay in system.
    /// This singleton exposes only UI-relevant read fields.
    /// </summary>
    public struct ReputationStateSingleton : IComponentData
    {
        private const float DEFAULT_TRUST_LEVEL = 50f;

        /// <summary>Current trust level (0-100).</summary>
        public float TrustLevel;

        /// <summary>Trust tier (0=Frozen, 1=Untrusted, 2=Neutral, 3=Trusted, 4=InnerCircle).</summary>
        public ReputationTier Tier;

        /// <summary>Whether player is currently frozen out.</summary>
        public bool IsFrozenOut;

        /// <summary>Frequency multiplier for offer generation.</summary>
        public float FrequencyMultiplier;

        public static ReputationStateSingleton Default => new()
        {
            TrustLevel = DEFAULT_TRUST_LEVEL,
            Tier = ReputationTier.Neutral,
            IsFrozenOut = false,
            FrequencyMultiplier = 1f
        };

        public static void EnsureExists(EntityManager em)
        {
            CivicSingleton.Ensure(em, Default);
        }
    }

    /// <summary>
    /// Reputation tier enum for UI display.
    /// </summary>
    public enum ReputationTier : byte
    {
        Frozen = 0,
        Untrusted = 1,
        Neutral = 2,
        Trusted = 3,
        InnerCircle = 4
    }
}
