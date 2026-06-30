using System;
using System.Collections.Generic;
using Colossal.Logging;
using CivicSurvival.Core.Utils;
using Game;
using Game.Prefabs;
using Game.Simulation;
using Unity.Entities;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Attributes;

namespace CivicSurvival.Core.Systems.Effects
{
    /// <summary>
    /// Effect prefab cache system.
    /// Caches CS2 built-in effect prefabs (SFX/VFX) for fast lookup by name.
    ///
    /// Used by:
    /// - VanillaVfxSystem (native SFX via EnabledEffectData)
    /// - Any system needing effect prefab entities
    ///
    /// VFX explosions are handled by VanillaVfxSystem (direct EnabledEffectData injection).
    /// Consumers must poll IsInitialized/TryGetEffect; domain registration order does not
    /// guarantee prefab cache readiness on the first simulation tick.
    /// </summary>
    [ActIndependent]
    [FrameworkSystem]
    public partial class EffectCacheSystem : CivicSystemBase
    {
        private static readonly LogContext Log = new("EffectCacheSystem");

        private readonly Dictionary<string, Entity> m_Cache = new();

        private PrefabSystem m_PrefabSystem = null!;
        private bool m_Initialized;
        private bool m_VfxSurveyDone;
        private float m_LastLogTime = float.NegativeInfinity; // T-024 fix: throttle logs to prevent spam
        private EntityQuery m_EffectDataQuery; // Cached query for init (avoids CreateEntityQuery per frame)

        // Picks one entry when resolving AudioRandomize containers, mirroring vanilla
        // AudioManager.GetRandomizeAudio. Transient — not serialized; audio variety only.
        private Unity.Mathematics.Random m_Random;

        #region Public API

        public bool IsValid => World != null && World.IsCreated;

        public bool IsInitialized => m_Initialized;

        public void InvalidateCache()
        {
            m_Cache.Clear();

            if (m_EffectDataQuery != default)
            {
                m_EffectDataQuery.Dispose();
                m_EffectDataQuery = default;
            }

            m_Initialized = false;
            m_LastLogTime = float.NegativeInfinity;
        }

