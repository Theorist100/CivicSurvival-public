using System;
using Unity.Entities;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Events;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Utils;
using CivicSurvival.Core.Attributes;

namespace CivicSurvival.Domains.Economics.Systems
{
    /// <summary>
    /// EDA handler for donor funds events.
    /// Decouples Economics domain from Diplomacy domain.
    ///
    /// Listens to: DonorEvent(FundsReceived)
    /// DonorConferenceSystem owns the durable retained BudgetAddFundsRequest.
    /// This handler remains as a compatibility observer for FundsReceived notifications.
    /// </summary>
    [ActIndependent]
    public partial class DonorFundsHandlerSystem : CivicSystemBase
    {
        private static readonly LogContext Log = new("DonorFundsHandlerSystem");

        protected override void OnCreate()
        {
            base.OnCreate();
            SubscribeRequired<DonorEvent>(OnDonorEvent);
            Log.Info("Created");
        }

        protected override void OnUpdateImpl()
        {
            // No per-frame logic - event-driven only
        }

        protected override void OnDestroy()
        {
            UnsubscribeSafe<DonorEvent>(OnDonorEvent);
            base.OnDestroy();
        }

        private void OnDonorEvent(DonorEvent evt)
        {
            if (!Enabled)
                return;

            if (evt.Type != DonorEventType.FundsReceived)
                return;

            if (evt.Amount <= 0)
            {
                Log.Warn($"Received FundsReceived with invalid amount: {evt.Amount}");
                return;
            }

            var aid = BalanceConfig.Current.Aid;
            long maxGrant = Math.Max(aid.DeepConcernFunds, Math.Max(aid.HeadlinesFunds, aid.GlobalShockFunds));
            if (evt.Amount > maxGrant)
            {
                Log.Warn($"Rejected donor aid amount above configured cap: {evt.Amount:N0} > {maxGrant:N0}");
                return;
            }

            if (Log.IsDebugEnabled)
                Log.Debug($"Observed confirmed donor aid notification: ${evt.Amount:N0}");
        }
    }
}
