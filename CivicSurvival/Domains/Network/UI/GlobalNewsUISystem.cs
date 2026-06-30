using CivicSurvival.Core.Events;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.UI;
using CivicSurvival.Core.UI.DomainState;
using CivicSurvival.Core.Utils;
using CivicSurvival.Domains.Network.Events;
using CivicSurvival.Domains.Network.Services;
using static CivicSurvival.Core.UI.B;
using CivicSurvival.Core.Attributes;

namespace CivicSurvival.Domains.Network.UI
{
    /// <summary>
    /// UI system for Global News.
    /// Exposes online stats and connection status to UI.
    ///
    /// Migrated from GlobalNewsUIPanel → CivicUIPanelSystem.
    /// Gains: proper ECS lifecycle. EventBus subscriptions in OnCreate/OnDestroy.
    /// </summary>
    [ActIndependent]
    [HandlesRequestKind(RequestKind.NicknameUpdate)]
    public partial class GlobalNewsUISystem : CivicUIPanelSystem
    {
        private ModSettings? m_Settings;

        // Event-driven state (updated from event handlers, serialized in OnPanelUpdate)
        private int m_OnlineNow;
        private int m_OnlineHour;
        private int m_OnlineToday;
        private int m_OnlineTotal;
        private bool m_Connected;
        private string m_ConnectionStatus = "Disconnected";
        private int m_NicknameChangesRemaining;
        private bool m_NicknameInitialized;
        private string m_LastJson = string.Empty;

        // Cached "an Online consent decision has been recorded" flag. Latches true once the
        // global ConsentStore file exists (after the first toggle) and never goes back, so
        // the file is stat'd only until the first decision instead of every 500ms tick.
        private bool m_OnlineConsentRecorded;

        protected override void OnCreate()
        {
            base.OnCreate();

            SubscribeRequired<OnlineStatsUpdatedEvent>(OnStatsUpdated);
            SubscribeRequired<GlobalConnectionChangedEvent>(OnConnectionChanged);
            SubscribeRequired<ToggleGlobalConnectionCommand>(OnToggleConnectionCommand);
            SubscribeRequired<NicknameBudgetUpdatedEvent>(OnNicknameBudgetUpdated);

            Log.Info("Created");
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            m_Settings ??= ServiceRegistry.Instance.Require<ModSettings>();
            // Seed the latch from the global store: existing players who already chose
            // Online (file present) must not see the first-enable consent modal again.
            if (!m_OnlineConsentRecorded)
                m_OnlineConsentRecorded = Core.Services.ConsentStore.Exists(Core.Services.ConsentKey.OnlineConnection);
        }

        protected override void ConfigureBindings()
        {
            var sb = DomainJsonHelper.GetBuilder();
            new NewsDto().WriteTo(sb);
            m_LastJson = sb.ToString();
            Bindings.Add<string>(NewsState, m_LastJson);

            var newsFeed = ServiceRegistry.Instance.Require<NewsFeedService>();
            newsFeed.RegisterBinding(binding => AddUpdateBinding(binding));
        }

        protected override void ConfigureTriggers()
        {
            Triggers.Add<bool>(ToggleGlobalConnection, FeatureIds.Network, OnToggleConnection);
            Triggers.Add<string>(SetPlayerNickname, FeatureIds.Network, RequestResultBridge.Nickname, OnSetNickname);
        }

        private void OnToggleConnection(bool enable)
        {
            Log.Info($"Toggle global connection: {enable}");

            EventBus?.SafePublish(new ToggleGlobalConnectionCommand(enable), "GlobalNewsUISystem");
        }

