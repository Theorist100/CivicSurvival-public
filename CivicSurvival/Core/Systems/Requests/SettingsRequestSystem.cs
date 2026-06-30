using Game;
using Game.Simulation;
using Unity.Entities;
using CivicSurvival.Core.Components.Domain.Power;
using CivicSurvival.Core.Components.Requests;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Events;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Systems.Scheduling;

using CivicSurvival.Core.Services;
namespace CivicSurvival.Core.Systems.Requests
{
    /// <summary>
    /// Processes simple settings requests: AutoDispatch toggle, BackupPolicy.
    /// Thin set/toggle operations without complex business logic.
    ///
    /// Flow:
    /// 1. UI creates entity with XxxRequest (Status=Pending)
    /// 2. This system reads singleton, applies change, destroys entity
    ///
    /// Note: internet mode is applied synchronously by CognitiveStateSystem (sole owner of CognitiveState).
    ///
    /// Uses RequireForUpdate - zero overhead when no requests pending.
    /// </summary>
    [ActIndependent]
    [HandlesRequestKind(RequestKind.AutoDispatchToggle)]
    [HandlesRequestKind(RequestKind.BackupPolicy)]
    [TransientConsumerReconcile(typeof(AutoDispatchToggleRequest), ReconcileMode.ExplicitlyLossyAndSafe,
        Justification = "Transient UI command: settings are changed only inside this consumer, so pre-consume load loss has no durable side-effect.")]
    [TransientConsumerReconcile(typeof(BackupPolicyRequest), ReconcileMode.ExplicitlyLossyAndSafe,
        Justification = "Transient UI command: backup policy changes only inside this consumer, so pre-consume load loss is reissuable.")]
    public partial class SettingsRequestSystem : CivicSystemBase
    {
        private static readonly LogContext Log = new("SettingsRequestSystem");

        private EntityQuery m_AutoDispatchQuery;
        private EntityQuery m_BackupPolicyQuery;
        private ModCleanupBarrier m_ModCleanupBarrier = null!;

        // Auto-dispatch writer for restore side-effect
        private IAutoDispatchSettingsWriter? m_AutoDispatchSettings;
        private IBackupPowerPolicyWriter? m_BackupPolicyWriter;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_AutoDispatchQuery = GetEntityQuery(
                ComponentType.ReadWrite<AutoDispatchToggleRequest>()
            );
            m_BackupPolicyQuery = GetEntityQuery(
                ComponentType.ReadWrite<BackupPolicyRequest>()
            );

            // Wake only when any request exists
            RequireAnyForUpdate(m_AutoDispatchQuery, m_BackupPolicyQuery);

            m_ModCleanupBarrier = World.GetOrCreateSystemManaged<ModCleanupBarrier>();

            Log.Info("Created");
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            m_AutoDispatchSettings ??= ServiceRegistry.Instance.Require<IAutoDispatchSettingsWriter>();
            m_BackupPolicyWriter ??= ServiceRegistry.Instance.Require<IBackupPowerPolicyWriter>();
        }

        protected override void OnUpdateImpl()
        {
            EntityCommandBuffer ecb = default;
            bool ecbCreated = false;
            DestroyRequestsWithoutMeta(ref ecb, ref ecbCreated);

            // R3-D-2: Defense-in-depth act guard. UI prevents access during PreWar,
            // but guard against stale/crafted request entities reaching this system.
#pragma warning disable CIVIC070 // Act guard — reads on user request, 1-frame lag invisible
            bool hasActSingleton = SystemAPI.TryGetSingleton<CurrentActSingleton>(out var actSingleton);
            if (!hasActSingleton || actSingleton.CurrentAct < Act.Crisis)
            {
                // Destroy leaked request entities to prevent indefinite persistence (S24-A1)
                // FIX H100: Set Status=Failed before destroy so UI gets notified
                foreach (var (meta, entity) in SystemAPI.Query<RefRO<RequestMeta>>().WithAll<AutoDispatchToggleRequest>().WithEntityAccess())
                {
                    EnsureEcb(ref ecb, ref ecbCreated);
                    RequestResultEmitter.Emit(ecb, meta.ValueRO, RequestKind.AutoDispatchToggle, RequestStatus.Failed, ReasonIds.SettingsAvailableFromCrisis, SystemAPI.Time.ElapsedTime);
                    ecb.DestroyEntity(entity);
                }
                foreach (var (meta, entity) in SystemAPI.Query<RefRO<RequestMeta>>().WithAll<BackupPolicyRequest>().WithEntityAccess())
                {
                    EnsureEcb(ref ecb, ref ecbCreated);
                    RequestResultEmitter.Emit(ecb, meta.ValueRO, RequestKind.BackupPolicy, RequestStatus.Failed, ReasonIds.SettingsAvailableFromCrisis, SystemAPI.Time.ElapsedTime);
                    ecb.DestroyEntity(entity);
                }
                if (ecbCreated)
                    m_ModCleanupBarrier.AddJobHandleForProducer(Dependency);
                return;
            }
#pragma warning restore CIVIC070

            ProcessAutoDispatchRequests(ref ecb, ref ecbCreated);
            ProcessBackupPolicyRequests(ref ecb, ref ecbCreated);

            if (ecbCreated)
                m_ModCleanupBarrier.AddJobHandleForProducer(Dependency);
        }

#pragma warning disable CIVIC145 // Lazy helper: every call site writes immediately after EnsureEcb returns.
        private void EnsureEcb(ref EntityCommandBuffer ecb, ref bool ecbCreated)
        {
            if (ecbCreated)
                return;

            ecb = m_ModCleanupBarrier.CreateCommandBuffer();
            ecbCreated = true;
        }
#pragma warning restore CIVIC145