        [CompletesDependency("CacheEffects: one-shot prefab cache rebuild outside gameplay hot paths; ToEntityArray materialises EffectData prefab entities once, then m_Initialized gates subsequent calls")]
        private void CacheEffects()
        {
            if (m_Initialized) return;

            try
            {
                if (m_EffectDataQuery == default)
                    m_EffectDataQuery = EntityManager.CreateEntityQuery(ComponentType.ReadOnly<EffectData>());
                var entities = m_EffectDataQuery.ToEntityArray(Unity.Collections.Allocator.Temp);

                int found = 0;
                foreach (var entity in entities)
                {
                    if (m_PrefabSystem.TryGetPrefab(entity, out PrefabBase prefab))
                    {
                        string name = prefab.name;
                        if (!m_Cache.ContainsKey(name))
                        {
                            m_Cache[name] = entity;
                            found++;
                        }
                    }
                }

                if (entities.IsCreated) entities.Dispose();

                if (found > 0)
                {
                    m_Initialized = true;
                    Log.Info($"[EffectCacheSystem] Cached {m_Cache.Count} effect prefabs");
                    LogEffectAvailability();
                    return;
                }
                else
                {
                    // T-024 fix: Log throttling - prevents console spam if PrefabSystem is slow/broken
                    float now = UnityEngine.Time.realtimeSinceStartup;
                    if (now - m_LastLogTime >= 1.0f)
                    {
                        m_LastLogTime = now;
                        Log.Debug("[EffectCacheSystem] Waiting for effects...");
                    }
                    return;
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[EffectCacheSystem] Init error: {ex}");
            }
        }

        public Entity GetEffect(string effectName)
        {
            if (!m_Initialized)
            {
                return Entity.Null;
            }

            if (m_Cache.TryGetValue(effectName, out Entity entity))
            {
                if (EntityManager.Exists(entity) && EntityManager.HasComponent<EffectData>(entity))
                {
                    return entity;
                }

                // Evict only the offending entry — other cached prefabs remain valid.
                // A full InvalidateCache() here strips SFX/VFX from unrelated consumers
                // mid-frame (row W2-44: unordered consumers lose all sound that frame).
                Log.Warn($"[EffectCacheSystem] Cached effect '{effectName}' is stale; evicting entry");
                m_Cache.Remove(effectName);
            }

            return Entity.Null;
        }

        public bool TryGetEffect(string effectName, out Entity entity)
        {
            entity = GetEffect(effectName);
            return entity != Entity.Null;
        }

        public bool TryGetSfx(string effectName, out Entity entity)
        {
            entity = GetEffect(effectName);
            if (entity == Entity.Null)
                return false;

            // Several vanilla SFX effects (LightningSFX, BuildingCollapseSFX, ...) are
            // AudioRandomize containers: the prefab entity carries an AudioRandomizeData
            // buffer of references to concrete SFX entities, not an AudioSourceData buffer
            // of its own. AudioManager.AddTemp resolves AudioSourceData only, so collapse
            // the container to one of its SFX entries first (mirrors GetRandomizeAudio).
            if (!EntityManager.HasBuffer<AudioSourceData>(entity)
                && EntityManager.HasBuffer<AudioRandomizeData>(entity))
            {
                var randomize = EntityManager.GetBuffer<AudioRandomizeData>(entity, true);
                if (randomize.Length == 0)
                {
                    Log.Warn($"[EffectCacheSystem] Effect '{effectName}' has an empty AudioRandomizeData buffer");
                    entity = Entity.Null;
                    return false;
                }

                entity = randomize[m_Random.NextInt(randomize.Length)].m_SFXEntity;
            }

            if (!EntityManager.HasBuffer<AudioSourceData>(entity))
            {
                Log.Warn($"[EffectCacheSystem] Effect '{effectName}' is not an audio prefab; missing AudioSourceData");
                entity = Entity.Null;
                return false;
            }

            var audioSources = EntityManager.GetBuffer<AudioSourceData>(entity, true);
            if (audioSources.Length > 0)
                return true;

            Log.Warn($"[EffectCacheSystem] Effect '{effectName}' has an empty AudioSourceData buffer");
            entity = Entity.Null;
            return false;
        }

        #endregion

        protected override void OnCreate()
        {
            base.OnCreate();
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_Random = Unity.Mathematics.Random.CreateFromIndex(0); // non-zero seed for randomize-container picks
            Log.Info($"{nameof(EffectCacheSystem)} created");
        }

        protected override void OnUpdateImpl()
        {
            if (!m_Initialized)
            {
                CacheEffects();
            }
        }

        protected override void OnDestroy()
        {
            InvalidateCache();

            Log.Info($"{nameof(EffectCacheSystem)} destroyed");
            base.OnDestroy();
        }

        /// <summary>
        /// Log availability of key SFX effects for diagnostic purposes.
        /// </summary>
        private void LogEffectAvailability()
        {
            string[] sfxEffects = {
                EffectNames.SIREN_LOOP, EffectNames.COLLAPSE_SFX, EffectNames.LIGHTNING_SFX, EffectNames.FIRE_LOOP
            };

            var sfxStatus = new bool[sfxEffects.Length];

            for (int i = 0; i < sfxEffects.Length; i++)
                sfxStatus[i] = m_Cache.ContainsKey(sfxEffects[i]);

            Log.Debug("[EffectCacheSystem] === SFX ===");
            for (int i = 0; i < sfxEffects.Length; i++)
            {
                if (Log.IsDebugEnabled) Log.Debug($"  {sfxEffects[i]}: {(sfxStatus[i] ? "OK" : "NOT FOUND")}");
            }

            // DIAG: Survey VFX effects and prefabs with Effect buffers (variant 6 discovery)
            if (Log.IsDebugEnabled && !m_VfxSurveyDone)
            {
                m_VfxSurveyDone = true;
                LogVfxEffectSurvey();
            }
        }

        /// <summary>
        /// DIAG: Log all EffectData entities that have VFXData (= VFX Graph effects),
        /// then find prefab entities whose Effect buffer references explosion-like VFX.
        /// One-shot diagnostic for variant 6 implementation.
        /// </summary>
#pragma warning disable CIVIC050, CIVIC051, CIVIC218 // Diagnostic one-shot survey — dynamic queries, not hot path
        private void LogVfxEffectSurvey()
        {
            Log.Info("[VFX:DIAG] === VFX Effect Survey START ===");

            // Part 1: All EffectData entities with VFXData = available VFX effects
            var vfxQuery = EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<EffectData>(),
                ComponentType.ReadOnly<VFXData>());
            var vfxEntities = vfxQuery.ToEntityArray(Unity.Collections.Allocator.Temp);
            int vfxCount = 0;

            foreach (var entity in vfxEntities)
            {
                if (m_PrefabSystem.TryGetPrefab(entity, out PrefabBase prefab))
                {
                    var vfxData = EntityManager.GetComponentData<VFXData>(entity);
                    var effectData = EntityManager.GetComponentData<EffectData>(entity);
                    Log.Info($"[VFX:DIAG] VFX effect: '{prefab.name}' (entity={entity.Index}, maxCount={vfxData.m_MaxCount}, " +
                             $"ownerCulling={effectData.m_OwnerCulling}, " +
                             $"required={effectData.m_Flags.m_RequiredFlags}, " +
                             $"forbidden={effectData.m_Flags.m_ForbiddenFlags}, " +
                             $"intensity={effectData.m_Flags.m_IntensityFlags})");
                    vfxCount++;
                }
            }
            if (vfxEntities.IsCreated) vfxEntities.Dispose();
            vfxQuery.Dispose(); // FIX M7: dispose diagnostic query
            Log.Info($"[VFX:DIAG] Total VFX effects: {vfxCount}");

            // Part 2: Find prefab entities with Effect buffer containing explosion/fire VFX
            // These are the prefabs we can use as PrefabRef for variant 6
            var effectBufferQuery = EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<Game.Prefabs.PrefabData>(),
                ComponentType.ReadOnly<Game.Prefabs.Effect>());
            var prefabEntities = effectBufferQuery.ToEntityArray(Unity.Collections.Allocator.Temp);
            int explosionPrefabs = 0;

