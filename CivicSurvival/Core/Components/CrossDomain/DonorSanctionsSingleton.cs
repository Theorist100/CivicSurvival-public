using CivicSurvival.Core.Components.Lifecycle;
using Unity.Entities;

namespace CivicSurvival.Core.Components.CrossDomain
{
    /// <summary>
    /// A2 FIX 2c: Sanctions state as ECS singleton.
    /// Replaces DonorEvent(SanctionsApplied/Expired) for L1↔L1 state sync.
    ///
    /// Access: SystemAPI.GetSingleton&lt;DonorSanctionsSingleton&gt;()
    ///
    /// Writer: DonorConferenceSystem (Diplomacy domain)
    /// Readers: CrisisEconomicsSystem, CountermeasuresUpdateSystem, CountermeasuresUISystem,
    ///          ShadowWalletSystem (4 consumers total)
    ///
    /// Note: ShadowWalletSingleton.SanctionsMarkup is written by ShadowWalletSystem
    /// (reads SanctionsActive from this singleton), not directly by DonorConferenceSystem.
    /// </summary>
    public struct DonorSanctionsSingleton : IComponentData
    {
        /// <summary>Whether international sanctions are currently active.</summary>
        public bool SanctionsActive;

        /// <summary>Trade penalty factor (0-1). Applied to commerce multiplier.</summary>
        public float TradePenalty;

        public static DonorSanctionsSingleton Default => new();

        public static void EnsureExists(EntityManager em)
        {
            CivicSingleton.Ensure(em, Default);
        }
    }
}
