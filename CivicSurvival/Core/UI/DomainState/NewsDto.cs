using CivicSurvival.Core.UI;

namespace CivicSurvival.Core.UI.DomainState
{
    /// <summary>
    /// Global news domain DTO.
    /// Note: Some fields (online stats, connection) are updated from event handlers.
    /// The system must track state in fields and rebuild JSON in OnPanelUpdate.
    /// </summary>
    public partial struct NewsDto : IDomainDto
    {
        public int GlobalOnlineNow;
        public int GlobalOnlineHour;
        public int GlobalOnlineToday;
        public int GlobalOnlineTotal;
        public bool GlobalConnected;
        public string GlobalConnectionStatus;
        public bool NetworkConnectionEnabled;
        public string PlayerNickname;
        public string NicknameRequestJson;
        // Monthly nickname change budget left (max 3); -1 = unknown (not yet reported by server).
        public int NicknameChangesRemaining;
        // True once the player has ever set a nickname (first set is free, beyond the budget).
        public bool NicknameInitialized;
        // True once an Online consent decision has been recorded globally (ConsentStore).
        // The settings consent modal triggers only on the FIRST enable — i.e. when this is
        // false and the player moves the master to ON. After any decision it stays true.
        public bool OnlineConsentRecorded;
    }
}
