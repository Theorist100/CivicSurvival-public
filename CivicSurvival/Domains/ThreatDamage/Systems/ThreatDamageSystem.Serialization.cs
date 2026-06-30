using System;
using Colossal.Serialization.Entities;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Serialization;
using CivicSurvival.Core.Systems;
using CivicSurvival.Core.Utils;
using Unity.Entities;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Components.Threats;

namespace CivicSurvival.Domains.ThreatDamage.Systems
{
    public partial class ThreatDamageSystem : IDefaultSerializable, IResettable, IBootDefaultsReset
    {
        public void SetDefaults(Context context)
        {
            ResetState();
        }
        void IResettable.ResetState() => ResetState();

        /// <summary>
        /// Seed from game time + offset 0x1001 (avoids collision with ThreatSpawnSystem 0x2001).
        /// Shared between OnCreate and ResetState — single source of truth.
        /// </summary>
        private static int CalculateSeed()
        {
            // Save/load entry point: GameTimeSystem may be null before OnGameLoaded.
            // TickCount fallback keeps the seed non-deterministic but unique-per-session.
            int baseSeed = GameTimeSystem.TryGetTotalGameSeconds(out var seconds)
                ? (int)Math.Round(seconds)
                : Environment.TickCount;
            return baseSeed + 0x1001;
        }

        private void ResetState()
        {
            int seed = CalculateSeed();
            m_Random = new SerializableRandom(seed);
            ResetTransientRuntimeState();
            // Fresh spatial-desync telemetry budget per load (ephemeral, [NonSerialized]).
            m_SpatialOobEmitsThisSession = 0;

            Log.Info($"SetDefaults called with seed {seed}");
        }

        private void ResetTransientRuntimeState()
        {
            // Clear per-frame containers. V_REGRESSION Phase 8: shared
            // IFrameMutationDedup is frame-cleared by FrameMutationDedupClearSystem;
            // no per-system reset of Destroy/Ignite queues needed.
            if (m_ProcessedVictims.IsCreated) m_ProcessedVictims.Clear();
            if (m_AirDefenseBuildings.IsCreated) m_AirDefenseBuildings.Clear();
            m_AirDefenseFilter.ResetCount();
            if (m_VictimsList.IsCreated) m_VictimsList.Clear();
            if (m_DamageResults.IsCreated) m_DamageResults.Clear();
            if (m_PendingApplyImpacts.IsCreated) m_PendingApplyImpacts.Clear();

            // L4 FIX: Clear building cache and debriefing batcher (stale after load/act transition)
            // W2 M1 FIX: Dispose pending cache too (ForceComplete + Dispose, same as OnDestroy)
            m_BuildingCacheManager.ResetForReload();
            if (m_DebriefingBatcher.IsCreated) m_DebriefingBatcher.Clear();
            m_ImpactSingleton.Invalidate();
            m_LastSeenAct = default;
            m_HasSeenAct = false;
            m_threatGenerationClock = null!;
            m_IsBallisticImpact = false;
            m_BallisticCasualtyTotal = 0;
            m_BallisticWorstType = default;
        }

        public void ResetToBootDefaults(ResetReason reason)
        {
            if (m_Random.State == 0)
                m_Random = new SerializableRandom(0x1001);

            ResetTransientRuntimeState();

            Log.Info($"Boot-default reset after deserialize recovery: {reason}");
        }

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SaveVersions.GLOBAL);
            try
            {

                SerializableRandomCodec.Write(new SerializableRandomState(m_Random.State), writer);

            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
            SerializationGuard.LogSerialized(nameof(ThreatDamageSystem), SaveVersions.GLOBAL);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SaveVersions.GLOBAL, out var version, out var block, nameof(ThreatDamageSystem)))
            {
                ResetToBootDefaults(ResetReason.VersionMismatch);
                return;
            }
            try
            {
                // CIVIC348: dispose NativeContainers before overwriting state (prevents leak on mid-game load)
                m_BuildingCacheManager.ResetForReload();

#pragma warning disable CIVIC144 // SerializableRandomState is the scalar payload for SerializableRandomCodec.
                SerializableRandomCodec.Read(reader, out var randomState);
#pragma warning restore CIVIC144
                m_Random = new SerializableRandom(randomState.State);

                // Reset non-serialized state that holds stale entity references after load
                ResetTransientRuntimeState();

                Log.Info($"Deserialized v{version}: Random state={m_Random.State}");
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
