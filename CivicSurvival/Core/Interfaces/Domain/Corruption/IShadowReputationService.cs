using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Interfaces.Domain.Corruption
{
    /// <summary>
    /// Interface for shadow reputation tracking.
    /// Allows contract/countermeasures systems to update reputation without direct dependency on Corruption domain.
    ///
    /// Consumers: ContractResponseSystem (Accept/Reject), CountermeasuresUpdateSystem (Caught/Successful)
    /// Null-object semantics (when Corruption closed): trust=0, no freeze, no inner circle,
    /// multiplier=1f (neutral — offers fire at base rate); all event handlers are no-ops.
    /// </summary>
    [GenerateNullObject]
    [OwnedByFeatureId(FeatureIds.CorruptionName)]
    public interface IShadowReputationService
    {
        /// <summary>Current trust value in the [0, 100] range.</summary>
        float CurrentTrustLevel { get; }

        /// <summary>True while the player is below the trust threshold or inside the freeze timer.</summary>
        bool IsFrozenOut { get; }

        /// <summary>True when exclusive high-trust offers should be available.</summary>
        bool IsInnerCircle { get; }

        /// <summary>Offer frequency multiplier derived from current trust tier.</summary>
        [NullReturn(1f)]
        float GetFrequencyMultiplier();

        /// <summary>Apply a direct trust delta with a diagnostic reason.</summary>
        void ModifyTrust(float delta, string reason);

        /// <summary>
        /// Called when player accepts a shady offer.
        /// Increases trust with criminal network.
        /// </summary>
        void OnOfferAccepted();

        /// <summary>
        /// Called when player rejects an offer or accepts the official contractor.
        /// Decreases trust with criminal network.
        /// </summary>
        void OnOfferRejected();

        /// <summary>
        /// Called when player is arrested (investigation/police resolved with charges).
        /// Large trust penalty — criminal network avoids unreliable partners.
        /// Design: CORRUPTION_ECONOMY.md:264 — "Get caught = -20 trust"
        /// </summary>
        void OnCaught();

        /// <summary>
        /// Called when player escapes investigation/police without arrest.
        /// Trust bonus — criminal network values partners who handle heat.
        /// Design: CORRUPTION_ECONOMY.md:263 — "Successful scheme = +5 trust"
        /// </summary>
        void OnSchemeSuccessful();
    }
}
