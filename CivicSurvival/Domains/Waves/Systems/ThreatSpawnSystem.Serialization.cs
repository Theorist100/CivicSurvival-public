using Colossal.Serialization.Entities;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Serialization;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Domains.Waves.Systems
{
    /// <summary>
    /// ThreatSpawnSystem - Save/Load serialization (IDefaultSerializable).
    /// FIX EC-SL-008: Persists Random state to maintain determinism across save/load.
    /// Without this fix, random would be overwritten in OnCreate() after Deserialize().
    /// </summary>
    public partial class ThreatSpawnSystem : IDefaultSerializable, IResettable, IBootDefaultsReset
    {
        // FIX EC-SL-008: Saved state to apply in OnCreate (after Deserialize runs)
        private ulong? m_SavedRandomState;  // RD-001: Changed to ulong for SerializableRandom
        private bool m_HasSavedBounds;
        private Unity.Mathematics.float3 m_SavedMapMin;
        private Unity.Mathematics.float3 m_SavedMapMax;
        private bool m_SavedBoundsCached;
        private int m_SavedWavesSinceRecache;

        public void SetDefaults(Context context) => ResetState();
        void IResettable.ResetState() => ResetState();

        private void ResetState()
        {
            // FIX EC-SL-008: Clear saved state (new game, not loading)
            m_SavedRandomState = null;
            m_HasSavedBounds = false;
            m_SavedMapMin = default;
            m_SavedMapMax = default;
            m_SavedBoundsCached = false;
            m_SavedWavesSinceRecache = 0;

            // Random — re-seed for new session
            m_Random = new SerializableRandom(System.Environment.TickCount + 0x2001);

            // Map bounds — will be recalculated on first wave
            m_MapMin = default;
            m_MapMax = default;
            m_BoundsCached = false;
            m_WavesSinceRecache = 0;
            // Drone prefab entity is owned by CivicPrefabInitSystem; resolved once at
            // IInitializable.OnInitialize, which fires on every load via PLVS — no cached
            // state in this system to clear.

            // Target cache lives on ThreatTargetCacheSystem and refreshes on its
            // own throttle. Drop the cached source reference so the next SpawnWave
            // re-resolves it via ServiceRegistry — defensive against any
            // ServiceRegistry rewiring across save/load.
            if (m_TargetSelector != null) m_TargetSelector.Source = null;
            m_TargetSource = null;
            m_threatGenerationClock = null!;

            // L11 FIX: Reset diagnostic ECB counter (cosmetic — prevents stale totals in PERF log)
            ResetCounters();

            // Spawn-batch sequence — transient [NonSerialized] counter; reset so a reused
            // system instance starts fresh after load (wrap-guard re-bases it to 1 on next ++).
            m_SpawnBatchSeq = 0;

            // Exhaust attach-controller log-arm flag — transient cosmetic state, restart fresh
            m_ExhaustAttachLogArmed = false;

            // One-shot missing-drone report latch — transient, restart fresh after load
            m_ReportedMissingDronePrefab = false;

            Log.Info("SetDefaults: Starting fresh");
        }

        public void ResetToBootDefaults(ResetReason reason)
        {
            ClearSavedState();
            Log.Info($"[BOOT-RESET] ThreatSpawnSystem reason={reason} HasSavedBounds={m_HasSavedBounds} SavedRandomState={(m_SavedRandomState.HasValue ? m_SavedRandomState.Value.ToString() : "null")} WavesSinceRecache={m_SavedWavesSinceRecache}");
        }

        private void ClearSavedState()
        {
            m_SavedRandomState = null;
            m_HasSavedBounds = false;
            m_SavedMapMin = default;
            m_SavedMapMax = default;
            m_SavedBoundsCached = false;
            m_SavedWavesSinceRecache = 0;
        }

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SaveVersions.GLOBAL);
            try
            {

                var state = new ThreatSpawnPersistState(
                    m_Random.State,
                    m_MapMin.x,
                    m_MapMin.y,
                    m_MapMin.z,
                    m_MapMax.x,
                    m_MapMax.y,
                    m_MapMax.z,
                    m_BoundsCached,
                    m_WavesSinceRecache);
                ThreatSpawnCodec.Write(state, writer);

            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
            SerializationGuard.LogSerialized(nameof(ThreatSpawnSystem), SaveVersions.GLOBAL);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SaveVersions.GLOBAL, out var version, out var block, nameof(ThreatSpawnSystem)))
            {
                ResetToBootDefaults(ResetReason.VersionMismatch);
                return;
            }
            try
            {
                ThreatSpawnCodec.Read(reader, out var state);

                // FIX EC-SL-008: Store in saved state, apply in OnCreate after initialization
                // Guard: Xorshift64 state can never be 0. Zero = field not read (corrupt/missing).
                // Leave m_SavedRandomState null → ApplySavedState uses OnCreate's fresh seed.
                if (state.RandomState != 0) m_SavedRandomState = state.RandomState;
                m_SavedMapMin = new Unity.Mathematics.float3(state.MinX, state.MinY, state.MinZ);
                m_SavedMapMax = new Unity.Mathematics.float3(state.MaxX, state.MaxY, state.MaxZ);
                // Bounds are process-local terrain cache. Recalculate from TerrainSystem
                // after load instead of replaying a stale saved map snapshot.
                m_HasSavedBounds = false;
                m_SavedBoundsCached = false;
                m_SavedWavesSinceRecache = 0;
                m_BoundsCached = false;
                m_WavesSinceRecache = 0;
                m_TargetSource = null;
                if (m_TargetSelector != null) m_TargetSelector.Source = null;
                m_threatGenerationClock = null!;

                Log.Info($"Deserialized v{version}: RandomState={state.RandomState} (pending apply in OnCreate)");
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

        /// <summary>
        /// FIX EC-SL-008: Apply saved state after OnCreate initializes fields.
        /// Called from OnCreate() after default initialization.
        /// </summary>
        internal void ApplySavedState()
        {
            if (m_SavedRandomState.HasValue)
            {
                // RD-001: Create new SerializableRandom with saved state
                m_Random = new SerializableRandom(m_SavedRandomState.Value);
                if (Log.IsDebugEnabled) Log.Debug($"EC-SL-008: Random state restored from save: {m_Random.State}");
                m_SavedRandomState = null;
            }

            if (m_HasSavedBounds)
            {
                m_MapMin = m_SavedMapMin;
                m_MapMax = m_SavedMapMax;
                m_BoundsCached = m_SavedBoundsCached;
                m_WavesSinceRecache = m_SavedWavesSinceRecache;
                if (Log.IsDebugEnabled) Log.Debug("EC-SL-008: Map bounds restored from save");
                m_HasSavedBounds = false;
            }
        }
    }
}

