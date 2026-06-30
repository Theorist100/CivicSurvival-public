using Game;
using Game.Simulation;
using Unity.Entities;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Components.Requests;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.UI;
using CivicSurvival.Core.Utils;
using CivicSurvival.Core.Systems.Scheduling;

namespace CivicSurvival.Core.Systems
{
    /// <summary>
    /// Bridges terminal RequestResultEvent entities into UI-facing RequestResultBridge state.
    /// </summary>
    // Reads RequestResultEvent — must run after the post-load purge so a stale
    // pre-save terminal result is never re-collected / re-toasted on load
    // (CIVIC415 / W2 row 171, requestresultevent-stale-ttl).
    [ActIndependent]
    public partial class RequestResultCollectorSystem : CivicSystemBase
    {
        private static readonly LogContext Log = new("RequestResultCollector");

        private ModCleanupBarrier m_ModCleanupBarrier = null!;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_ModCleanupBarrier = World.GetOrCreateSystemManaged<ModCleanupBarrier>();
            Log.Info("Created");
        }

        protected override void OnUpdateImpl()
        {
            EntityCommandBuffer ecb = default;
            bool ecbCreated = false;

            foreach (var (result, entity) in
                SystemAPI.Query<RefRO<RequestResultEvent>>()
                    .WithNone<Reported>()
                    .WithEntityAccess())
            {
                var value = result.ValueRO;
                if (value.Status == RequestStatus.Pending)
                    Log.Warn($"Ignoring pending RequestResultEvent kind={value.Kind}, requestId={value.RequestId}");
                else
                {
                    bool mapped = RequestResultBridge.Complete(value, out bool published);
                    if (!mapped && value.Kind != RequestKind.Unknown)
                        Log.Warn($"Unmapped RequestResultEvent kind={value.Kind}, requestId={value.RequestId}");

                    if (published && value.Status == RequestStatus.Failed)
                        RequestResultBridge.PublishRejectToast(value);
                }

                if (!ecbCreated)
                {
                    ecb = m_ModCleanupBarrier.CreateCommandBuffer();
                    ecbCreated = true;
                }
                ecb.AddComponent<Reported>(entity);
            }

            if (ecbCreated)
                m_ModCleanupBarrier.AddJobHandleForProducer(Dependency);
        }
    }
}
