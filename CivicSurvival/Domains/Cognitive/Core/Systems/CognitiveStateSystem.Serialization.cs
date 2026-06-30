using Colossal.Serialization.Entities;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Serialization;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Interfaces.Services;
using Unity.Entities;

namespace CivicSurvival.Domains.Cognitive.Core.Systems
{
    /// <summary>
    /// Serialization partial for CognitiveStateSystem.
    /// Handles save/load of system state and CognitiveState singleton.
    ///
    /// FIX W4-L1: Config-like fields (rates, thresholds, multipliers) are NO LONGER
    /// serialized. They are rebuilt from BalanceConfig on load — balance patches take effect
    /// on existing saves without version bump.
    ///
    /// Hero fields (HeroStatus / HeroDeployCost / HeroInfectionReduction / HeroRecoveryBonus)
    /// have moved to HeroDeploymentSystem.Serialization (separate singleton).
    ///
    /// Serialized: IsActive, RandomState, LastDailyTick, InternetMode,
    ///             m_LastInternetMode, CognitiveIntegrityBuffer.
    /// Rebuilt:    InfectionRate, RecoveryRate, CompromiseThreshold, CriticalThreshold,
    ///             CriticalRecoveryMultiplier, Firewall*/Blackout* multipliers.
    /// </summary>
    public partial class CognitiveStateSystem : IDefaultSerializable, IBootDefaultsReset
    {
        [System.NonSerialized] private CognitiveStatePersistState? m_PendingPersistState;
        [System.NonSerialized] private bool m_RestoreBootDefaultsAfterLoad;

