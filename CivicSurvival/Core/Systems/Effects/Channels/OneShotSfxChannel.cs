using System.Collections.Generic;
using Game.Effects;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Core.Systems.Effects
{
    /// <summary>
    /// Channel for one-shot SFX entries sent through vanilla AudioManager's temp queue.
    ///
    /// AudioManager's Temp path resolves AudioSourceData from the requested SFX prefab
    /// directly, avoiding the EnabledEffectData EditorContainer flag/component split.
    /// Zero EntityManager.Instantiate, zero main-thread structural changes.
    ///
    /// Requests are written into AudioManager's NativeQueue from a scheduled job
    /// (<see cref="AddTempSfxJob"/>) rather than on the main thread. This is the
    /// canonical vanilla audio pattern (Game.Audio.SFXCullingSystem): the writer job
    /// chains after AudioManager's source-update dependency and registers through
    /// AddSourceUpdateWriter, so AudioManager.OnUpdate (Cleanup phase) waits for it in
    /// its own drain — instead of stalling the main thread with audioDeps.Complete().
    /// </summary>
    internal sealed class OneShotSfxChannel
    {
        private static readonly LogContext Log = new("VanillaVfxSystem");

        public const int MAX_CONCURRENT = 16;
        public const int MAX_PENDING_REQUESTS = MAX_CONCURRENT * 2;

        internal struct SfxRequest
        {
            public float3 Position;
            public Entity Prefab;
        }

        private readonly List<SfxRequest> m_Pending = new();

        public int ActiveCount => 0;
        public int PendingCount => m_Pending.Count;
        public bool IsIdle => m_Pending.Count == 0;

        public void Enqueue(Entity prefab, float3 position)
        {
            if (m_Pending.Count >= MAX_PENDING_REQUESTS)
            {
                Log.Warn($"[SFX] DROPPED request (pending full: {m_Pending.Count}/{MAX_PENDING_REQUESTS})");
                return;
            }

            m_Pending.Add(new SfxRequest { Position = position, Prefab = prefab });
        }

        public void Reset()
        {
            m_Pending.Clear();
        }

        /// <summary>
        /// Drain up to <see cref="MAX_CONCURRENT"/> pending requests into a freshly
        /// allocated NativeArray for <see cref="AddTempSfxJob"/>, clearing the pending
        /// list. Returns an uncreated array (default) when nothing is pending. The
        /// caller owns the returned array and must dispose it on the job handle.
        /// </summary>
        public NativeArray<SfxRequest> DrainPending(Allocator allocator)
        {
            if (m_Pending.Count == 0)
                return default;

            int count = math.min(m_Pending.Count, MAX_CONCURRENT);
            int dropped = m_Pending.Count - count;

            var requests = new NativeArray<SfxRequest>(count, allocator);
            for (int i = 0; i < count; i++)
                requests[i] = m_Pending[i];

            if (dropped > 0)
                Log.Warn($"[SFX] DROPPED {dropped} sounds (per-frame cap: {MAX_CONCURRENT})");
            else if (Log.IsDebugEnabled)
                Log.Debug($"[SFX] Scheduled {count} temp sounds");

            m_Pending.Clear();
            return requests;
        }
    }

    /// <summary>
    /// Writes one-shot SFX requests into AudioManager's temp source queue off the main
    /// thread. Scheduled with the AudioManager source-update dependency as input and
    /// registered via AddSourceUpdateWriter; AudioManager completes it during its
    /// Cleanup-phase queue drain (Game.Audio.AudioManager.OnUpdate).
    /// </summary>
#if ENABLE_BURST
    [Unity.Burst.BurstCompile]
#endif
    internal struct AddTempSfxJob : IJob
    {
        [ReadOnly] public NativeArray<OneShotSfxChannel.SfxRequest> Requests;
        public SourceUpdateData SourceUpdateData;

        public void Execute()
        {
            for (int i = 0; i < Requests.Length; i++)
            {
                var req = Requests[i];
                SourceUpdateData.AddTemp(req.Prefab, new Game.Objects.Transform
                {
                    m_Position = req.Position,
                    m_Rotation = quaternion.identity
                });
            }
        }
    }
}