        private TriggerOutcome OnSetNickname(string nickname)
        {
            if (string.IsNullOrEmpty(nickname))
            {
                Log.Info("Nickname cleared");
                return QueueNicknameRequest(string.Empty);
            }
            var validation = NameFilter.Validate(nickname);
            if (!validation.IsValid)
            {
                Log.Warn($"Invalid nickname rejected: {validation.Error}");
                var reason = validation.Error.Contains("length", System.StringComparison.OrdinalIgnoreCase)
                    || validation.Error.Contains("exceed", System.StringComparison.OrdinalIgnoreCase)
                    ? ReasonIds.NicknameInvalidLength
                    : ReasonIds.NicknameInvalidChars;
                return TriggerOutcome.Reject(reason);
            }

            Log.Info($"Nickname update requested: {nickname}");

            return QueueNicknameRequest(nickname);
        }

        private TriggerOutcome QueueNicknameRequest(string nickname)
        {
            return TriggerOutcome.Pending(token =>
            {
                EventBus?.SafePublish(
                    new SetNicknameCommand(nickname, token.RequestId, World.Time.ElapsedTime),
                    "GlobalNewsUISystem");
                return true;
            }, ReasonIds.NicknameServerUnavailable);
        }

        protected override void OnPanelUpdate()
        {
            var dto = new NewsDto
            {
                GlobalOnlineNow = m_OnlineNow,
                GlobalOnlineHour = m_OnlineHour,
                GlobalOnlineToday = m_OnlineToday,
                GlobalOnlineTotal = m_OnlineTotal,
                GlobalConnected = m_Connected,
                GlobalConnectionStatus = m_ConnectionStatus,
                NetworkConnectionEnabled = m_Settings != null && m_Settings.NetworkConnectionEnabled,
                PlayerNickname = m_Settings?.PlayerNickname ?? "",
                NicknameRequestJson = RequestResultBridge.Get(RequestResultBridge.Nickname).ToJson(),
                NicknameChangesRemaining = m_NicknameChangesRemaining,
                NicknameInitialized = m_NicknameInitialized,
                OnlineConsentRecorded = m_OnlineConsentRecorded
            };

            var sb = DomainJsonHelper.GetBuilder();
            dto.WriteTo(sb);
            string json = sb.ToString();
            if (json != m_LastJson)
            {
                m_LastJson = json;
                PublishJsonWhenComplete(NewsState, NoSourceChecks, () => json);
            }
        }

        private void OnStatsUpdated(OnlineStatsUpdatedEvent evt)
        {
            m_OnlineNow = evt.Stats.OnlineNow;
            m_OnlineHour = evt.Stats.OnlineHour;
            m_OnlineToday = evt.Stats.OnlineToday;
            m_OnlineTotal = evt.Stats.TotalPlayers;
        }

        private void OnConnectionChanged(GlobalConnectionChangedEvent evt)
        {
            m_Connected = evt.IsConnected;
            m_ConnectionStatus = evt.Message;
        }

        private void OnNicknameBudgetUpdated(NicknameBudgetUpdatedEvent evt)
        {
            m_NicknameChangesRemaining = evt.ChangesRemaining;
            m_NicknameInitialized = evt.Initialized;
        }

        private void OnToggleConnectionCommand(ToggleGlobalConnectionCommand cmd)
        {
            m_Connected = cmd.Enable && m_Connected;
            m_ConnectionStatus = cmd.Enable ? m_ConnectionStatus : "Disconnected by user";
            // Any toggle (on OR off) records a consent decision in the global store
            // (GlobalNewsSystem.OnToggleConnection writes it). Latch so the first-enable
            // consent modal does not re-trigger after the player has chosen once.
            m_OnlineConsentRecorded = true;
        }

        protected override void OnDestroy()
        {
            UnsubscribeSafe<OnlineStatsUpdatedEvent>(OnStatsUpdated);
            UnsubscribeSafe<GlobalConnectionChangedEvent>(OnConnectionChanged);
            UnsubscribeSafe<ToggleGlobalConnectionCommand>(OnToggleConnectionCommand);
            UnsubscribeSafe<NicknameBudgetUpdatedEvent>(OnNicknameBudgetUpdated);

            base.OnDestroy();
        }
    }
}
