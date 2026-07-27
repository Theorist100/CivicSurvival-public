using Unity.Entities;
using CivicSurvival.Core.Components.Domain.Diplomacy;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.UI.DomainState;
using static CivicSurvival.Core.UI.B;
using CivicSurvival.Core.Attributes;

namespace CivicSurvival.Domains.Diplomacy.UI
{
    /// <summary>
    /// UI system for Shadow Reputation (TrustLevel) display.
    /// ECS-Pure: Reads directly from ReputationStateSingleton.
    ///
    /// Migrated from ShadowReputationUIPanel to CivicUIPanelSystem.
    /// Gains: auto-disposed EntityQuery, RequireForUpdate, proper ECS lifecycle.
    /// </summary>
    [ActIndependent]
    public partial class ShadowReputationUISystem : CivicUIPanelSystem
    {
        private const float DEFAULT_TRUST_LEVEL = 50f;

        private EntityQuery m_ReputationQuery;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_ReputationQuery = GetEntityQuery(
                ComponentType.ReadOnly<ReputationStateSingleton>());

            ReputationStateSingleton.EnsureExists(EntityManager);
            RequireForUpdate(m_ReputationQuery);

            Log.Info("Created");
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            // SAVE_LOAD_LIFECYCLE_DOCTRINE Invariant 2: OnCreate doesn't re-run on new-game.
            ReputationStateSingleton.EnsureExists(EntityManager);
        }

        protected override void ConfigureBindings()
        {
            Bindings.Add<string>(ReputationState, "{}");
        }

        protected override void OnPanelUpdate()
        {
            var dto = new ReputationDto
            {
                TrustLevel = DEFAULT_TRUST_LEVEL,
                TrustTier = "Neutral",
                OfferFrequencyMult = 1f
            };

            if (m_ReputationQuery.TryGetSingleton<ReputationStateSingleton>(out var rep))
            {
                dto.TrustLevel = rep.TrustLevel;
                dto.TrustTier = TierToString(rep.Tier);
                dto.IsFrozenOut = rep.IsFrozenOut;
                dto.OfferFrequencyMult = rep.FrequencyMultiplier;
            }

            PublishWhenComplete(ReputationState, NoSourceChecks, () => dto);
        }

        private static string TierToString(ReputationTier tier)
        {
            return tier switch
            {
                ReputationTier.Frozen => "Frozen",
                ReputationTier.Untrusted => "Untrusted",
                ReputationTier.Neutral => "Neutral",
                ReputationTier.Trusted => "Trusted",
                ReputationTier.InnerCircle => "Inner Circle",
                _ => "Unknown"
            };
        }
    }
}