        public void ResetToBootDefaults(ResetReason reason)
        {
            m_PendingPersistState = null;
            m_RestoreBootDefaultsAfterLoad = true;
            m_LastInternetMode = GlobalInternetMode.Open;
            Log.Info($"[BOOT-RESET] CognitiveStateSystem reason={reason} IsActive=False InternetMode=Open Districts=0");
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
                CognitiveState state;
                CognitiveIntegrityPersistEntry[] entries = System.Array.Empty<CognitiveIntegrityPersistEntry>();
                if (m_StateQuery.TryGetSingletonEntity<CognitiveState>(out var stateEntity))
                {
                    state = EntityManager.GetComponentData<CognitiveState>(stateEntity);
                    entries = EntityManager.HasBuffer<CognitiveIntegrityBuffer>(stateEntity)
                        ? ToPersistEntries(EntityManager.GetBuffer<CognitiveIntegrityBuffer>(stateEntity, true))
                        : System.Array.Empty<CognitiveIntegrityPersistEntry>();
                }
                else
                {
                    state = CognitiveState.Default;
                }

                var persistState = new CognitiveStatePersistState(
                    state.IsActive,
                    state.RandomState.state,
                    state.LastDailyTick,
                    state.InternetMode,
                    m_LastInternetMode,
                    entries);
                CognitiveStateCodec.Write(persistState, writer);

            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
            SerializationGuard.LogSerialized(nameof(CognitiveStateSystem), SaveVersions.GLOBAL);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SaveVersions.GLOBAL, out _, out var block, nameof(CognitiveStateSystem)))
            {
                ResetToBootDefaults(ResetReason.VersionMismatch);
                return;
            }
            try
            {
                CognitiveStateCodec.Read(reader, out var persistState);
                m_RestoreBootDefaultsAfterLoad = false;
                m_LastInternetMode = persistState.InternetMode;
                m_PendingPersistState = persistState;
                // Intentional discard: if the singleton entity isn't ready yet the pending state is
                // retained and reapplied from OnLoadRestore (which checks the result).
                _ = TryApplyPendingPersistState(EntityManager, "Deserialize");

                Log.Info($"Deserialized: IsActive={persistState.IsActive}, InternetMode={persistState.InternetMode}, Districts={persistState.IntegrityBuffer.Count} (config rebuilt from BalanceConfig)");
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

        private static Unity.Mathematics.Random RestoreRandom(uint state)
        {
            var r = default(Unity.Mathematics.Random);
            r.state = state == 0 ? 1u : state;
            return r;
        }

        public void OnLoadRestore(EntityManager entityManager)
        {
            CognitiveState.EnsureExists(entityManager);
            if (m_RestoreBootDefaultsAfterLoad)
            {
                WirePenaltySystem();
                ResetState();
                m_RestoreBootDefaultsAfterLoad = false;
                return;
            }

            if (!TryApplyPendingPersistState(entityManager, "OnLoadRestore"))
            {
                if (m_StateQuery.TryGetSingletonEntity<CognitiveState>(out var stateEntity)
                    && entityManager.Exists(stateEntity)
                    && !entityManager.HasBuffer<CognitiveIntegrityBuffer>(stateEntity))
                {
                    entityManager.AddBuffer<CognitiveIntegrityBuffer>(stateEntity);
                }
            }
        }

        private bool TryApplyPendingPersistState(EntityManager entityManager, string source)
        {
            if (!m_PendingPersistState.HasValue)
                return false;

            CognitiveState.EnsureExists(entityManager);
            if (!m_StateQuery.TryGetSingletonEntity<CognitiveState>(out var stateEntity)
                || !entityManager.Exists(stateEntity))
            {
                Log.Warn($"CognitiveState singleton missing during {source}; decoded payload retained for owner restore");
                return false;
            }

            ApplyPersistState(entityManager, stateEntity, m_PendingPersistState.Value);
            m_PendingPersistState = null;
            return true;
        }

        private void ApplyPersistState(EntityManager entityManager, Entity stateEntity, in CognitiveStatePersistState persistState)
        {
            var cfgBase = CognitiveState.Default;
            entityManager.SetComponentData(stateEntity, new CognitiveState
            {
                IsActive = persistState.IsActive,
                RandomState = RestoreRandom(persistState.RandomState),
                LastDailyTick = persistState.LastDailyTick,
                InternetMode = persistState.InternetMode,
                InfectionRate = cfgBase.InfectionRate,
                RecoveryRate = cfgBase.RecoveryRate,
                CompromiseThreshold = cfgBase.CompromiseThreshold,
                CriticalThreshold = cfgBase.CriticalThreshold,
                CriticalRecoveryMultiplier = cfgBase.CriticalRecoveryMultiplier,
                FirewallInfectionMultiplier = cfgBase.FirewallInfectionMultiplier,
                FirewallRecoveryMultiplier = cfgBase.FirewallRecoveryMultiplier,
                FirewallCommercePenalty = cfgBase.FirewallCommercePenalty,
                BlackoutCommercePenalty = cfgBase.BlackoutCommercePenalty
            });

            if (!entityManager.HasBuffer<CognitiveIntegrityBuffer>(stateEntity))
                entityManager.AddBuffer<CognitiveIntegrityBuffer>(stateEntity);

            var buffer = entityManager.GetBuffer<CognitiveIntegrityBuffer>(stateEntity);
            buffer.Clear();
            foreach (var entry in persistState.IntegrityBuffer)
            {
                buffer.Add(new CognitiveIntegrityBuffer
                {
                    DistrictIndex = entry.DistrictIndex,
                    Integrity = entry.Integrity,
                    LastUpdateTime = entry.LastUpdateTime,
                    IsCompromised = entry.IsCompromised
                });
            }

            m_LastInternetMode = persistState.LastInternetMode == GlobalInternetMode.Open
                ? persistState.InternetMode
                : persistState.LastInternetMode;
        }

        private static CognitiveIntegrityPersistEntry[] ToPersistEntries(Unity.Entities.DynamicBuffer<CognitiveIntegrityBuffer> buffer)
        {
            var entries = new CognitiveIntegrityPersistEntry[buffer.Length];
            for (int i = 0; i < buffer.Length; i++)
            {
                var entry = buffer[i];
                entries[i] = new CognitiveIntegrityPersistEntry(
                    entry.DistrictIndex,
                    entry.Integrity,
                    entry.LastUpdateTime,
                    entry.IsCompromised);
            }
            return entries;
        }
    }
}
