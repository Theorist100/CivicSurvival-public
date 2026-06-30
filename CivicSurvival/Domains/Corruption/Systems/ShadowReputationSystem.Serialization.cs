using Colossal.Serialization.Entities;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Serialization;
using CivicSurvival.Core.Interfaces.Services;

namespace CivicSurvival.Domains.Corruption.Systems
{
    /// <summary>
    /// ShadowReputationSystem - Save/Load serialization.
    /// Owns its own serialization instead of being serialized by MaintenanceContractSystem.
    /// Uses SerializationGuard for unified versioning.
    /// </summary>
    public partial class ShadowReputationSystem : IBootDefaultsReset
    {
        public void ResetToBootDefaults(ResetReason reason)
        {
            m_TrustLevel = BalanceConfig.Current.ShadowReputation.InitialTrust;
            m_FrozenUntilDay = 0f;
            m_TotalOffersAccepted = 0;
            m_TotalOffersRejected = 0;
            m_TotalSchemesSuccessful = 0;
            m_TotalTimesCaught = 0;
            m_LastPassiveRecoveryDay = 0;
            m_WalletFreezeDesiredInitialized = false;
            m_LastWalletFreezeDesired = false;
        }

        public void SetDefaults(Context context) => ResetState();

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SaveVersions.GLOBAL);
            try
            {
                var state = new ShadowReputationPersistState(
                    m_TrustLevel,
                    m_FrozenUntilDay,
                    m_TotalOffersAccepted,
                    m_TotalOffersRejected,
                    m_TotalSchemesSuccessful,
                    m_TotalTimesCaught,
                    m_LastPassiveRecoveryDay);
                ShadowReputationCodec.Write(state, writer);
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
            SerializationGuard.LogSerialized(nameof(ShadowReputationSystem), SaveVersions.GLOBAL);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SaveVersions.GLOBAL, out var version, out var block, nameof(ShadowReputationSystem)))
            {
                ResetToBootDefaults(ResetReason.VersionMismatch);
                return;
            }
            try
            {
                ShadowReputationCodec.Read(reader, out var state);
                m_TrustLevel = state.TrustLevel;
                m_FrozenUntilDay = state.FrozenUntilDay;
                m_TotalOffersAccepted = state.TotalOffersAccepted;
                m_TotalOffersRejected = state.TotalOffersRejected;
                m_TotalSchemesSuccessful = state.TotalSchemesSuccessful;
                m_TotalTimesCaught = state.TotalTimesCaught;
                m_LastPassiveRecoveryDay = state.LastPassiveRecoveryDay;

                // Post-load singleton seed is done by ValidateAfterLoad (IPostLoadValidation),
                // not here: GameTimeSystem.CurrentDay is 0 during deserialization, so calling
                // UpdateReputationSingleton() now would publish an incorrect IsFrozenOut. The
                // throttled OnThrottledUpdate then owns the ongoing day-boundary refresh.

                Log.Info($"Deserialized v{version}: Trust={m_TrustLevel:F1}, FrozenUntil={m_FrozenUntilDay:F0}");
            }
            catch (System.Exception ex)
            {
                Log.Error($"Deserialize failed: {ex}");
                ResetToBootDefaults(ResetReason.DeserializeFailed);
            }
            finally
            {
                SerializationGuard.EndBlock(reader, block);
            }
        }
    }
}
