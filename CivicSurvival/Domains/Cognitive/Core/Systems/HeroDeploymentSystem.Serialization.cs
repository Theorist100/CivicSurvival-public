using Colossal.Serialization.Entities;
using Unity.Entities;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Serialization;

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

        public void ResetToBootDefaults(ResetReason reason)
        {
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

                var persistState = new HeroDeploymentPersistState((byte)state.HeroStatus);
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

                // Rebuild config-like fields from BalanceConfig
                var cfgBase = HeroDeploymentState.Default;

                if (m_HeroStateQuery.TryGetSingletonEntity<HeroDeploymentState>(out var stateEntity))
                {
                    var heroStatus = persistState.HeroStatus switch
                    {
                        1 => HeroStatus.Deployed,
                        2 => HeroStatus.Lecturing,
                        _ => HeroStatus.Inactive
                    };
                    EntityManager.SetComponentData(stateEntity, new HeroDeploymentState
                    {
                        HeroStatus = heroStatus,
                        HeroDeployCost = cfgBase.HeroDeployCost,
                        HeroInfectionReduction = cfgBase.HeroInfectionReduction,
                        HeroRecoveryBonus = cfgBase.HeroRecoveryBonus
                    });
                }

                Log.Info($"Deserialized: HeroStatus={persistState.HeroStatus} (config rebuilt from BalanceConfig)");
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
            if (!m_RestoreBootDefaultsAfterLoad)
                return;

            // Best-effort prime of m_PenaltySystem before reset; ResetState is null-safe
            // if the penalty system is not resolvable yet on this load boundary.
            _ = TryResolvePenaltySystem(force: true);
            ResetState();
            m_RestoreBootDefaultsAfterLoad = false;
        }
    }
}
