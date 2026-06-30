using CivicSurvival.Core.Components.Requests;
using CivicSurvival.Core.Components.Domain.Countermeasures;
using CivicSurvival.Core.Components.Lifecycle;
using CivicSurvival.Core.Interfaces.Domain.Economy;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.UI.DomainState;
using CivicSurvival.Core.Utils;
using Game;
using Unity.Entities;
using CivicSurvival.Core.Systems.Scheduling;

namespace CivicSurvival.Core.Services.Countermeasures
{
    /// <summary>
    /// Wallet integration adapter for Countermeasures domain.
    /// Encapsulates all wallet side-effects: deduct, freeze, unfreeze, confiscate.
    ///
    /// Caller: CountermeasuresUpdateSystem, CmChoiceProcessor (sole callers)
    /// </summary>
    public sealed class CmWalletOps
    {
        private static readonly LogContext Log = new("Countermeasures");

        private readonly IShadowWalletService m_Wallet;
        private readonly GameSimulationEndBarrier m_Barrier;
        private readonly World m_World;

        public CmWalletOps(IShadowWalletService walletService, GameSimulationEndBarrier barrier, World world)
        {
            m_Wallet = walletService ?? NullShadowWalletService.Instance;
            m_Barrier = barrier;
            m_World = world;
        }

        public long GetBalance() => m_Wallet.GetWalletSnapshot().Balance;

        public bool IsFrozen => m_Wallet.GetWalletSnapshot().IsFrozen;

        public ActionAvailabilityField ResolveAction(ActionKey key, Act currentAct, long proposedCost)
        {
            var ctx = new ActionContext(
                false,
                GamePhase.Calm,
                true,
                currentAct);

            var snapshot = m_Wallet.GetWalletSnapshot();
            if (snapshot.Exists)
            {
                ctx = ctx.WithWalletState(
                    snapshot.IsFrozen,
                    snapshot.Balance,
                    snapshot.SanctionsMarkup);
            }

            return ActionGate.Resolve(key, ctx.WithCost(proposedCost));
        }

        public bool TryQueueRetainedDeduct(
            int amount,
            in RequestMeta requestMeta,
            out Entity entity,
            out long effectiveCost)
        {
            var ecb = m_Barrier.CreateCommandBuffer();
            if (!CanDeduct(amount, out _))
            {
                entity = Entity.Null;
                effectiveCost = 0;
                return false;
            }

            entity = ecb.QueuePendingOperation(new CountermeasureBribeIntent
            {
                Kind = CountermeasureBribeIntent.InvestigationKind
            });
            if (!BudgetEmitter.TryQueueDeductOnEntity(
                m_World,
                ecb,
                entity,
                amount,
                BudgetCategory.ShadowOps,
                BudgetPriority.PlayerAction,
                "CountermeasuresBribe:Investigation",
                out effectiveCost,
                requestMeta,
                BudgetResultMode.RetainResult))
            {
                ecb.DestroyEntity(entity);
                entity = Entity.Null;
                return false;
            }

            return true;
        }

        private bool CanDeduct(int amount, out long effectiveCost)
        {
            effectiveCost = 0;
            if (amount <= 0)
                return false;

            // Single-writer: CanAffordWithPending internalizes IsFrozen + SanctionsMarkup + Pending.
            var result = m_Wallet.CanAffordWithPending(amount);
            effectiveCost = result.EffectiveCost;
            return result.Affordable;
        }

        /// <summary>
        /// Deduct bypassing freeze check. Used for police bribe — wallet IS frozen during police phase,
        /// but bribe is the player's escape mechanism from freeze.
        /// Direct deduct (not ECB) because ProcessDeductRequests checks IsFrozen and would reject.
        /// </summary>
        public bool TryDeductBypassFreeze(int amount, out long remainingBalance)
            => TryDeductBypassFreeze(amount, out remainingBalance, out _);

        public bool TryDeductBypassFreeze(int amount, out long remainingBalance, out long effectiveCost)
        {
            remainingBalance = 0;
            effectiveCost = 0;
            if (amount <= 0) return false;

            var affordability = m_Wallet.CanAffordBypassFreeze(amount);
            if (!affordability.Affordable)
            {
                remainingBalance = GetBalance();
                return false;
            }
            effectiveCost = affordability.EffectiveCost;

            // Direct deduct — bypasses ECB pipeline where TryDeduct would reject due to IsFrozen.
            // CanAffordBypassFreeze already validated balance. DeductBypassFreeze writes via singleton.
#pragma warning disable CIVIC237 // TOCTOU safe: main-thread single writer, no yield between check and deduct
            if (!m_Wallet.DeductBypassFreeze(effectiveCost, "PoliceBribe"))
#pragma warning restore CIVIC237
            {
                remainingBalance = GetBalance();
                return false;
            }

            remainingBalance = GetBalance();
            return true;
        }

        public bool CanAffordBypassFreeze(int amount)
        {
            if (amount <= 0) return false;
            return m_Wallet.CanAffordBypassFreeze(amount).Affordable;
        }

        public void Freeze()
        {
            var ecb = m_Barrier.CreateCommandBuffer();
            var requestEntity = ecb.CreateEntity();
            ecb.AddComponent(requestEntity, new ShadowWalletControlRequest
            {
                Type = ShadowWalletControlType.Freeze,
                FreezeReason = FreezeReason.PoliceInvestigation
            });
            RequestMetaWriter.AddInternal(ecb, requestEntity, nameof(ShadowWalletControlRequest), nameof(ShadowWalletControlType.Freeze));
            Log.Info("[Countermeasures] Wallet freeze requested (police investigation)");
        }

        public void Unfreeze()
        {
            var ecb = m_Barrier.CreateCommandBuffer();
            var requestEntity = ecb.CreateEntity();
            ecb.AddComponent(requestEntity, new ShadowWalletControlRequest
            {
                Type = ShadowWalletControlType.Unfreeze,
                FreezeReason = FreezeReason.PoliceInvestigation
            });
            RequestMetaWriter.AddInternal(ecb, requestEntity, nameof(ShadowWalletControlRequest), nameof(ShadowWalletControlType.Unfreeze));
            Log.Info("[Countermeasures] Wallet unfreeze requested (police investigation cleared)");
        }

        public static void UnfreezeViaEcb(EntityCommandBuffer ecb)
        {
            var requestEntity = ecb.CreateEntity();
            ecb.AddComponent(requestEntity, new ShadowWalletControlRequest
            {
                Type = ShadowWalletControlType.Unfreeze,
                FreezeReason = FreezeReason.PoliceInvestigation
            });
            RequestMetaWriter.AddInternal(ecb, requestEntity, nameof(ShadowWalletControlRequest), nameof(ShadowWalletControlType.Unfreeze));
        }

        public static void ConfiscateViaEcb(EntityCommandBuffer ecb)
        {
            var requestEntity = ecb.CreateEntity();
            ecb.AddComponent(requestEntity, new ShadowWalletControlRequest
            {
                Type = ShadowWalletControlType.Confiscate,
                FreezeReason = FreezeReason.Confiscated
            });
            RequestMetaWriter.AddInternal(ecb, requestEntity, nameof(ShadowWalletControlRequest), nameof(ShadowWalletControlType.Confiscate));
        }
    }
}
