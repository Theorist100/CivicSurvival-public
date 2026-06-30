using CivicSurvival.Core.Attributes;

namespace CivicSurvival.Core.Infrastructure
{
    /// <summary>
    /// Single source of truth for transient threat pipeline generations.
    /// The stamp rides Shahed/Ballistic -> arrival -> impact/debris and answers only:
    /// "does this transient belong to the currently loaded world generation?"
    ///
    /// Unlike <see cref="ActEpochClock"/>, this clock does not advance on narrative
    /// act transitions. In-flight threats are allowed to cross PreWar -> Crisis or
    /// later act changes; save/load and explicit reset boundaries still invalidate
    /// preserved transient leftovers.
    /// </summary>
    [InfrastructureService]
    public sealed class ThreatGenerationClock
    {
        /// <summary>Sentinel for a never-stamped transient. Invalid — fail loud.</summary>
        public const int Unstamped = 0;

        /// <summary>Current transient generation. Valid baseline = 1.</summary>
        public int Current { get; private set; } = 1;

        /// <summary>
        /// Advance on load/reset boundaries so pre-load transient leftovers become
        /// stale regardless of their stamped value.
        /// </summary>
        public int AdvanceForLoadBoundary() => ++Current;
    }
}