            foreach (var entity in prefabEntities)
            {
                if (!EntityManager.HasBuffer<Game.Prefabs.Effect>(entity)) continue;
                var buffer = EntityManager.GetBuffer<Game.Prefabs.Effect>(entity, true);
                bool hasExplosionVfx = false;
                var effectNamesSb = new System.Text.StringBuilder();

                for (int i = 0; i < buffer.Length; i++)
                {
                    Entity effectEntity = buffer[i].m_Effect;
                    if (effectEntity == Entity.Null) continue;

                    // Check if this effect is a VFX (has VFXData)
                    if (!EntityManager.HasComponent<VFXData>(effectEntity)) continue;

                    string effectName = "?";
                    if (m_PrefabSystem.TryGetPrefab(effectEntity, out PrefabBase effectPrefab))
                        effectName = effectPrefab.name;

                    // Look for explosion/fire/smoke/dust effects
                    string upper = effectName.ToUpperInvariant();
                    bool isExplosion = upper.Contains("EXPLO", StringComparison.Ordinal) ||
                                      upper.Contains("FIRE", StringComparison.Ordinal) ||
                                      upper.Contains("SMOKE", StringComparison.Ordinal) ||
                                      upper.Contains("DUST", StringComparison.Ordinal) ||
                                      upper.Contains("BURN", StringComparison.Ordinal) ||
                                      upper.Contains("SPARK", StringComparison.Ordinal) ||
                                      upper.Contains("EMBER", StringComparison.Ordinal);

                    if (isExplosion)
                    {
                        hasExplosionVfx = true;
                        effectNamesSb.Append($"[{i}]='{effectName}' ");
                    }
                }

                if (hasExplosionVfx)
                {
                    string prefabName = "?";
                    if (m_PrefabSystem.TryGetPrefab(entity, out PrefabBase ownerPrefab))
                        prefabName = ownerPrefab.name;

                    Log.Info($"[VFX:DIAG] Prefab '{prefabName}' (entity={entity.Index}, effects={buffer.Length}): {effectNamesSb}");
                    explosionPrefabs++;

                    // Cap output to avoid log spam
                    const int maxLoggedPrefabs = 30;
                    if (explosionPrefabs >= maxLoggedPrefabs)
                    {
                        Log.Info("[VFX:DIAG] ... capped at 30 prefabs");
                        break;
                    }
                }
            }
            if (prefabEntities.IsCreated) prefabEntities.Dispose();
            effectBufferQuery.Dispose(); // FIX M7: dispose diagnostic query

            Log.Info($"[VFX:DIAG] Prefabs with explosion/fire VFX: {explosionPrefabs}");
            Log.Info("[VFX:DIAG] === VFX Effect Survey END ===");
        }
#pragma warning restore CIVIC050, CIVIC051, CIVIC218
    }
}
