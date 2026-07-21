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
            Bindings.Add<int>(ArenaYourScore, -1);
            Bindings.Add<int>(ArenaPendingScore, 0);
            Bindings.Add<string>(ArenaYourRankTier, "");
            Bindings.Add<int>(ArenaAllTimeDelta, 0);
            Bindings.Add<int>(ArenaWeeklyDelta, 0);
            Bindings.Add<string>(ArenaShadowLeaderboard, "[]");
            Bindings.Add<string>(ArenaShadowWeekly, "[]");
            Bindings.Add<string>(ArenaShadowRankTiers, "[]");
            Bindings.Add<int>(ArenaShadowYourPosition, -1);
            Bindings.Add<int>(ArenaShadowYourWeeklyPosition, -1);
            // Net is a real value that can be negative; the UI reads
            // ArenaShadowRankTier being non-empty as "server confirmed".
            Bindings.Add<int>(ArenaShadowNet, 0);
            Bindings.Add<int>(ArenaShadowPending, 0);
            Bindings.Add<string>(ArenaShadowRankTier, "");
            Bindings.Add<string>(ArenaProsperityLeaderboard, "[]");
            Bindings.Add<string>(ArenaProsperityWeekly, "[]");
            Bindings.Add<string>(ArenaProsperityRankTiers, "[]");
            Bindings.Add<int>(ArenaProsperityYourPosition, -1);
            Bindings.Add<int>(ArenaProsperityYourWeeklyPosition, -1);
            Bindings.Add<int>(ArenaProsperityScore, -1);
            Bindings.Add<int>(ArenaProsperityPending, 0);
            Bindings.Add<string>(ArenaProsperityRankTier, "");
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
            Bindings.Update(ArenaYourScore, ClampToInt(m_LeaderboardSystem.YourConfirmedScore));
            Bindings.Update(ArenaPendingScore, ClampToInt(m_LeaderboardSystem.PendingScore));
            Bindings.Update(ArenaYourRankTier, m_LeaderboardSystem.YourRankTier);
            Bindings.Update(ArenaAllTimeDelta, m_LeaderboardSystem.AllTimePositionDelta);
            Bindings.Update(ArenaWeeklyDelta, m_LeaderboardSystem.WeeklyPositionDelta);

            Bindings.Update(ArenaShadowLeaderboard, m_LeaderboardSystem.GetShadowJson());
            Bindings.Update(ArenaShadowWeekly, m_LeaderboardSystem.GetShadowWeeklyJson());
            Bindings.Update(ArenaShadowRankTiers, m_LeaderboardSystem.GetShadowRankTiersJson());
            Bindings.Update(ArenaShadowYourPosition, m_LeaderboardSystem.ShadowYourPosition ?? -1);
            Bindings.Update(ArenaShadowYourWeeklyPosition, m_LeaderboardSystem.ShadowYourWeeklyPosition ?? -1);
            Bindings.Update(ArenaShadowNet, ClampToInt(m_LeaderboardSystem.ShadowNet));
            Bindings.Update(ArenaShadowPending, ClampToInt(m_LeaderboardSystem.ShadowPending));
            Bindings.Update(ArenaShadowRankTier, m_LeaderboardSystem.ShadowRankTier);

            Bindings.Update(ArenaProsperityLeaderboard, m_LeaderboardSystem.GetProsperityJson());
            Bindings.Update(ArenaProsperityWeekly, m_LeaderboardSystem.GetProsperityWeeklyJson());
            Bindings.Update(ArenaProsperityRankTiers, m_LeaderboardSystem.GetProsperityRankTiersJson());
            Bindings.Update(ArenaProsperityYourPosition, m_LeaderboardSystem.ProsperityYourPosition ?? -1);
            Bindings.Update(ArenaProsperityYourWeeklyPosition, m_LeaderboardSystem.ProsperityYourWeeklyPosition ?? -1);
            Bindings.Update(ArenaProsperityScore, ClampToInt(m_LeaderboardSystem.ProsperityConfirmedScore));
            Bindings.Update(ArenaProsperityPending, ClampToInt(m_LeaderboardSystem.ProsperityPending));
            Bindings.Update(ArenaProsperityRankTier, m_LeaderboardSystem.ProsperityRankTier);

            Bindings.Update(ArenaLastRefreshResult, RequestResultBridge.Get(RequestResultBridge.ArenaRefresh).ToJson());
#pragma warning restore CIVIC108
        }

        private static int ClampToInt(long value)
        {
            // Clamp makes the long→int narrowing provably safe (CIVIC136).
            // Full int range on purpose: shadow net (income − confiscations) is
            // legitimately negative for a player whose wallet was seized.
            return (int)System.Math.Min(System.Math.Max(value, int.MinValue), int.MaxValue);
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
