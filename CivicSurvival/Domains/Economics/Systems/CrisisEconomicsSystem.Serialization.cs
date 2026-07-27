using Colossal.Serialization.Entities;
using Game.City;
using Game.Simulation;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Serialization;
using CivicSurvival.Domains.Economics;
using Unity.Entities;

namespace CivicSurvival.Domains.Economics.Systems
{
    public partial class CrisisEconomicsSystem : IBootDefaultsReset
    {
        public void ResetToBootDefaults(ResetReason reason)
        {
            m_CrisisActive = false;
            m_CrisisStartDay = 0;
            m_LoansBlocked = false;
            m_SavedCreditworthiness = 0;
            m_HasSavedCreditworthiness = false;
            m_TourismPenaltyApplied = false;
            m_PreWarCommercePenalty = 0f;
            m_PreWarLoansBlocked = false;
            m_CachedCommerceMultiplier = 1f;
            m_CityIntegrity = 1f;
            m_PopulationState = PopulationState.Loyal;
            m_EconomyState.Invalidate();
            m_Throttle.Reset();
            CrisisEconomicsAdapter.PublishCommerceMultiplier(World, 1f);
        }

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SaveVersions.GLOBAL);
            try
            {
                var state = new CrisisEconomicsPersistState(
                    m_CrisisActive,
                    m_CrisisStartDay,
                    m_LoansBlocked,
                    m_SavedCreditworthiness,
                    m_HasSavedCreditworthiness,
                    m_TourismPenaltyApplied,
                    m_PreWarCommercePenalty,
                    m_PreWarLoansBlocked);
                CrisisEconomicsCodec.Write(state, writer);
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }

            if (Log.IsDebugEnabled) Log.Debug($"[CrisisEconomics] Serialized: CrisisActive={m_CrisisActive}, StartDay={m_CrisisStartDay}, LoansBlocked={m_LoansBlocked}, SavedCredit={m_SavedCreditworthiness}, HasSavedCredit={m_HasSavedCreditworthiness}, TourismPenalty={m_TourismPenaltyApplied}, PreWarCommerce={m_PreWarCommercePenalty}, PreWarLoans={m_PreWarLoansBlocked}");
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            EnsureCrossDomainSingletons();

