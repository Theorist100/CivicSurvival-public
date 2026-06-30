using CivicSurvival.Core.Attributes;
using Game;

namespace CivicSurvival.Services.Arena
{
    /// <summary>
    /// Drains Arena refresh completions during UIUpdate so paused gameplay still
    /// delivers terminal UI state.
    /// </summary>
    [ActIndependent]
    public partial class ArenaRefreshCompletionPumpSystem : GameSystemBase
    {
        private ArenaLeaderboardSystem? m_LeaderboardSystem;

        protected override void OnUpdate()
        {
            m_LeaderboardSystem ??= World.GetExistingSystemManaged<ArenaLeaderboardSystem>();
            m_LeaderboardSystem?.PumpRefreshCompletions();
        }
    }
}
