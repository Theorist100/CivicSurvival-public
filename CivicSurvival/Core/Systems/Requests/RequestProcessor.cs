using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Components.Requests;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.Components.Threats;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Systems;
using CivicSurvival.Core.Systems.Scheduling;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.UI.DomainState;
using Game;
using Unity.Collections;
using Unity.Entities;

namespace CivicSurvival.Core.Systems.Requests
{
    public interface IFireAndForgetRequestProcessor<TRequest>
        where TRequest : unmanaged, IComponentData
    {
        void ApplyFireAndForget(in TRequest request, EntityCommandBuffer ecb);
    }

    public abstract partial class EmittingRequestProcessor<TRequest> : CivicSystemBase
        where TRequest : unmanaged, IComponentData
    {
        private GameSimulationEndBarrier m_GameSimulationEndBarrier = null!;
        private EntityQuery m_RequestQuery;
        private EntityQuery m_WaveStateQuery;
        private EntityQuery m_CurrentActQuery;
        private ComponentLookup<RequestMeta> m_RequestMetaLookup;
        private ComponentLookup<TRequest> m_RequestLookup;

        protected abstract ActionKey ActionKey { get; }
        public abstract RequestKind RequestKind { get; }

        protected sealed override void OnCreate()
        {
            base.OnCreate();

            m_GameSimulationEndBarrier = World.GetOrCreateSystemManaged<GameSimulationEndBarrier>();
            m_RequestQuery = GetEntityQuery(
                ComponentType.ReadOnly<TRequest>(),
                ComponentType.ReadOnly<RequestMeta>());
            m_WaveStateQuery = GetEntityQuery(ComponentType.ReadOnly<WaveStateSingleton>());
            m_CurrentActQuery = GetEntityQuery(ComponentType.ReadOnly<CurrentActSingleton>());
            m_RequestMetaLookup = GetComponentLookup<RequestMeta>(isReadOnly: true);
            m_RequestLookup = GetComponentLookup<TRequest>(isReadOnly: true);

            _ = ActionKey;
            _ = RequestKind;
            RequireForUpdate(m_RequestQuery);
            OnCreateProcessor();
        }

        protected virtual void OnCreateProcessor() { }

        [CompletesDependency("RequestProcessor.OnUpdateImpl: request entities are transient and processed in small batches; ToEntityArray materialises the batch for sequential validation with sub-tick-safe ECB playback via GameSimulationEndBarrier")]
        protected sealed override void OnUpdateImpl()
        {
            if (m_RequestQuery.IsEmpty)
                return;

            EntityCommandBuffer ecb = default;
            bool ecbCreated = false;
            m_RequestMetaLookup.Update(this);
            m_RequestLookup.Update(this);

            var entities = m_RequestQuery.ToEntityArray(Allocator.Temp);
            try
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    var entity = entities[i];
                    if (!m_RequestMetaLookup.HasComponent(entity) || !m_RequestLookup.HasComponent(entity))
                        continue;

                    var meta = m_RequestMetaLookup[entity];
                    var request = m_RequestLookup[entity];
                    if (ShouldRunActionGate(in request))
                    {
                        var ctx = BuildActionContext(in request);
                        var gate = ActionGate.Resolve(ResolveActionKey(in request), ctx);

                        if (!gate.CanRun)
                        {
                            if (!ecbCreated)
                            {
                                ecb = m_GameSimulationEndBarrier.CreateCommandBuffer();
                                ecbCreated = true;
                            }
                            RequestResultEmitter.Emit(ecb, meta, RequestKind, RequestStatus.Failed, ReasonId.FromRuntime(gate.LockedReasonId), World.Time.ElapsedTime);
                            ecb.DestroyEntity(entity);
                            continue;
                        }
                    }

                    var status = Apply(in request, in meta, ref ecb, ref ecbCreated, out var reasonId);
                    switch (status)
                    {
                        case RequestStatus.Success:
                            if (!ecbCreated)
                            {
                                ecb = m_GameSimulationEndBarrier.CreateCommandBuffer();
                                ecbCreated = true;
                            }
                            RequestResultEmitter.EmitSuccess(ecb, meta, RequestKind, World.Time.ElapsedTime);
                            break;
                        case RequestStatus.Pending:
                            // Pending keeps the entity alive intentionally so the next tick re-attempts.
                            continue;
                        default:
                            if (!ecbCreated)
                            {
                                ecb = m_GameSimulationEndBarrier.CreateCommandBuffer();
                                ecbCreated = true;
                            }
                            RequestResultEmitter.Emit(ecb, meta, RequestKind, status, ReasonId.FromRuntime(reasonId), World.Time.ElapsedTime);
                            break;
                    }
                    ecb.DestroyEntity(entity);
                }
            }
            finally
            {
                if (entities.IsCreated)
                    entities.Dispose();
            }

            if (ecbCreated)
                m_GameSimulationEndBarrier.AddJobHandleForProducer(Dependency);
        }

        protected abstract RequestStatus Apply(
            in TRequest request,
            in RequestMeta meta,
            ref EntityCommandBuffer ecb,
            ref bool ecbCreated,
            out string reasonId);

#pragma warning disable CIVIC145 // Lazy helper: base processor and subclasses write immediately after EnsureEcb returns.
        protected void EnsureEcb(ref EntityCommandBuffer ecb, ref bool ecbCreated)
        {
            if (ecbCreated)
                return;

            ecb = m_GameSimulationEndBarrier.CreateCommandBuffer();
            ecbCreated = true;
        }
#pragma warning restore CIVIC145

        protected virtual ActionKey ResolveActionKey(in TRequest request) => ActionKey;

        protected virtual bool ShouldRunActionGate(in TRequest request) => true;

        protected virtual ActionContext BuildActionContext(in TRequest request) => BuildActionContext();

        private ActionContext BuildActionContext()
        {
            bool hasWaveState = m_WaveStateQuery.TryGetSingleton<WaveStateSingleton>(out var waveState);
            bool hasActSingleton = m_CurrentActQuery.TryGetSingleton<CurrentActSingleton>(out var actSingleton);
            return new ActionContext(
                hasWaveState,
                hasWaveState ? waveState.CurrentPhase : GamePhase.Calm,
                hasActSingleton,
                hasActSingleton ? actSingleton.CurrentAct : Act.PreWar);
        }
    }

    public abstract partial class FireAndForgetRequestProcessor<TRequest> : CivicSystemBase, IFireAndForgetRequestProcessor<TRequest>
        where TRequest : unmanaged, IComponentData
    {
        public abstract void ApplyFireAndForget(in TRequest request, EntityCommandBuffer ecb);
    }
}
