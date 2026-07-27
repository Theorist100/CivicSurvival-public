using System;
using System.Reflection;
using Game.Effects;
using Game.Simulation;
using Unity.Collections;
using Unity.Entities;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Core.Systems.Effects
{
    /// <summary>
    /// Debug-only diagnostics for VanillaVfxSystem.
    ///
    /// All public entry points early-out when debug logging is disabled, so production
    /// builds pay no cost beyond the gate check.
    ///
    /// Capabilities:
    /// - Heartbeat counter while effects are active
    /// - Next-frame survival verification (was effect killed by EffectControlJob?)
    /// - PRE/POST state dump of enabledData + anchor buffer
    /// - Reflection peek into VFXSystem.m_Effects[vfxIndex] internal state
    /// </summary>
    internal sealed class VanillaVfxDiagnostics
    {
        private static readonly LogContext Log = new("VanillaVfxSystem");
        private const int HEARTBEAT_INTERVAL = 60;

        private int m_VerifyFrame;
        private int m_VerifyEffectId;
        private int m_VerifyEffectSlot;
        private int m_VerifyEnabledIndex;
        private Entity m_VerifyPrefab;
        private int m_VerifyVfxIndex = -1;
#pragma warning disable CIVIC367 // Diagnostic-only counter, stale value after load has no gameplay impact
        private int m_FramesSinceLastAdd;
#pragma warning restore CIVIC367

        public void Reset()
        {
            // FIX M8: Reset diagnostic verify frame — prevents spurious "NOT FOUND" warning in Debug mode
            m_VerifyFrame = 0;
            m_VerifyEffectId = 0;
            m_VerifyEffectSlot = -1;
            m_VerifyEnabledIndex = 0;
            m_VerifyPrefab = Entity.Null;
            m_VerifyVfxIndex = -1;
            m_FramesSinceLastAdd = 0;
        }

        /// <summary>
        /// Periodic heartbeat: emits a Debug log every HEARTBEAT_INTERVAL frames while
        /// effects are active, so timeline gaps are visible in the log.
        /// </summary>
        public void TickFrame(int currentFrame, int activeCount)
        {
            if (activeCount > 0)
            {
                m_FramesSinceLastAdd++;
                if (m_FramesSinceLastAdd % HEARTBEAT_INTERVAL == 0 && Log.IsDebugEnabled)
                {
                    Log.Debug($"[VFX:DIAG:HEARTBEAT] {m_FramesSinceLastAdd}f since last add, " +
                             $"activeCount={activeCount}, frame={currentFrame}");
                }
            }
        }

        /// <summary>
        /// Schedule a next-frame verification of the most recently added effect.
        /// Resets heartbeat counter.
        /// </summary>
        public void OnEffectAdded(int currentFrame, int effectId, int effectSlot, int enabledIndex, Entity prefab, int vfxIndex)
        {
            m_VerifyFrame = currentFrame + 1;
            m_VerifyEffectId = effectId;
            m_VerifyEffectSlot = effectSlot;
            m_VerifyEnabledIndex = enabledIndex;
            m_VerifyPrefab = prefab;
            m_VerifyVfxIndex = vfxIndex;
            m_FramesSinceLastAdd = 0;
        }

        /// <summary>
        /// Log the full state of enabledData entries owned by our anchor.
        /// Caller must have already called lookup.Update(system).
        /// </summary>
        public void LogState(
            string phase,
            NativeList<EnabledEffectData> enabledData,
            DynamicBuffer<EnabledEffect> anchorBuffer,
            int activeCount,
            int pendingCount)
        {
            if (!Log.IsDebugEnabled) return;

            Log.Debug($"[VFX:DIAG:{phase}] enabledData.Length={enabledData.Length}, " +
                     $"anchorBuffer.Length={anchorBuffer.Length}, " +
                     $"activeCount={activeCount}, pending={pendingCount}, " +
                     $"frame={UnityEngine.Time.frameCount}");

            for (int i = 0; i < anchorBuffer.Length; i++)
            {
                var be = anchorBuffer[i];
                string entryInfo;
                if (be.m_EnabledIndex >= 0 && be.m_EnabledIndex < enabledData.Length)
                {
                    var ed = enabledData[be.m_EnabledIndex];
                    entryInfo = $"flags={ed.m_Flags}, owner={ed.m_Owner.Index}:{ed.m_Owner.Version}, " +
                                $"prefab={ed.m_Prefab.Index}, pos=({ed.m_Position.x:F0},{ed.m_Position.y:F0},{ed.m_Position.z:F0}), " +
                                $"intensity={ed.m_Intensity:F1}, edEffectIdx={ed.m_EffectIndex}";
                }
                else
                {
                    entryInfo = $"INDEX OUT OF RANGE (enabledData.Length={enabledData.Length})";
                }
                Log.Debug($"[VFX:DIAG:{phase}]   buf[{i}]: effectSlot={be.m_EffectIndex}, enabledIdx={be.m_EnabledIndex} → {entryInfo}");
            }
        }

        /// <summary>
        /// Run next-frame verification if one is due. Re-acquires enabledData (read-only)
        /// and the anchor buffer through the supplied lookup.
        /// </summary>
        public void RunVerifyIfDue(
            SystemBase host,
            int currentFrame,
            Entity anchor,
            EffectControlSystem ecs,
            VFXSystem vfxSystem,
            ref BufferLookup<EnabledEffect> lookup)
        {
            if (m_VerifyFrame == 0 || currentFrame < m_VerifyFrame) return;
            int scheduledFrame = m_VerifyFrame;
            m_VerifyFrame = 0;

            if (!Log.IsDebugEnabled) return;

            var enabledData = ecs.GetEnabledData(readOnly: true, out var deps);
            deps.Complete();
            lookup.Update(host);

            // CIVIC035: anchor entity guaranteed to have EnabledEffect buffer (we created it)
#pragma warning disable CIVIC035
            var buffer = lookup[anchor];
#pragma warning restore CIVIC035

            int framesDelta = currentFrame - (scheduledFrame - 1);
            Log.Debug($"[VFX:VERIFY] +{framesDelta}f after add: enabledData.Length={enabledData.Length}, " +
                     $"anchorBuffer.Length={buffer.Length}");

            int currentEnabledIndex = -1;
            bool foundInBuffer = false;
            for (int i = 0; i < buffer.Length; i++)
            {
                if (buffer[i].m_EffectIndex != m_VerifyEffectSlot) continue;

                foundInBuffer = true;
                int eidx = buffer[i].m_EnabledIndex;
                currentEnabledIndex = eidx;
                if (eidx >= 0 && eidx < enabledData.Length)
                {
                    var ed = enabledData[eidx];
                    bool isEnabled = (ed.m_Flags & EnabledEffectFlags.IsEnabled) != 0;
                    bool enabledUpdated = (ed.m_Flags & EnabledEffectFlags.EnabledUpdated) != 0;
                    bool isVfx = (ed.m_Flags & EnabledEffectFlags.IsVFX) != 0;
                    bool ownerMatch = ed.m_Owner == anchor;
                    bool prefabMatch = ed.m_Prefab == m_VerifyPrefab;

                    Log.Debug($"[VFX:VERIFY] Effect {m_VerifyEffectId} FOUND at slot={m_VerifyEffectSlot}, enabledIdx={eidx}: " +
                             $"IsEnabled={isEnabled}, EnabledUpdated={enabledUpdated}, IsVFX={isVfx}, " +
                             $"ownerMatch={ownerMatch}, prefabMatch={prefabMatch}, " +
                             $"flags=0x{(int)ed.m_Flags:X}, " +
                             $"pos=({ed.m_Position.x:F0},{ed.m_Position.y:F0},{ed.m_Position.z:F0})");

                    if (!isEnabled)
                        Log.Warn($"[VFX:VERIFY] *** EFFECT DISABLED! EffectControlJob likely killed it (WrongPrefab) ***");
                    if (enabledUpdated)
                        Log.Warn($"[VFX:VERIFY] *** EnabledUpdated SET — entry marked for removal by CompleteEnabledSystem ***");
                    if (!ownerMatch)
                        Log.Warn($"[VFX:VERIFY] *** Owner MISMATCH: expected anchor {anchor.Index}:{anchor.Version}, got {ed.m_Owner.Index}:{ed.m_Owner.Version} ***");
                    if (!prefabMatch)
                        Log.Warn($"[VFX:VERIFY] *** Prefab MISMATCH: expected {m_VerifyPrefab.Index}:{m_VerifyPrefab.Version}, got {ed.m_Prefab.Index}:{ed.m_Prefab.Version} ***");
                }
                else
                {
                    Log.Warn($"[VFX:VERIFY] Effect {m_VerifyEffectId} slot={m_VerifyEffectSlot} in buffer but enabledIdx={eidx} OUT OF RANGE (len={enabledData.Length})");
                }
                break;
            }

            if (!foundInBuffer)
            {
                Log.Warn($"[VFX:VERIFY] *** Effect {m_VerifyEffectId} slot={m_VerifyEffectSlot} NOT FOUND in anchor buffer! " +
                         $"Was at enabledIdx={m_VerifyEnabledIndex}. " +
                         $"Buffer has {buffer.Length} entries. Entry may have expired or been removed. ***");

                if (m_VerifyEnabledIndex >= 0 && m_VerifyEnabledIndex < enabledData.Length)
                {
                    var ed = enabledData[m_VerifyEnabledIndex];
                    Log.Debug($"[VFX:VERIFY] enabledData[{m_VerifyEnabledIndex}] now has: " +
                             $"owner={ed.m_Owner.Index}:{ed.m_Owner.Version}, prefab={ed.m_Prefab.Index}, " +
                             $"flags=0x{(int)ed.m_Flags:X}, effectIdx={ed.m_EffectIndex}");
                }
                else
                {
                    Log.Debug($"[VFX:VERIFY] enabledData[{m_VerifyEnabledIndex}] NO LONGER EXISTS (len={enabledData.Length})");
                }
            }

            CheckVfxSystemState(vfxSystem, currentEnabledIndex);
        }

        /// <summary>
        /// Low-level reflection fetch of VFXSystem.m_Effects[vfxIndex] internals.
        /// Shared by the verify-path REFLECT dump and the one-shot exhaust sampler.
        /// </summary>
        private static bool TryReflectEffectInfo(
            VFXSystem vfxSystem,
            int vfxIndex,
            out NativeParallelHashMap<int, int> indices,
            out int lastCount,
            out UnityEngine.VFX.VisualEffect? visualEffect,
            out object? effectInfo,
            out string error)
        {
            indices = default;
            lastCount = -1;
            visualEffect = null;
            effectInfo = null;
            error = string.Empty;

            if (vfxIndex < 0)
            {
                error = "vfxIndex not set";
                return false;
            }

#pragma warning disable S3011 // Reflection on private field — required for vanilla VFX diagnostics (no public API)
            var effectsField = typeof(VFXSystem).GetField("m_Effects", BindingFlags.NonPublic | BindingFlags.Instance);
#pragma warning restore S3011
            if (effectsField == null)
            {
                error = "Can't find m_Effects field on VFXSystem";
                return false;
            }

            var effectsArray = effectsField.GetValue(vfxSystem) as Array;
            if (effectsArray == null || vfxIndex >= effectsArray.Length)
            {
                error = $"m_Effects null or vfxIndex {vfxIndex} >= length {effectsArray?.Length}";
                return false;
            }

            effectInfo = effectsArray.GetValue(vfxIndex);
            var effectInfoType = effectInfo.GetType();
            var indicesField = effectInfoType.GetField("m_Indices");
            var lastCountField = effectInfoType.GetField("m_LastCount");
            var visualEffectField = effectInfoType.GetField("m_VisualEffect");
            if (indicesField == null || lastCountField == null || visualEffectField == null)
            {
                error = "Can't find EffectInfo fields";
                return false;
            }

            indices = (NativeParallelHashMap<int, int>)indicesField.GetValue(effectInfo);
            lastCount = (int)lastCountField.GetValue(effectInfo);
            visualEffect = visualEffectField.GetValue(effectInfo) as UnityEngine.VFX.VisualEffect;
            return true;
        }

        /// <summary>
        /// Use reflection to inspect VFXSystem.m_Effects[vfxIndex] internal state.
        /// Checks if our enabledIndex was added to m_Indices and whether the underlying
        /// VisualEffect is rendering particles.
        /// </summary>
        private void CheckVfxSystemState(VFXSystem vfxSystem, int currentEnabledIndex)
        {
            try
            {
                if (!TryReflectEffectInfo(vfxSystem, m_VerifyVfxIndex, out var indices, out int lastCount,
                        out var visualEffect, out object? effectInfo, out string error))
                {
                    Log.Warn($"[VFX:DIAG:REFLECT] {error}");
                    return;
                }

                int indexCount = indices.IsCreated ? indices.Count() : -1;
                bool hasOriginal = indices.IsCreated && indices.ContainsKey(m_VerifyEnabledIndex);
                bool hasCurrent = currentEnabledIndex >= 0 && indices.IsCreated && indices.ContainsKey(currentEnabledIndex);
                int aliveParticles = visualEffect != null ? visualEffect.aliveParticleCount : -1;
                bool isPlaying = visualEffect != null && visualEffect.HasAnySystemAwake();
                bool isCulled = visualEffect != null && visualEffect.culled;
                var vfxPos = visualEffect != null ? visualEffect.transform.position : UnityEngine.Vector3.zero;
                var vfxBounds = visualEffect != null ? visualEffect.GetComponent<UnityEngine.Renderer>()?.bounds : (UnityEngine.Bounds?)null;

                bool veEnabled = visualEffect != null && visualEffect.enabled;
                bool goActive = visualEffect != null && visualEffect.gameObject.activeInHierarchy;
                bool hasAsset = visualEffect != null && visualEffect.visualEffectAsset != null;
                string assetName = hasAsset ? visualEffect!.visualEffectAsset!.name : "NULL";
                int countProp = -1;
                bool pause = false;
                try
                {
                    if (visualEffect != null)
                    {
                        countProp = visualEffect.GetInt(UnityEngine.Shader.PropertyToID("Count"));
                        pause = visualEffect.pause;
                    }
                }
                catch (Exception ex) { if (Log.IsDebugEnabled) Log.Debug($"VFX property read failed: {ex.Message}"); }

                Log.Debug($"[VFX:DIAG:REFLECT] VFXSystem.m_Effects[{m_VerifyVfxIndex}]: " +
                         $"indices.Count={indexCount}, lastCount={lastCount}, " +
                         $"hasOriginalIdx({m_VerifyEnabledIndex})={hasOriginal}, " +
                         $"hasCurrentIdx({currentEnabledIndex})={hasCurrent}, " +
                         $"aliveParticles={aliveParticles}, isPlaying={isPlaying}, " +
                         $"CULLED={isCulled}, vfxPos=({vfxPos.x:F0},{vfxPos.y:F0},{vfxPos.z:F0}), " +
                         $"veEnabled={veEnabled}, goActive={goActive}, pause={pause}, " +
                         $"asset={assetName}, CountProp={countProp}");
                if (vfxBounds.HasValue)
                {
                    var b = vfxBounds.Value;
                    Log.Debug($"[VFX:DIAG:REFLECT] Renderer bounds: center=({b.center.x:F0},{b.center.y:F0},{b.center.z:F0}), " +
                             $"size=({b.size.x:F0},{b.size.y:F0},{b.size.z:F0})");
                }
                else
                {
                    Log.Debug("[VFX:DIAG:REFLECT] No Renderer component on VisualEffect GameObject");
                }

                if (!hasOriginal && !hasCurrent)
                {
                    var instancesField = effectInfo!.GetType().GetField("m_Instances");
                    if (instancesField != null)
                    {
                        var instances = (NativeArray<int>)instancesField.GetValue(effectInfo);
                        Log.Warn($"[VFX:DIAG:REFLECT] *** OUR ENTRY NOT IN VFXSystem! " +
                                 $"VFXSystem never received our Add OR index was stale. " +
                                 $"instances.Length={instances.Length}, indices.Count={indexCount} ***");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"[VFX:DIAG:REFLECT] Reflection failed: {ex}");
            }
        }
    }
}
