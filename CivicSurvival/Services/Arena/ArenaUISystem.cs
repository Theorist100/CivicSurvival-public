using System.Linq;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.UI;
using static CivicSurvival.Core.UI.B;
using CivicSurvival.Core.Attributes;

namespace CivicSurvival.Services.Arena
{
    /// <summary>
    /// UI system for Arena Leaderboard.
    /// Exposes leaderboard data to React UI.
    ///
    /// Migrated from ArenaUIPanel → CivicUIPanelSystem.
    /// Gains: proper ECS lifecycle. System reference resolved in OnCreate.
    /// </summary>
    [ActIndependent]
    [HandlesRequestKind(RequestKind.ArenaRefresh)]
    public partial class ArenaUISystem : CivicUIPanelSystem
    {
        private ArenaLeaderboardSystem? m_LeaderboardSystem;

        protected override void OnCreate()
        {
            base.OnCreate();
            Log.Info("Created");
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            // Arena can be closed by feature gates; keep bindings renderable and
            // let the guarded update/trigger paths below report unavailable state.
            m_LeaderboardSystem ??= FeatureRegistry.Instance.Query<ArenaLeaderboardSystem>();
        }

        protected override void ConfigureBindings()
        {
            Bindings.Add<string>(ArenaLeaderboard, "[]");
            Bindings.Add<string>(ArenaWeekly, "[]");
            Bindings.Add<string>(ArenaRankTiers, "[]");
            Bindings.Add<int>(ArenaYourPosition, -1);
            Bindings.Add<int>(ArenaYourWeeklyPosition, -1);
            Bindings.Add<string>(ArenaLastRefreshResult, RequestResultBridge.Get(RequestResultBridge.ArenaRefresh).ToJson());
        }

        protected override void ConfigureTriggers()
        {
            Triggers.Add(RefreshArenaLeaderboard, FeatureIds.ArenaUI, RequestResultBridge.ArenaRefresh, OnRefreshLeaderboard);
        }

        protected override void OnPanelUpdate()
        {
#pragma warning disable CIVIC256 // Resolved in OnCreate via GetOrCreate — null practically impossible
            if (m_LeaderboardSystem == null)
                return;
#pragma warning restore CIVIC256

#pragma warning disable CIVIC108 // Intentional: null-guarded above, system has no .Enabled toggle
            Bindings.Update(ArenaLeaderboard, m_LeaderboardSystem.GetLeaderboardJson());
            Bindings.Update(ArenaWeekly, m_LeaderboardSystem.GetWeeklyJson());
            Bindings.Update(ArenaRankTiers, m_LeaderboardSystem.GetRankTiersJson());

            Bindings.Update(ArenaYourPosition, m_LeaderboardSystem.YourPosition ?? -1);
            Bindings.Update(ArenaYourWeeklyPosition, m_LeaderboardSystem.YourWeeklyPosition ?? -1);
            Bindings.Update(ArenaLastRefreshResult, RequestResultBridge.Get(RequestResultBridge.ArenaRefresh).ToJson());
#pragma warning restore CIVIC108
        }

        private TriggerOutcome OnRefreshLeaderboard()
        {
            // Network availability is enforced upstream: ArenaUI is dep-skipped when Arena
            // dep-skips, and ArenaFeature.Gate = RequiresFeature("Network"). If this method
            // is reachable, Network is open.

#pragma warning disable CIVIC108 // CanRefresh is the leaderboard owner's runtime availability contract.
            if (m_LeaderboardSystem == null || !m_LeaderboardSystem.CanRefresh)
#pragma warning restore CIVIC108
                return TriggerOutcome.Reject(ReasonIds.ArenaRefreshTelemetryDisabled);

#pragma warning disable CIVIC108 // IsRefreshInFlight is the leaderboard owner's refresh lifecycle contract.
            if (m_LeaderboardSystem.IsRefreshInFlight)
#pragma warning restore CIVIC108
                return TriggerOutcome.Reject(ReasonIds.ArenaRefreshInflight);

            Log.Info("Manual refresh requested");
            return TriggerOutcome.Pending(token =>
            {
#pragma warning disable CIVIC108 // Null-checked by accepted path above
                return m_LeaderboardSystem != null && m_LeaderboardSystem.ForceRefresh(token.RequestId);
#pragma warning restore CIVIC108
            }, ReasonIds.ArenaRefreshTelemetryDisabled);
        }
    }
}
