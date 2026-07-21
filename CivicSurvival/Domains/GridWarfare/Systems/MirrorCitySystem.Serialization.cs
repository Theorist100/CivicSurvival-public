using System;
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
    /// MirrorCitySystem - Save/Load serialization (IDefaultSerializable).
    /// Persists the MirrorCityState header + the EnemyTarget buffer (the authoritative city model)
    /// across save/load via MirrorCityCodec. The derived axes live on EnemyState and are persisted
    /// by EnemySimulationSystem's own block — this system never writes them from the load path.
    /// Restore follows the EnemySimulationSystem owner pattern: Deserialize buffers the payload,
    /// OnLoadRestore recreates the singleton + buffer and applies it.
    /// </summary>
#pragma warning disable CIVIC223 // PostLoadValidationSystem invokes ICivicSingletonOwner.OnLoadRestore; Deserialize only buffers payload.
    public partial class MirrorCitySystem : IDefaultSerializable, IResettable, IBootDefaultsReset
    {
        public void ResetToBootDefaults(ResetReason reason)
        {
            m_RepairClockInitialized = false;
            m_LastRepairGameTimeHours = 0f;
            m_RestoredState = MirrorCityState.Default;
            m_RestoredTargets = Array.Empty<EnemyTarget>();
            m_HasRestoredCity = false;
            m_SeedDamageFromAxes = true; // default: allow the old-save migration (new-game path is a no-op)
            m_PreserveProgressOnRegen = false;
        }

#pragma warning restore CIVIC223
#pragma warning disable CIVIC221 // Restore buffer is written in Deserialize and applied by ICivicSingletonOwner.OnLoadRestore; Serialize reads live singleton state.
        [System.NonSerialized] private MirrorCityState m_RestoredState;
        [System.NonSerialized] private EnemyTarget[] m_RestoredTargets = Array.Empty<EnemyTarget>();
#pragma warning restore CIVIC221
        [System.NonSerialized] private bool m_HasRestoredCity;

        /// <summary>
        /// Called when starting a new game (not loading a save).
        /// </summary>
        public void SetDefaults(Context context) => ResetState();
        void IResettable.ResetState() => ResetState();

        private void ResetState()
        {
            m_RepairClockInitialized = false;
            m_LastRepairGameTimeHours = 0f;

            // Reset singleton + buffer to defaults (prevents partially-loaded data after deser failure).
            if (m_MirrorQuery.TryGetSingletonEntity<MirrorCityState>(out var entity))
            {
                EntityManager.SetComponentData(entity, MirrorCityState.Default);
                if (EntityManager.HasBuffer<EnemyTarget>(entity))
                    EntityManager.GetBuffer<EnemyTarget>(entity).Clear();
            }

            m_RestoredState = MirrorCityState.Default;
            m_RestoredTargets = Array.Empty<EnemyTarget>();
            m_HasRestoredCity = false;
            m_SeedDamageFromAxes = true; // new game: healthy enemy axes make the migration a self-gating no-op
            m_PreserveProgressOnRegen = false;

            Log.Info("SetDefaults: Starting fresh (un-generated mirror city)");
        }

        /// <summary>
        /// Serialize the mirror-city model to the save file.
        /// </summary>
        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SaveVersions.GLOBAL);
            try
            {
                if (!m_MirrorQuery.TryGetSingletonEntity<MirrorCityState>(out var entity))
                {
                    MirrorCityCodec.Write(MirrorCityState.Default, Array.Empty<EnemyTarget>(), writer);
                }
                else
                {
                    var state = EntityManager.GetComponentData<MirrorCityState>(entity);
                    EnemyTarget[] targets;
                    if (EntityManager.HasBuffer<EnemyTarget>(entity))
                    {
                        var buffer = EntityManager.GetBuffer<EnemyTarget>(entity, isReadOnly: true);
                        targets = new EnemyTarget[buffer.Length];
                        for (int i = 0; i < buffer.Length; i++)
                            targets[i] = buffer[i];
                    }
                    else
                    {
                        targets = Array.Empty<EnemyTarget>();
                    }
                    MirrorCityCodec.Write(state, targets, writer);
                }
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
            SerializationGuard.LogSerialized(nameof(MirrorCitySystem), SaveVersions.GLOBAL);
        }

        /// <summary>
        /// Deserialize the mirror-city model from the save file into the restore buffer.
        /// </summary>
        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SaveVersions.GLOBAL, out var version, out var block, nameof(MirrorCitySystem)))
            {
                ResetToBootDefaults(ResetReason.VersionMismatch);
                return;
            }
            try
            {
                MirrorCityCodec.Read(reader, out var state, out var targets, out bool variantIdOutOfRange);

                // genVersion policy (pre-release, decision 12): a format-version mismatch regenerates
                // rather than migrates — drop the stale buffer and let the tick regenerate a fresh city.
                // Crucially this is NOT the old-save migration (decision 11): a version bump is
                // "reset to defaults, no damage carry-over", so suppress the axis-seeded damage and let
                // the fresh city stand at full hp regardless of the (now stale) derived enemy axes.
                // An out-of-range persisted variant id (rollback from a larger catalog / corruption)
                // takes the same path: the codec reset the id to 0, but the targets were generated for
                // a DIFFERENT map — pairing them with variant-0's contour would be incoherent.
                int liveTuningHash = Core.Logic.MirrorCityGenerator.ComputeTuningHash(Core.Config.BalanceConfig.Current.GridWarfare);
                if (state.Generated
                    && (state.GenVersion != MirrorCityState.CurrentGenVersion || variantIdOutOfRange))
                {
                    Log.Info($"Deserialized v{version}: genVersion {state.GenVersion}/{MirrorCityState.CurrentGenVersion}" +
                             (variantIdOutOfRange ? ", out-of-range variantId" : string.Empty) +
                             " mismatch — regenerating city (full hp, no damage carry-over)");
                    state = MirrorCityState.Default;
                    targets = Array.Empty<EnemyTarget>();
                    m_SeedDamageFromAxes = false;
                }
                else if (state.Generated && state.TuningHash != liveTuningHash)
                {
                    // Catalog-affecting balance retune (TuningHash stamp): those fields shift the
                    // generator's draw sequence, so the persisted buffer is unreproducible under the
                    // live config and must regenerate — but unlike a genVersion bump this is a data-side
                    // retune, not a format change, so the PLAYER'S PROGRESS is preserved: keep the
                    // rolled variant (same map, no surprise re-roll), keep the permanent CapCut* (key
                    // kills), and let decision-11 seeding re-apply the persisted enemy axes as damage
                    // on the fresh targets ("received a recon map", not a silent wipe).
                    Log.Warn($"Deserialized v{version}: tuningHash {state.TuningHash:X8}/{liveTuningHash:X8} mismatch — " +
                             $"regenerating city under live tuning, preserving variant {state.VariantId}, cap cuts and axis progress");
                    var fresh = MirrorCityState.Default;
                    fresh.VariantId = state.VariantId;
                    fresh.CapCutPhysical = state.CapCutPhysical;
                    fresh.CapCutDigital = state.CapCutDigital;
                    fresh.CapCutSocial = state.CapCutSocial;
                    state = fresh;
                    targets = Array.Empty<EnemyTarget>();
                    m_SeedDamageFromAxes = true;
                    m_PreserveProgressOnRegen = true;
                }
                else
                {
                    // Normal restore, or an old save that never had a city (decision 11): allow the
                    // first generation to migrate the current enemy axes onto the fresh targets.
                    m_SeedDamageFromAxes = true;
                }

                m_RestoredState = state;
                m_RestoredTargets = targets ?? Array.Empty<EnemyTarget>();
                m_HasRestoredCity = true;
                m_RepairClockInitialized = false;
                m_LastRepairGameTimeHours = 0f;

                Log.Info($"Deserialized v{version}: variant={state.VariantId}, generated={state.Generated}, targets={m_RestoredTargets.Length}");
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
            MirrorCityState.EnsureExists(entityManager);

            if (m_MirrorQuery.TryGetSingletonEntity<MirrorCityState>(out var entity))
            {
                entityManager.SetComponentData(entity, m_HasRestoredCity ? m_RestoredState : MirrorCityState.Default);

                if (entityManager.HasBuffer<EnemyTarget>(entity))
                {
                    var buffer = entityManager.GetBuffer<EnemyTarget>(entity);
                    buffer.Clear();
                    if (m_HasRestoredCity && m_RestoredTargets != null)
                    {
                        for (int i = 0; i < m_RestoredTargets.Length; i++)
                            buffer.Add(m_RestoredTargets[i]);
                    }
                }
            }

            // Transient repair clock is re-seeded on the next tick; the restored targets are
            // authoritative and the derived axes are recomputed once the tick observes a change.
            // Seam (phase F): reconciles the derived EnemyState axes with the restored targets on
            // load (RecomputeAxes on post-load) — deliberately not done here so this system never
            // writes the EnemyState axes from the load path in the skeleton.
            m_HasRestoredCity = false;
            m_RestoredTargets = Array.Empty<EnemyTarget>();
            m_RepairClockInitialized = false;
        }
    }
}