        private void DestroyRequestsWithoutMeta(ref EntityCommandBuffer ecb, ref bool ecbCreated)
        {
            foreach (var (_, entity) in
                SystemAPI.Query<RefRO<AutoDispatchToggleRequest>>()
                    .WithNone<RequestMeta>()
                    .WithEntityAccess())
            {
                Log.Warn("Destroyed AutoDispatchToggleRequest without RequestMeta");
                EnsureEcb(ref ecb, ref ecbCreated);
                ecb.DestroyEntity(entity);
            }

            foreach (var (_, entity) in
                SystemAPI.Query<RefRO<BackupPolicyRequest>>()
                    .WithNone<RequestMeta>()
                    .WithEntityAccess())
            {
                Log.Warn("Destroyed BackupPolicyRequest without RequestMeta");
                EnsureEcb(ref ecb, ref ecbCreated);
                ecb.DestroyEntity(entity);
            }
        }

        private void ProcessAutoDispatchRequests(ref EntityCommandBuffer ecb, ref bool ecbCreated)
        {
            foreach (var (meta, entity) in
                SystemAPI.Query<RefRO<RequestMeta>>()
                .WithAll<AutoDispatchToggleRequest>()
                .WithEntityAccess())
            {
                var status = RequestStatus.Success;
                var reasonId = ReasonId.None;
                if (m_AutoDispatchSettings != null && m_AutoDispatchSettings.TryToggleAutoDispatch(out var enabled, out var restored))
                {
                    foreach (int idx in restored)
                        EventBus?.SafePublish(new DistrictStateChangedEvent(idx));
                    Log.Info($"AutoDispatch toggled: {enabled}");
                }
                else
                {
                    status = RequestStatus.Failed;
                    reasonId = ReasonIds.AutoDispatchStateUnavailable;
                    Log.Warn("AutoDispatchToggleRequest failed: singleton not found");
                }

                if (status == RequestStatus.Success)
                {
                    EnsureEcb(ref ecb, ref ecbCreated);
                    RequestResultEmitter.EmitSuccess(ecb, meta.ValueRO, RequestKind.AutoDispatchToggle, SystemAPI.Time.ElapsedTime);
                }
                else
                {
                    EnsureEcb(ref ecb, ref ecbCreated);
                    RequestResultEmitter.Emit(ecb, meta.ValueRO, RequestKind.AutoDispatchToggle, status, reasonId, SystemAPI.Time.ElapsedTime);
                }
                ecb.DestroyEntity(entity);
            }
        }

        private void ProcessBackupPolicyRequests(ref EntityCommandBuffer ecb, ref bool ecbCreated)
        {
            foreach (var (request, meta, entity) in
                SystemAPI.Query<RefRO<BackupPolicyRequest>, RefRO<RequestMeta>>()
                .WithEntityAccess())
            {
                var status = RequestStatus.Success;
                var reasonId = ReasonId.None;
                if (m_BackupPolicyWriter != null && m_BackupPolicyWriter.TrySetBackupPolicy(request.ValueRO.Policy))
                {
                    Log.Info($"Backup policy set to: {request.ValueRO.Policy}");
                }
                else
                {
                    status = RequestStatus.Failed;
                    reasonId = ReasonIds.BackupPolicyStateUnavailable;
                    Log.Warn("BackupPolicyRequest failed: singleton not found");
                }

                if (status == RequestStatus.Success)
                {
                    EnsureEcb(ref ecb, ref ecbCreated);
                    RequestResultEmitter.EmitSuccess(ecb, meta.ValueRO, RequestKind.BackupPolicy, SystemAPI.Time.ElapsedTime);
                }
                else
                {
                    EnsureEcb(ref ecb, ref ecbCreated);
                    RequestResultEmitter.Emit(ecb, meta.ValueRO, RequestKind.BackupPolicy, status, reasonId, SystemAPI.Time.ElapsedTime);
                }
                ecb.DestroyEntity(entity);
            }
        }
    }
}