            if (!SerializationGuard.TryBeginBlock(reader, SaveVersions.GLOBAL, out var version, out var block, nameof(CrisisEconomicsSystem)))
            {
                ResetToBootDefaults(ResetReason.VersionMismatch);
                return;
            }
            try
            {
                CrisisEconomicsCodec.Read(reader, out var state);
                m_CrisisActive = state.CrisisActive;
                m_CrisisStartDay = state.CrisisStartDay;
                m_LoansBlocked = state.LoansBlocked;
                m_SavedCreditworthiness = state.SavedCreditworthiness;
                m_HasSavedCreditworthiness = state.HasSavedCreditworthiness;
                m_TourismPenaltyApplied = state.TourismPenaltyApplied;
                m_PreWarCommercePenalty = state.PreWarCommercePenalty;
                m_PreWarLoansBlocked = state.PreWarLoansBlocked;

                // Force throttle to fire on first frame so commerce registry updates immediately
                m_Throttle.ForceNextFire();

                m_CogIntegrityBufferLookup.Update(this);
                UpdateCityIntegrity();
                m_PopulationState = GetPopulationState(m_CityIntegrity);
                UpdateCommerceRegistry();

                // Update UI bindings to reflect loaded state
                UpdateBindings();

                if (Log.IsDebugEnabled) Log.Debug($"[CrisisEconomics] Deserialized v{version}: CrisisActive={m_CrisisActive}, StartDay={m_CrisisStartDay}, LoansBlocked={m_LoansBlocked}, SavedCredit={m_SavedCreditworthiness}, HasSavedCredit={m_HasSavedCreditworthiness}, TourismPenalty={m_TourismPenaltyApplied}, PreWarCommerce={m_PreWarCommercePenalty}, PreWarLoans={m_PreWarLoansBlocked}");
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

        public void SetDefaults(Context context) => ResetState();
        void IResettable.ResetState() => ResetState();

        private void ResetState()
        {
            EnsureCrossDomainSingletons();

            m_CrisisActive = false;
            m_CrisisStartDay = 0;
            m_LoansBlocked = false;
            m_SavedCreditworthiness = 0;
            m_HasSavedCreditworthiness = false;
            m_TourismPenaltyApplied = false;
            m_PreWarCommercePenalty = 0f;
            m_PreWarLoansBlocked = false;
            m_CachedCommerceMultiplier = 1f;
            m_CityIntegrity = 1f;
            m_PopulationState = PopulationState.Loyal;
            m_EconomyState.Invalidate();
            m_Throttle.Reset();
            CrisisEconomicsAdapter.PublishCommerceMultiplier(World, 1f);
            UpdateBindings();
            Log.Debug("[CrisisEconomics] Set defaults (crisis inactive)");
        }

        public void DebugResetEconomy(string source)
        {
            ResetState();
            if (Log.IsDebugEnabled) Log.Debug($"[DEBUG] {source}: economy reset");
        }

        /// <summary>
        /// FIX S7-06: Detect and fix stranded vanilla Creditworthiness after version-mismatch reset.
        /// Version mismatch: SetDefaults sets m_LoansBlocked=false, m_SavedCreditworthiness=0,
        /// but vanilla Creditworthiness stays 0 (set by mod during crisis). Permanent loan lockout.
        /// </summary>
        public void ValidateAfterLoad()
        {
            OnLoadRestore(EntityManager);

            // Inv 2: EconomySingleton is written only by the throttled
            // WriteEconomyStateJob in OnUpdateImpl, so until the first post-load
            // throttled tick consumers (BackupPower charge-rate) read the Default
            // (State=Loyal, CityIntegrity=1.0). Publish the reconciled value
            // synchronously here — PLVS Phase 2, before any frame-1 consumer — so
            // there is no Default window. Done before the creditworthiness early
            // returns below since it is independent of city/Creditworthiness.
            var city = m_CitySystem.City;
            if (city == Unity.Entities.Entity.Null || !EntityManager.Exists(city))
                return;

            if (!EntityManager.HasComponent<Creditworthiness>(city))
                return;

            var credit = EntityManager.GetComponentData<Creditworthiness>(city);

            // FIX CIVIC223: Re-apply loan block after load — vanilla Creditworthiness may have
            // been recalculated to non-zero during load, but m_LoansBlocked says it should be 0.
            if ((m_LoansBlocked || m_PreWarLoansBlocked) && credit.m_Amount != 0)
            {
                if (!m_HasSavedCreditworthiness)
                {
                    m_SavedCreditworthiness = System.Math.Max(1, credit.m_Amount);
                    m_HasSavedCreditworthiness = true;
                    Log.Warn($"[CrisisEconomics] Creditworthiness baseline fallback — version mismatch (baseline={m_SavedCreditworthiness})");
                }
                credit.m_Amount = 0;
                EntityManager.SetComponentData(city, credit);
                Log.Info($"[CrisisEconomics] Post-load: re-blocked loans (saved {m_SavedCreditworthiness:N0})");
                return;
            }

            // FIX S7-06: Detect stranded vanilla Creditworthiness after version-mismatch reset.
            if (!m_LoansBlocked && !m_CrisisActive && !m_PreWarLoansBlocked && credit.m_Amount == 0)
            {
                credit.m_Amount = 1;
                EntityManager.SetComponentData(city, credit);
                m_SavedCreditworthiness = 0;
                m_HasSavedCreditworthiness = false;
                Log.Warn("[CrisisEconomics] FIX S7-06: Creditworthiness was 0 with no active loan block — restored to 1 for vanilla recalculation");
            }

            // Commerce registry updated on first throttle tick after load (ForceNextFire in Deserialize).
        }

        public void OnLoadRestore(EntityManager entityManager)
        {
            // Merge policy: PreferRestoredPayload for CommerceRateRegistry's runtime
            // factors and PreferNonDefault for EconomySingleton's derived state.
            // PLVS calls this before validators and throttled rebasing, so backup
            // power consumers never see EconomySingleton.Default after load.
            EnsureSingleton(ref m_EconomyState, entityManager, EconomySingleton.Default);
            CommerceRateRegistry.EnsureExists(entityManager);

            m_CogIntegrityBufferLookup.Update(this);
            UpdateCityIntegrity();
            m_PopulationState = GetPopulationState(m_CityIntegrity);
            UpdateCommerceRegistry();

            if (m_EconomyStateQuery.TryGetSingletonEntity<EconomySingleton>(out var economyEntity))
            {
                entityManager.SetComponentData(economyEntity, new EconomySingleton
                {
                    State = m_PopulationState,
                    CityIntegrity = m_CityIntegrity
                });
            }
        }

        private void EnsureCrossDomainSingletons()
        {
            EconomySingleton.EnsureExists(EntityManager);
            CommerceRateRegistry.EnsureExists(EntityManager);
        }

        private void RestoreCrossDomainSingletons()
        {
            EnsureSingleton(ref m_EconomyState, EconomySingleton.Default);
            CommerceRateRegistry.EnsureExists(EntityManager);
        }
    }
}
