using System;
using Colossal.Serialization.Entities;
using Unity.Entities;
using CivicSurvival.Core.Events;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Serialization;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Components.Domain.Power;
using CivicSurvival.Domains.Diplomacy.Data;

using CivicSurvival.Core.Interfaces.Services;

namespace CivicSurvival.Domains.Diplomacy.Systems
{
    /// <summary>
    /// DonorConferenceSystem - Save/Load serialization.
    /// Uses SerializationGuard for unified versioning.
    ///
    /// Note: Scandal state is now managed by ScandalSystem.
    /// </summary>
    public partial class DonorConferenceSystem : IBootDefaultsReset
    {
        public void ResetToBootDefaults(ResetReason reason)
        {
            ResetBootDefaultsFields();
            Log.Info($"[BOOT-RESET] system={nameof(DonorConferenceSystem)} reason={reason}");
        }

        public void SetDefaults(Context context) => ResetState();

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SaveVersions.GLOBAL);
            try
            {
                var state = new DonorConferencePersistState(
                    m_State.UsesRemaining,
                    m_State.CooldownDaysRemaining,
                    m_State.ActiveGenerators,
                    m_State.SanctionsActive,
                    m_State.SanctionDaysRemaining,
                    m_State.TradePenalty,
                    m_State.GeneratorMW,
                    m_GameDay,
                    m_GeneratorDecayCounter,
                    m_ImportTrustPenalty,
                    m_HasLastReplenishedAct,
                    m_LastReplenishedAct,
                    sawLastReplenishedAct: true,
                    sawUsesRemaining: true,
                    sawGeneratorMW: true);
                DonorConferenceCodec.Write(state, writer);
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
            SerializationGuard.LogSerialized(nameof(DonorConferenceSystem), SaveVersions.GLOBAL);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SaveVersions.GLOBAL, out _, out var block, nameof(DonorConferenceSystem)))
            {
                ResetToBootDefaults(ResetReason.VersionMismatch);
                return;
            }
            try
            {
                var diplomacyConfig = BalanceConfig.Current.Diplomacy;
                var defaultState = DonorConferenceStateData.CreateDefault(MAX_USES);
                m_DonorSanctions.Invalidate();

#pragma warning disable CIVIC144 // Donor codec returns a bounded scalar snapshot, not a collection size
                DonorConferenceCodec.Read(
                    reader,
                    defaultState.UsesRemaining,
                    defaultState.GeneratorMW,
                    MAX_USES,
                    MAX_GENERATORS,
                    out var snapshot);
#pragma warning restore CIVIC144

                m_State = new DonorConferenceStateData
                {
                    UsesRemaining = snapshot.UsesRemaining,
                    CooldownDaysRemaining = snapshot.CooldownDaysRemaining,
                    ActiveGenerators = snapshot.ActiveGenerators,
                    SanctionsActive = snapshot.SanctionsActive,
                    SanctionDaysRemaining = snapshot.SanctionDaysRemaining,
                    TradePenalty = snapshot.TradePenalty,
                    GeneratorMW = snapshot.GeneratorMW
                };
                m_GameDay = snapshot.GameDay;
                m_GeneratorDecayCounter = snapshot.GeneratorDecayCounter;
                m_ImportTrustPenalty = snapshot.ImportTrustPenalty;
                if (snapshot.SawLastReplenishedAct)
                {
                    m_HasLastReplenishedAct = snapshot.HasLastReplenishedAct;
                    m_LastReplenishedAct = snapshot.LastReplenishedAct;
                }
                else
                {
                    m_HasLastReplenishedAct = false;
                    m_LastReplenishedAct = default;
                }
                m_ConferenceDialogActive = false;
                ResetActBaseline();

                if (!snapshot.SawUsesRemaining)
                    Log.Warn($"Deserialize missing usesRemaining; restored default {m_State.UsesRemaining}");

                if ((!snapshot.SawGeneratorMW || m_State.GeneratorMW <= 0) && m_State.ActiveGenerators > 0)
                {
                    m_State.GeneratorMW = diplomacyConfig.GeneratorMw;
                    Log.Warn($"Deserialize missing generator MW snapshot for active generators; using current config {m_State.GeneratorMW}MW");
                }

                bool hadZombieSanctions = m_State.SanctionsActive && m_State.SanctionDaysRemaining <= 0f;
                m_State.ClampInvariants(MAX_USES, MAX_GENERATORS, DonorConferenceCodec.MaxDonorDays, DonorConferenceCodec.MaxTradePenalty);
                if (hadZombieSanctions)
                    Log.Warn("Cleared zombie sanctions (active flag without positive days)");

                int normalizedDecayInterval = Math.Max(1, diplomacyConfig.GeneratorDecayIntervalDays);
                m_GeneratorDecayCounter = m_State.ActiveGenerators > 0
                    ? m_GeneratorDecayCounter % normalizedDecayInterval
                    : 0;

                // F-6 (SYS-08): no structural singleton work during Deserialize.
                // The ExternalPowerSource entity and DonorSanctions singleton are
                // (re)created post-barrier in OnLoadRestore (PLVS Phase 2) from this
                // same restored m_State — doing it here writes singletons before the
                // load barrier and is torn down/re-created anyway.
                Log.Info($"Deserialized: Uses={m_State.UsesRemaining}, Generators={m_State.ActiveGenerators}");
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

        public void OnLoadRestore(EntityManager entityManager)
        {
            m_DonorSanctions.Invalidate();
            RestoreDonorSanctionsEntity(entityManager);
            WriteSanctionsSingleton(m_State.SanctionsActive, m_State.TradePenalty);

            RestoreExternalPowerSourceEntity(entityManager);
            if (m_State.ActiveGenerators > 0)
                UpdateExternalPowerSource();
            else
                ClearExternalPowerSource();
            SeedActBaselineFromSingleton(force: true);
            SeedReplenishLatchFromCurrentActIfMissing();
        }

        private void SeedReplenishLatchFromCurrentActIfMissing()
        {
            if (m_HasLastReplenishedAct)
                return;

            if (!TryReadCurrentAct(out var current))
                return;

            m_LastReplenishedAct = current;
            m_HasLastReplenishedAct = true;
            Log.Info($"Seeded donor replenish latch from loaded act {current}");
        }
    }
}
