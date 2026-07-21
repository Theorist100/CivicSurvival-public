using Game;
using Unity.Entities;
using CivicSurvival.Core.Components.Domain.AirDefense;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Systems;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Utils;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Attributes;

using CivicSurvival.Core.Services;
namespace CivicSurvival.Domains.AirDefense.Systems
{
    /// <summary>
    /// Manages Air Defense policy.
    ///
    /// Responsibilities:
    /// - Apply the player's policy selection synchronously (IDefensePolicyCommandService,
    ///   pause-safe AXIOM 14 route 3 — the UI callback applies before returning)
    /// - Expose CurrentPolicy as IDefensePolicyReader for cross-domain reads (no singleton needed)
    ///
    /// SRP: Policy management only, no targeting logic.
    /// </summary>
    [ActIndependent]
    public partial class AirDefensePolicySystem : CivicSystemBase, IDefensePolicyReader, IDefensePolicyCommandService, IResettable
    {
        private static readonly LogContext Log = new("AirDefensePolicySystem");

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

            // Policy A: AirDefenseCreditsSingleton is owned by AirDefenseStateSystem
            // ([SingletonOwner] + ICivicSingletonOwner). EnsureExists must NOT be called
            // here — a non-owner creating it in OnCreate (before the saved entity is
            // deserialized) is exactly what produced the duplicate-singleton load bug.

            // Producer-side registration MUST happen in OnCreate, not OnStartRunning:
            // OnStartRunning fires only on first Update, which never arrives if
            // GameSimulation phase never ticks (e.g. UI consumer in MainMenu hits us first).
            ServiceRegistry.Instance.Register<IDefensePolicyReader>(this);
            ServiceRegistry.Instance.Register<IDefensePolicyCommandService>(this);

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
            {
                ServiceRegistry.Instance.Unregister<IDefensePolicyReader>(this);
                ServiceRegistry.Instance.Unregister<IDefensePolicyCommandService>(this);
            }
            base.OnDestroy();
        }

        protected override void OnUpdateImpl()
        {
            // Policy selection is applied synchronously via IDefensePolicyCommandService;
            // no per-frame request drain remains.
        }

        /// <summary>
        /// IDefensePolicyCommandService — sync apply on the UI thread (pause-safe).
        /// </summary>
        public void SetDefensePolicyImmediate(DefensePolicy policy) => SetDefensePolicy(policy);

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
