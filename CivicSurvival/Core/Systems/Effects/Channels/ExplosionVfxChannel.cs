using System;
using System.Collections.Generic;
using Game.Effects;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Core.Systems.Effects
{
    /// <summary>
    /// Channel for explosion VFX entries injected into vanilla EnabledEffectData.
    ///
    /// Owns active and pending explosion state. Anchor entity, EnabledEffectLookup
    /// and shared effect-id counter are supplied by the host system on each call —
    /// the channel itself is a plain managed object, not a SystemBase.
    ///
    /// Effects are added once and removed once (not re-injected every frame).
    /// CompleteEnabledSystem maintains index consistency through the anchor's
    /// EnabledEffect buffer on RemoveAtSwapBack.
    /// </summary>
    internal sealed class ExplosionVfxChannel
    {
        private static readonly LogContext Log = new("VanillaVfxSystem");

        public const int MAX_CONCURRENT = 32;
        public const int MAX_PENDING_REQUESTS = MAX_CONCURRENT * 2;

        // MuzzleFlash is high-frequency (one per active AA per firing frame) and purely
        // cosmetic with a very short lifetime. It shares the channel with gameplay-meaningful
        // combat explosions (DirectHit/Ballistic/Intercept/Debris). Without a sub-budget,
        // volley AA fire saturates the pool and the full-pool drop in AddPending removes the
        // combat explosions instead. Cap concurrent muzzle flashes so they can never starve
        // combat explosions out of the shared pool.
        private const int MAX_CONCURRENT_MUZZLE_FLASH = 8;

        // VFX scale per explosion type
        private const float SCALE_DIRECT_HIT = 2.5f;
        private const float SCALE_BALLISTIC = 4.0f;
        private const float SCALE_INTERCEPT = 1.2f;
        private const float SCALE_DEBRIS = 0.7f;
        private const float SCALE_MUZZLE_FLASH = 0.3f;

        // Lifetime per explosion type (seconds)
        private const float LIFETIME_DIRECT_HIT = 4.0f;
        private const float LIFETIME_BALLISTIC = 6.0f;
        private const float LIFETIME_INTERCEPT = 2.0f;
        private const float LIFETIME_DEBRIS = 1.5f;
        private const float LIFETIME_MUZZLE_FLASH = 0.5f;

        private struct ExplosionRequest
        {
            public float3 Position;
            public ExplosionType Type;
        }

        private struct ActiveEffect
        {
            public float3 Position;
            public float Scale;
            public float ExpiresAt;
            public int EffectId;
            public int EffectSlot;
            public ExplosionType Type;
            public Entity Prefab;
            public int VfxIndex;
        }

        private readonly struct ResolvedEffect
        {
            public ResolvedEffect(Entity prefab, int vfxIndex)
            {
                Prefab = prefab;
                VfxIndex = vfxIndex;
            }

            public Entity Prefab { get; }
            public int VfxIndex { get; }
        }

        private readonly ActiveEffect[] m_Active = new ActiveEffect[MAX_CONCURRENT];
        private readonly bool[] m_SlotInUse = new bool[MAX_CONCURRENT];
        private int m_ActiveCount;
        private int m_MuzzleActiveCount;

        private readonly List<ExplosionRequest> m_Pending = new();

        private Entity m_ExplosionPrefab;
        // MuzzleFlash uses a distinct, non-fire effect (sparks) so rapid AA fire does not
        // render as a building-fire plume on the gun. Falls back to the explosion prefab if unset.
        private Entity m_MuzzleFlashPrefab;
        private int m_ExplosionVfxIndex = -1;
        private int m_MuzzleFlashVfxIndex = -1;

        public int ActiveCount => m_ActiveCount;
        public int PendingCount => m_Pending.Count;
        public bool IsIdle => m_ActiveCount == 0 && m_Pending.Count == 0;
        public Entity ExplosionPrefab => m_ExplosionPrefab;

        public void SetPrefab(Entity prefab, int vfxIndex)
        {
            m_ExplosionPrefab = prefab;
            m_ExplosionVfxIndex = vfxIndex;
        }

        public void SetMuzzleFlashPrefab(Entity prefab, int vfxIndex)
        {
            m_MuzzleFlashPrefab = prefab;
            m_MuzzleFlashVfxIndex = vfxIndex;
        }

        // Cosmetic, high-frequency effect types. They run under their own sub-budgets and
        // are added after the gameplay-meaningful combat explosions (see AddPending).
        private static bool IsCosmetic(ExplosionType type) =>
            type == ExplosionType.MuzzleFlash;

        // MuzzleFlash gets its own effect; everything else (impacts, intercepts, debris)
        // shares the explosion prefab. Falls back to the explosion prefab when the
        // dedicated effect is unbound.
        private ResolvedEffect ResolveEffect(ExplosionType type)
        {
            if (type == ExplosionType.MuzzleFlash && m_MuzzleFlashPrefab != Entity.Null)
                return new ResolvedEffect(m_MuzzleFlashPrefab, m_MuzzleFlashVfxIndex);
            return new ResolvedEffect(m_ExplosionPrefab, m_ExplosionVfxIndex);
        }

        public void Enqueue(float3 position, ExplosionType type)
        {
            if (m_Pending.Count >= MAX_PENDING_REQUESTS)
            {
                Log.Warn($"[VFX] DROPPED request (pending full: {m_Pending.Count}/{MAX_PENDING_REQUESTS}, active={m_ActiveCount}/{MAX_CONCURRENT})");
                return;
            }
            m_Pending.Add(new ExplosionRequest { Position = position, Type = type });
        }

        /// <summary>
        /// True if any active effect has reached its expiration time.
        /// </summary>
        public bool HasExpiring(float now)
        {
            for (int i = 0; i < m_ActiveCount; i++)
            {
                if (now >= m_Active[i].ExpiresAt) return true;
            }
            return false;
        }

        /// <summary>
        /// Reset state after save/load. Caller is responsible for re-init prefab.
        /// </summary>
        public void Reset()
        {
            m_ActiveCount = 0;
            m_MuzzleActiveCount = 0;
            ClearSlots();
            m_Pending.Clear();
            m_ExplosionPrefab = Entity.Null;
            m_MuzzleFlashPrefab = Entity.Null;
            m_ExplosionVfxIndex = -1;
            m_MuzzleFlashVfxIndex = -1;
        }

        /// <summary>
        /// Runtime anchor loss invalidates active EnabledEffectData ownership, but
        /// pending requests can be replayed after the anchor is recreated.
        /// </summary>
        public void ResetForAnchorRecreate()
        {
            m_ActiveCount = 0;
            m_MuzzleActiveCount = 0;
            ClearSlots();
            m_ExplosionPrefab = Entity.Null;
            m_MuzzleFlashPrefab = Entity.Null;
            m_ExplosionVfxIndex = -1;
            m_MuzzleFlashVfxIndex = -1;
        }

        /// <summary>
        /// Reclaim slots whose disabled EnabledEffectData entries have already
        /// been physically removed by vanilla CompleteEnabledSystem.
        /// </summary>
        public void ReclaimReleasedSlots(
            DynamicBuffer<EnabledEffect> anchorBuffer,
            DynamicBuffer<Game.Prefabs.Effect> dummyEffectBuffer)
        {
            for (int slot = 0; slot < m_SlotInUse.Length; slot++)
            {
                if (!m_SlotInUse[slot]) continue;
                if (HasActiveSlot(slot)) continue;
                if (AnchorHasSlot(anchorBuffer, slot)) continue;

                ReleaseSlot(slot, dummyEffectBuffer);
            }
        }

        /// <summary>
        /// Expire effects whose lifetime has ended.
        /// Reads current valid index from anchor's EnabledEffect buffer
        /// (maintained by CompleteEnabledSystem on swap-back).
        /// Disables the EnabledEffectData entry and enqueues VFXUpdateInfo.Remove.
        /// CompleteEnabledSystem handles actual RemoveAtSwapBack + buffer cleanup.
        /// </summary>
        public void Expire(
            float now,
            NativeList<EnabledEffectData> enabledData,
            NativeQueue<VFXUpdateInfo> vfxQueue,
            DynamicBuffer<EnabledEffect> anchorBuffer,
            DynamicBuffer<Game.Prefabs.Effect> dummyEffectBuffer)
        {
            if (m_ActiveCount == 0) return;

            int removed = 0;

            for (int i = m_ActiveCount - 1; i >= 0; i--)
            {
                float remaining = m_Active[i].ExpiresAt - now;
                if (now < m_Active[i].ExpiresAt)
                {
                    if (remaining < 1.0f && Log.IsDebugEnabled)
                        Log.Debug($"[VFX:DIAG:EXPIRE] Effect {m_Active[i].EffectId} expiring in {remaining:F2}s");
                    continue;
                }

                int effectId = m_Active[i].EffectId;
                int effectSlot = m_Active[i].EffectSlot;

                // CompleteEnabledSystem keeps m_EnabledIndex up to date on swap-back
                int enabledIndex = -1;
                int bufferSlot = -1;
                for (int j = 0; j < anchorBuffer.Length; j++)
                {
                    if (anchorBuffer[j].m_EffectIndex == effectSlot)
                    {
                        enabledIndex = anchorBuffer[j].m_EnabledIndex;
                        bufferSlot = j;
                        break;
                    }
                }

                if (Log.IsDebugEnabled)
                    Log.Debug($"[VFX:DIAG:EXPIRE] Expiring effect {effectId}: effectSlot={effectSlot}, bufferSlot={bufferSlot}, " +
                             $"enabledIndex={enabledIndex}, enabledData.Length={enabledData.Length}, " +
                             $"pos=({m_Active[i].Position.x:F0},{m_Active[i].Position.y:F0},{m_Active[i].Position.z:F0})");

                if (enabledIndex >= 0 && enabledIndex < enabledData.Length)
                {
                    if (Log.IsDebugEnabled)
                    {
                        var preEntry = enabledData[enabledIndex];
                        Log.Debug($"[VFX:DIAG:EXPIRE] PRE-disable flags=0x{(int)preEntry.m_Flags:X}, " +
                                 $"owner={preEntry.m_Owner.Index}:{preEntry.m_Owner.Version}, " +
                                 $"prefab={preEntry.m_Prefab.Index}, intensity={preEntry.m_Intensity:F1}");
                    }

                    // Disable entry: clear IsEnabled, set EnabledUpdated (vanilla Disable pattern)
                    // CompleteEnabledSystem sees !IsEnabled + EnabledUpdated → removes entry + buffer
                    ref var entry = ref enabledData.ElementAt(enabledIndex);
                    entry.m_Flags &= ~EnabledEffectFlags.IsEnabled;
                    entry.m_Flags |= EnabledEffectFlags.EnabledUpdated;

                    vfxQueue.Enqueue(new VFXUpdateInfo
                    {
                        m_Type = VFXUpdateType.Remove,
                        m_EnabledIndex = new int2(enabledIndex, 0)
                    });

                    if (Log.IsDebugEnabled)
                        Log.Debug($"[VFX:DIAG:EXPIRE] POST-disable: sent Remove for enabledIndex={enabledIndex}");
                }
                else if (enabledIndex >= 0)
                {
                    Log.Warn($"[VFX:DIAG:EXPIRE] Index {enabledIndex} OUT OF RANGE (length={enabledData.Length}) " +
                             $"for effect {effectId} — entry already killed; dropping stale active record");
                }
                else
                {
                    Log.Warn($"[VFX:DIAG:EXPIRE] Effect {effectId} NOT FOUND in anchor buffer (len={anchorBuffer.Length}) " +
                             $"— dropping stale active record");
                }

                // Sub-budget decrement (combat types carry no counter).
                if (m_Active[i].Type == ExplosionType.MuzzleFlash && m_MuzzleActiveCount > 0)
                    m_MuzzleActiveCount--;

                int last = m_ActiveCount - 1;
                if (i < last) m_Active[i] = m_Active[last];
                m_ActiveCount--;
                removed++;
            }

            if (removed > 0 && Log.IsDebugEnabled)
                Log.Debug($"[VFX:DIAG:EXPIRE] Expired: {removed}, remaining={m_ActiveCount}");
        }

        /// <summary>
        /// Save-time teardown: the anchor entity is about to be tagged Deleted and
        /// destroyed by vanilla CleanUpSystem in this frame's Cleanup phase. Every
        /// anchor-owned EnabledEffectData entry must be marked Deleted here.
        ///
        /// The ordinary Expire path (clear IsEnabled + set EnabledUpdated, no Deleted
        /// flag) is safe only while the anchor is alive: CompleteEnabledSystem
        /// dereferences m_EffectOwners[m_Owner] for a disabled entry whenever the Deleted
        /// flag is unset. Once the anchor is destroyed those entries become
        /// dangling-owner orphans, and the next RemoveAtSwapBack that lands one in the
        /// fixup slot crashes the vanilla cleanup job (native AV in
        /// CompleteEnabledSystem.EffectCleanupJob) on a later frame — the save-during-
        /// attack crash. Setting the Deleted flag routes removal through the
        /// owner-skipping branch, so the entry is removed without ever touching the
        /// dead anchor's EnabledEffect buffer.
        ///
        /// Channel bookkeeping is cleared too: the post-save anchor recovery
        /// (RecoverMissingAnchor) rebuilds from an empty pool.
        /// </summary>
        public void ForceExpireAllForSave(
            Entity anchor,
            NativeList<EnabledEffectData> enabledData,
            NativeQueue<VFXUpdateInfo> vfxQueue)
        {
            for (int i = 0; i < enabledData.Length; i++)
            {
                ref var entry = ref enabledData.ElementAt(i);
                if (entry.m_Owner != anchor)
                    continue;
                if ((entry.m_Flags & EnabledEffectFlags.Deleted) != 0)
                    continue;

                // Owner-skipping removal: !IsEnabled + EnabledUpdated routes the entry into
                // CompleteEnabledSystem's removal branch; the Deleted flag makes that branch
                // skip the m_EffectOwners[m_Owner] deref against the about-to-die anchor.
                entry.m_Flags &= ~EnabledEffectFlags.IsEnabled;
                entry.m_Flags |= EnabledEffectFlags.EnabledUpdated | EnabledEffectFlags.Deleted;

                vfxQueue.Enqueue(new VFXUpdateInfo
                {
                    m_Type = VFXUpdateType.Remove,
                    m_EnabledIndex = new int2(i, 0)
                });
            }

            m_ActiveCount = 0;
            m_MuzzleActiveCount = 0;
            ClearSlots();
        }

        /// <summary>
        /// Add pending explosion requests to EnabledEffectData and anchor buffer.
        /// Returns metadata about the last added effect for next-frame verification.
        /// </summary>
        public void AddPending(
            float now,
            NativeList<EnabledEffectData> enabledData,
            NativeQueue<VFXUpdateInfo> vfxQueue,
            DynamicBuffer<EnabledEffect> anchorBuffer,
            DynamicBuffer<Game.Prefabs.Effect> dummyEffectBuffer,
            Entity anchor,
            ref int nextEffectId,
            out int addedCount,
            out int lastAddedEffectId,
            out int lastAddedEffectSlot,
            out int lastAddedEnabledIndex,
            out Entity lastAddedPrefab,
            out int lastAddedVfxIndex)
        {
            addedCount = 0;
            lastAddedEffectId = 0;
            lastAddedEffectSlot = -1;
            lastAddedEnabledIndex = -1;
            lastAddedPrefab = Entity.Null;
            lastAddedVfxIndex = -1;

            if (m_Pending.Count == 0) return;

            int dropped = 0;
            int localAdded = 0;
            int localLastId = 0;
            int localLastSlot = -1;
            int localLastIdx = -1;
            Entity localLastPrefab = Entity.Null;
            int localLastVfxIndex = -1;
            int localNextEffectId = nextEffectId;

            bool TryAdd(in ExplosionRequest req)
            {
                if (m_ActiveCount >= MAX_CONCURRENT)
                    return false;

                // Cosmetic sub-budget: high-frequency effects never consume the slots a
                // combat explosion needs. Over-budget muzzle flashes are dropped, combat
                // explosions are not.
                if (req.Type == ExplosionType.MuzzleFlash && m_MuzzleActiveCount >= MAX_CONCURRENT_MUZZLE_FLASH)
                    return false;

                var resolved = ResolveEffect(req.Type);
                if (resolved.Prefab == Entity.Null || resolved.VfxIndex < 0)
                    return false;

                int effectSlot = AllocateSlot();
                if (effectSlot < 0 || effectSlot >= dummyEffectBuffer.Length)
                    return false;

                float scale = GetScale(req.Type);
                float lifetime = GetLifetime(req.Type);
                if (localNextEffectId == int.MaxValue)
                {
                    Log.Warn("[VFX] Effect id counter reached Int32.MaxValue; resetting to 1");
                    localNextEffectId = 1;
                }
                int effectId = localNextEffectId++;

                int idx = enabledData.Length;
                dummyEffectBuffer[effectSlot] = new Game.Prefabs.Effect
                {
                    m_Effect = resolved.Prefab,
                    m_Position = float3.zero,
                    m_Scale = new float3(scale, scale, scale),
                    m_Rotation = quaternion.identity,
                    m_BoneIndex = new int2(-1, -1),
                    m_Intensity = 1f,
                    m_ParentMesh = -1,
                    m_AnimationIndex = -1,
                    m_Procedural = false
                };
                enabledData.Add(new EnabledEffectData
                {
                    m_Owner = anchor,
                    m_Prefab = resolved.Prefab,
                    m_EffectIndex = effectSlot,
                    m_Flags = EnabledEffectFlags.IsEnabled | EnabledEffectFlags.IsVFX,
                    m_Position = req.Position,
                    m_Scale = new float3(scale, scale, scale),
                    m_Rotation = quaternion.identity,
                    m_Intensity = 1f,
                    m_NextTime = 0f
                });

                vfxQueue.Enqueue(new VFXUpdateInfo
                {
                    m_Type = VFXUpdateType.Add,
                    m_EnabledIndex = new int2(idx, 0)
                });

                anchorBuffer.Add(new EnabledEffect
                {
                    m_EffectIndex = effectSlot,
                    m_EnabledIndex = idx
                });

                m_Active[m_ActiveCount] = new ActiveEffect
                {
                    Position = req.Position,
                    Scale = scale,
                    ExpiresAt = now + lifetime,
                    EffectId = effectId,
                    EffectSlot = effectSlot,
                    Type = req.Type,
                    Prefab = resolved.Prefab,
                    VfxIndex = resolved.VfxIndex
                };
                m_ActiveCount++;
                // Sub-budget increment (combat types carry no counter).
                if (req.Type == ExplosionType.MuzzleFlash)
                    m_MuzzleActiveCount++;
                localAdded++;

                localLastId = effectId;
                localLastSlot = effectSlot;
                localLastIdx = idx;
                localLastPrefab = resolved.Prefab;
                localLastVfxIndex = resolved.VfxIndex;

                if (Log.IsDebugEnabled)
                    Log.Debug($"[VFX:DIAG:ADD] #{effectId} {req.Type} at ({req.Position.x:F0},{req.Position.y:F0},{req.Position.z:F0}) " +
                             $"slot={effectSlot} prefab={resolved.Prefab.Index} vfxIndex={resolved.VfxIndex} " +
                             $"scale={scale:F1} life={lifetime:F1}s enabledIdx={idx} bufLen={anchorBuffer.Length} " +
                             $"enabledDataLen={enabledData.Length} frame={UnityEngine.Time.frameCount}");
                return true;
            }

            // Combat explosions (gameplay-meaningful) get the pool first; cosmetic effects
            // (muzzle flashes) fill remaining slots up to their own sub-budget. This
            // guarantees a frame of heavy AA fire can never drop an impact explosion in
            // favour of a flash.
            for (int i = 0; i < m_Pending.Count; i++)
            {
                var req = m_Pending[i];
                if (IsCosmetic(req.Type)) continue;
                if (!TryAdd(req)) dropped++;
            }
            for (int i = 0; i < m_Pending.Count; i++)
            {
                var req = m_Pending[i];
                if (!IsCosmetic(req.Type)) continue;
                if (!TryAdd(req)) dropped++;
            }

            addedCount = localAdded;
            lastAddedEffectId = localLastId;
            lastAddedEffectSlot = localLastSlot;
            lastAddedEnabledIndex = localLastIdx;
            lastAddedPrefab = localLastPrefab;
            lastAddedVfxIndex = localLastVfxIndex;
            nextEffectId = localNextEffectId;

            // FIX M9: Promote drop warning to Warn (was Debug-only — invisible in production)
            if (dropped > 0)
                Log.Warn($"[VFX] DROPPED {dropped} effects (pool full: {m_ActiveCount}/{MAX_CONCURRENT}, muzzle={m_MuzzleActiveCount}/{MAX_CONCURRENT_MUZZLE_FLASH})");
            else if (addedCount > 0 && Log.IsDebugEnabled)
                Log.Debug($"[VFX:DIAG:ADD] Batch: +{addedCount} effects, active={m_ActiveCount}/{MAX_CONCURRENT}");

            m_Pending.Clear();
        }

        private int AllocateSlot()
        {
            for (int i = 0; i < m_SlotInUse.Length; i++)
            {
                if (m_SlotInUse[i]) continue;
                m_SlotInUse[i] = true;
                return i;
            }

            return -1;
        }

        private void ReleaseSlot(int slot, DynamicBuffer<Game.Prefabs.Effect> dummyEffectBuffer)
        {
            if (slot < 0 || slot >= m_SlotInUse.Length) return;

            m_SlotInUse[slot] = false;
            if (slot < dummyEffectBuffer.Length)
                dummyEffectBuffer[slot] = default;
        }

        private void ClearSlots()
        {
            for (int i = 0; i < m_SlotInUse.Length; i++)
                m_SlotInUse[i] = false;
        }

        private bool HasActiveSlot(int slot)
        {
            for (int i = 0; i < m_ActiveCount; i++)
            {
                if (m_Active[i].EffectSlot == slot)
                    return true;
            }

            return false;
        }

        private static bool AnchorHasSlot(DynamicBuffer<EnabledEffect> anchorBuffer, int slot)
        {
            for (int i = 0; i < anchorBuffer.Length; i++)
            {
                if (anchorBuffer[i].m_EffectIndex == slot)
                    return true;
            }

            return false;
        }

        private static float GetScale(ExplosionType type) => type switch
        {
            ExplosionType.DirectHit => SCALE_DIRECT_HIT,
            ExplosionType.Ballistic => SCALE_BALLISTIC,
            ExplosionType.Intercept => SCALE_INTERCEPT,
            ExplosionType.Debris => SCALE_DEBRIS,
            ExplosionType.MuzzleFlash => SCALE_MUZZLE_FLASH,
            ExplosionType.None => 0f,
            _ => WarnUnknownType(type)
        };

        private static float GetLifetime(ExplosionType type) => type switch
        {
            ExplosionType.DirectHit => LIFETIME_DIRECT_HIT,
            ExplosionType.Ballistic => LIFETIME_BALLISTIC,
            ExplosionType.Intercept => LIFETIME_INTERCEPT,
            ExplosionType.Debris => LIFETIME_DEBRIS,
            ExplosionType.MuzzleFlash => LIFETIME_MUZZLE_FLASH,
            ExplosionType.None => 0f,
            _ => WarnUnknownType(type)
        };

        // A type missing from the scale/lifetime tables renders at zero scale for zero
        // seconds — effectively an invisible no-op. Loud, not silent: a new ExplosionType
        // member without table entries is a bug.
        private static float WarnUnknownType(ExplosionType type)
        {
            Log.Warn($"[VFX] ExplosionType.{type} has no scale/lifetime entry — effect is skipped (rendered at zero scale)");
            return 0f;
        }
    }
}
