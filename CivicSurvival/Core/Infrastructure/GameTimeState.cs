using CivicSurvival.Core.Types.Snapshots;

namespace CivicSurvival.Core.Infrastructure
{
    /// <summary>
    /// Thread-safe game time state holder.
    /// Owned by GameTimeSystem.
    /// </summary>
    public class GameTimeState
    {
        private readonly object m_Lock = new();
        private GameTimeSnapshot m_SnapshotSource = GameTimeSnapshot.NotStarted;

        /// <summary>Current immutable snapshot of game time.</summary>
        public GameTimeSnapshot Current
        {
            get { lock (m_Lock) return m_SnapshotSource; }
        }

        /// <summary>Publish snapshot (called by GameTimeSystem).</summary>
        internal void Publish(GameTimeSnapshot snapshot)
        {
            lock (m_Lock) m_SnapshotSource = snapshot;
        }
    }
}
