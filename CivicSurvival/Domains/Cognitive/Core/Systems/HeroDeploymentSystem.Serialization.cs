using Colossal.Serialization.Entities;
using Unity.Entities;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Serialization;
using CivicSurvival.Core.Types;

using CivicSurvival.Core.Interfaces.Services;

namespace CivicSurvival.Domains.Cognitive.Core.Systems
{
    /// <summary>
    /// Serialization partial for HeroDeploymentSystem.
    ///
    /// Only HeroStatus is persisted — HeroDeployCost / HeroInfectionReduction /
    /// HeroRecoveryBonus are config values rebuilt from BalanceConfig on load
    /// (same policy as CognitiveStateSystem for its config-like fields), so
    /// balance patches take effect on existing saves without a version bump.
    /// </summary>
    public partial class HeroDeploymentSystem : IDefaultSerializable, IBootDefaultsReset
    {
        [System.NonSerialized] private bool m_RestoreBootDefaultsAfterLoad;
        [System.NonSerialized] private HeroDeploymentPersistState? m_PendingPersistState;

        public void ResetToBootDefaults(ResetReason reason)
        {
            m_PendingPersistState = null;
            // Field-only: clear the penalty-system retry throttle so a stale ElapsedTime deadline from
            // the previous city can't block re-resolution after a boot-default recovery (the structural
            // HeroDeploymentState reset is deferred to OnLoadRestore via the latch below).
            m_NextPenaltySystemRetryTime = 0d;
            m_RestoreBootDefaultsAfterLoad = true;
            Log.Info($"[BOOT-RESET] HeroDeploymentSystem reason={reason} HeroStatus=Inactive");
        }

        public void SetDefaults(Context context)
        {
            ResetState();
        }

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SaveVersions.GLOBAL);
            try
            {
                HeroDeploymentState state;
                if (m_HeroStateQuery.TryGetSingletonEntity<HeroDeploymentState>(out var stateEntity))
                    state = EntityManager.GetComponentData<HeroDeploymentState>(stateEntity);
                else
                    state = HeroDeploymentState.Default;

                var persistState = new HeroDeploymentPersistState(
                    (byte)state.HeroStatus,
                    (byte)state.Archetype,
                    state.ArestovychTrustDebt,
                    state.DebtCollapseEndHour,
                    state.DebtCooldownEndHour,
                    state.ArchetypeSwitchCooldownEndHour);
                HeroDeploymentCodec.Write(persistState, writer);
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
            SerializationGuard.LogSerialized(nameof(HeroDeploymentSystem), SaveVersions.GLOBAL);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SaveVersions.GLOBAL, out _, out var block, nameof(HeroDeploymentSystem)))
            {
                ResetToBootDefaults(ResetReason.VersionMismatch);
                return;
            }
            try
            {
                HeroDeploymentCodec.Read(reader, out var persistState);
                m_RestoreBootDefaultsAfterLoad = false;
                m_PendingPersistState = persistState;
                // Intentional discard: if the singleton entity isn't ready yet the pending state is
                // retained and reapplied from OnLoadRestore (which checks the result).
                _ = TryApplyPendingPersistState(EntityManager, "Deserialize");

                Log.Info($"Deserialized: HeroStatus={persistState.HeroStatus} Archetype={persistState.Archetype} TrustDebt={persistState.TrustDebt:F2} (config rebuilt from BalanceConfig)");
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
            HeroDeploymentState.EnsureExists(entityManager);
            if (m_RestoreBootDefaultsAfterLoad)
            {
                // Best-effort prime of m_PenaltySystem before reset; ResetState is null-safe
                // if the penalty system is not resolvable yet on this load boundary.
                _ = TryResolvePenaltySystem(force: true);
                ResetState();
                m_RestoreBootDefaultsAfterLoad = false;
                return;
            }

            _ = TryApplyPendingPersistState(entityManager, "OnLoadRestore");
        }

        private bool TryApplyPendingPersistState(EntityManager entityManager, string source)
        {
            if (!m_PendingPersistState.HasValue)
                return false;

            HeroDeploymentState.EnsureExists(entityManager);
            if (!m_HeroStateQuery.TryGetSingletonEntity<HeroDeploymentState>(out var stateEntity)
                || !entityManager.Exists(stateEntity))
            {
                Log.Warn($"HeroDeploymentState singleton missing during {source}; decoded payload retained for owner restore");
                return false;
            }

            var persistState = m_PendingPersistState.Value;

            // Rebuild config-like fields from BalanceConfig
            var cfgBase = HeroDeploymentState.Default;
            var heroStatus = persistState.HeroStatus switch
            {
                1 => HeroStatus.Deployed,
                2 => HeroStatus.Lecturing,
                _ => HeroStatus.Inactive
            };
            var archetype = persistState.Archetype switch
            {
                1 => HeroArchetype.Arestovych,
                2 => HeroArchetype.Patriot,
                _ => HeroArchetype.Voice
            };
            entityManager.SetComponentData(stateEntity, new HeroDeploymentState
            {
                HeroStatus = heroStatus,
                HeroDeployCost = cfgBase.HeroDeployCost,
                HeroInfectionReduction = cfgBase.HeroInfectionReduction,
                HeroRecoveryBonus = cfgBase.HeroRecoveryBonus,
                // Gameplay-state from the save. Debt timers are absolute game-hours
                // (TotalGameHours, continuous across load) — restored verbatim, expired
                // naturally against the live clock by the consumer (no re-anchor needed).
                Archetype = archetype,
                ArestovychTrustDebt = persistState.TrustDebt,
                DebtCollapseEndHour = persistState.DebtEndHour,
                DebtCooldownEndHour = persistState.DebtCooldownEndHour,
                ArchetypeSwitchCooldownEndHour = persistState.SwitchCooldownEndHour
            });

            m_PendingPersistState = null;
            return true;
        }
    }
}
