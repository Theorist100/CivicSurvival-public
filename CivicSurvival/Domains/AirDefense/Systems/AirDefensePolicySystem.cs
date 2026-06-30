using Game;
using Unity.Entities;
using CivicSurvival.Core.Components.Domain.AirDefense;
using CivicSurvival.Core.Components.Requests;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Systems;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Utils;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces.Services;
using System.Threading;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Systems.Scheduling;

using CivicSurvival.Core.Services;
namespace CivicSurvival.Domains.AirDefense.Systems
{
    /// <summary>
    /// Manages Air Defense policy.
    ///
    /// Responsibilities:
    /// - Process DefensePolicyRequest entities (Data-Driven Commands)
    /// - Expose CurrentPolicy as IDefensePolicyReader for cross-domain reads (no singleton needed)
    ///
    /// SRP: Policy management only, no targeting logic.
    /// </summary>
    [ActIndependent]
    [HandlesRequestKind(RequestKind.DefensePolicy)]
    [TransientConsumerReconcile(typeof(DefensePolicyRequest), ReconcileMode.ExplicitlyLossyAndSafe,
        Justification = "Transient UI command: the persisted policy changes only after this consumer runs, so a load before processing leaves no partial durable side-effect.")]
    public partial class AirDefensePolicySystem : CivicSystemBase, IDefensePolicyReader, IResettable
    {
        // ECB command counter (encapsulated to avoid CA2211)
        private static int s_EcbCommandCount;
        public static int EcbCommandCount => Volatile.Read(ref s_EcbCommandCount);
        public static void ResetCounters() => Interlocked.Exchange(ref s_EcbCommandCount, 0);
        private static void IncrementEcbCount() => Interlocked.Increment(ref s_EcbCommandCount);

        private static readonly LogContext Log = new("AirDefensePolicySystem");

        private GameSimulationEndBarrier m_GameSimulationEndBarrier = null!;
        private EntityQuery m_PolicyRequestQuery;
        // Policy A: this field is persisted only through AirDefensePolicyCodec. The
        // AirDefenseCreditsSingleton.CurrentPolicy is a non-persisted projection written
        // synchronously by AirDefenseStateSystem. The old m_DeserializeSucceeded /
        // ValidateAfterLoad singleton-policy reconcile (S005b) is deleted — there is no
        // competing persisted copy to reconcile against.
        private DefensePolicy m_CurrentPolicy = DefensePolicy.HumanitarianShield;

        // IDefensePolicyReader — cross-domain read without singleton
        public DefensePolicy CurrentPolicy => m_CurrentPolicy;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_GameSimulationEndBarrier = World.GetOrCreateSystemManaged<GameSimulationEndBarrier>();
            m_PolicyRequestQuery = GetEntityQuery(ComponentType.ReadOnly<DefensePolicyRequest>());

            // Policy A: AirDefenseCreditsSingleton is owned by AirDefenseStateSystem
            // ([SingletonOwner] + ICivicSingletonOwner). EnsureExists must NOT be called
            // here — a non-owner creating it in OnCreate (before the saved entity is
            // deserialized) is exactly what produced the duplicate-singleton load bug.

            // Producer-side registration MUST happen in OnCreate, not OnStartRunning:
            // OnStartRunning fires only on first Update, which never arrives if
            // GameSimulation phase never ticks (e.g. UI consumer in MainMenu hits us first).
            ServiceRegistry.Instance.Register<IDefensePolicyReader>(this);

            Log.Info("Created");
        }

        protected override void OnStopRunning()
        {
            // Do NOT unregister here — service must stay registered between request batches.
            // OnStopRunning fires whenever RequireForUpdate disables the system → availability gap.
            base.OnStopRunning();
        }

        protected override void OnDestroy()
        {
            // Instance-aware: skips if new world already re-registered during world reload.
            if (ServiceRegistry.IsInitialized)
                ServiceRegistry.Instance.Unregister<IDefensePolicyReader>(this);
            base.OnDestroy();
        }

        protected override void OnUpdateImpl()
        {
            if (m_PolicyRequestQuery.IsEmpty) return;
            ProcessDefensePolicyRequests();
        }

        /// <summary>
        /// Process DefensePolicyRequest ephemeral entities.
        /// Data-Driven Commands pattern - UI creates entity, system processes.
        /// </summary>
        private void ProcessDefensePolicyRequests()
        {
            EntityCommandBuffer ecb = default;
            bool hasEcb = false;

            foreach (var (request, meta, entity) in
                SystemAPI.Query<RefRO<DefensePolicyRequest>, RefRO<RequestMeta>>()
                .WithEntityAccess())
            {
                if (!hasEcb) { ecb = m_GameSimulationEndBarrier.CreateCommandBuffer(); hasEcb = true; }

                SetDefensePolicy(request.ValueRO.Policy);
                RequestResultEmitter.EmitSuccess(ecb, meta.ValueRO, RequestKind.DefensePolicy, SystemAPI.Time.ElapsedTime);
                if (Log.IsDebugEnabled) Log.Debug($"Processed DefensePolicyRequest: {request.ValueRO.Policy}");
                ecb.DestroyEntity(entity);
                IncrementEcbCount();
            }

            if (hasEcb) m_GameSimulationEndBarrier.AddJobHandleForProducer(Dependency);
        }

        public void SetDefensePolicy(DefensePolicy policy)
        {
            if (m_CurrentPolicy != policy)
            {
                m_CurrentPolicy = policy;
                Log.Info($"Defense policy changed to: {policy}");
            }
        }

        // ============================================================================
        // STATE MANAGEMENT (for serialization)
        // ============================================================================

        public void ResetState()
        {
            // Owner internal state never holds the cross-domain null-object
            // sentinel. Restore the business default explicitly — symmetric with
            // AirDefenseCreditsSingleton.Default and the field initializer.
            m_CurrentPolicy = DefensePolicy.HumanitarianShield;
            // Policy A: credits singleton is owned by AirDefenseStateSystem (its
            // ResetState/OnLoadRestore handle EnsureExists). No EnsureExists here.
            Log.Info("State reset");
        }
    }
}
