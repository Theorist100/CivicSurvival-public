using Colossal.Serialization.Entities;
using Unity.Entities;
using CivicSurvival.Core.Components.Domain.GridWarfare;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Serialization;
using CivicSurvival.Core.Systems;

namespace CivicSurvival.Domains.GridWarfare.Systems
{
    /// <summary>
    /// EnemySimulationSystem - Save/Load serialization (IDefaultSerializable).
    /// Persists the EnemyState singleton (three axes + defence) across save/load.
    /// </summary>
#pragma warning disable CIVIC223 // PostLoadValidationSystem invokes ICivicSingletonOwner.OnLoadRestore; Deserialize only buffers payload.
    public partial class EnemySimulationSystem : IDefaultSerializable, IResettable, IBootDefaultsReset
    {
        public void ResetToBootDefaults(ResetReason reason)
        {
            m_RegenClockInitialized = false;
            m_LastAxisRegenGameTimeHours = 0f;
            m_HasRestoredEnemyState = false;
        }

#pragma warning restore CIVIC223
#pragma warning disable CIVIC221 // Restore buffer is written in Deserialize and applied by ICivicSingletonOwner.OnLoadRestore; Serialize reads live singleton state.
        [System.NonSerialized] private EnemyState m_RestoredEnemyState;
#pragma warning restore CIVIC221
        [System.NonSerialized] private bool m_HasRestoredEnemyState;

        /// <summary>
        /// Called when starting a new game (not loading a save).
        /// </summary>
        public void SetDefaults(Context context) => ResetState();
        void IResettable.ResetState() => ResetState();

        private void ResetState()
        {
            m_RegenClockInitialized = false;
            m_LastAxisRegenGameTimeHours = 0f;

            // Reset singleton to defaults (prevents partially-loaded data after deser failure)
            if (m_EnemyStateQuery.TryGetSingletonEntity<EnemyState>(out var enemyEntity))
                EntityManager.SetComponentData(enemyEntity, EnemyState.Default);
            m_HasRestoredEnemyState = false;

            Log.Info("SetDefaults: Starting fresh");
        }

        /// <summary>
        /// Serialize enemy state to save file.
        /// </summary>
        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SaveVersions.GLOBAL);
            try
            {
                // TOCTOU FIX: Use TryGetSingleton instead of IsEmpty + GetSingleton
                if (!m_EnemyStateQuery.TryGetSingleton<EnemyState>(out var state))
                    state = EnemyState.Default;

                // Layer 3: pure value codec owns the wire.
                EnemyStateCodec.Write(state, writer);
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
            SerializationGuard.LogSerialized(nameof(EnemySimulationSystem), SaveVersions.GLOBAL);
        }

        /// <summary>
        /// Deserialize enemy state from save file.
        /// </summary>
        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SaveVersions.GLOBAL, out var version, out var block, nameof(EnemySimulationSystem)))
            {
                ResetToBootDefaults(ResetReason.VersionMismatch);
                return;
            }
            try
            {
                EnemyStateCodec.Read(reader, out var es);

                m_RestoredEnemyState = es;
                m_HasRestoredEnemyState = true;
                m_LastAxisRegenGameTimeHours = 0f;
                m_RegenClockInitialized = false;

                Log.Info($"Deserialized v{version}: Physical={es.PhysicalAxis:F1}%, Digital={es.DigitalAxis:F1}%, Social={es.SocialAxis:F1}%");
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
            EnemyState.EnsureExists(entityManager);

            if (m_EnemyStateQuery.TryGetSingletonEntity<EnemyState>(out var enemyEntity))
            {
                entityManager.SetComponentData(enemyEntity, m_HasRestoredEnemyState
                    ? m_RestoredEnemyState
                    : EnemyState.Default);
            }
            m_HasRestoredEnemyState = false;
        }
    }
}
