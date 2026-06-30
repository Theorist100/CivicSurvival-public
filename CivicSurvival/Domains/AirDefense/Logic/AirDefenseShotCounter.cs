using System.Threading;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Domains.AirDefense.Logic
{
    /// <summary>
    /// Per-AAType shot counts drained in one flush. Sums to <see cref="Total"/>.
    /// Indexed by <c>(int)AAType</c>; length matches the AAType enum.
    /// </summary>
    public readonly struct AirDefenseShotsByType
    {
        public const int TypeCount = 4;

        private readonly int m_Heritage;
        private readonly int m_Bofors;
        private readonly int m_Gepard;
        private readonly int m_Patriot;

        public AirDefenseShotsByType(int heritage, int bofors, int gepard, int patriot)
        {
            m_Heritage = heritage;
            m_Bofors = bofors;
            m_Gepard = gepard;
            m_Patriot = patriot;
        }

        public int Total => m_Heritage + m_Bofors + m_Gepard + m_Patriot;

        public int Get(AAType type) => type switch
        {
            AAType.HeritageBofors => m_Heritage,
            AAType.Bofors40mm => m_Bofors,
            AAType.Gepard => m_Gepard,
            AAType.PatriotSAM => m_Patriot,
            _ => 0
        };
    }

    /// <summary>
    /// Thread-safe shot counters shared by AA producers (FireControlExecutor)
    /// and ballistic producers (BallisticDefenseSystem). Drained by
    /// AirDefenseShotStatsFlushSystem — the single writer of DebriefingShotStats.
    ///
    /// Extracted from AirDefenseOrchestrator: counters are a cross-system
    /// producer-consumer queue, not orchestration state. BDS and the flush
    /// system no longer depend on ADO just to push/drain shots.
    ///
    /// Counters are kept per AAType so the UI stats cache can fall in step with
    /// each type's own ammo total during a wave (Patriot missiles vs Bofors rounds),
    /// not just the merged AaAmmo number.
    /// </summary>
    internal static class AirDefenseShotCounter
    {
#pragma warning disable CIVIC031 // Cleared via Reset(); static needed for cross-system accumulation
        private static readonly int[] s_AAShotsPending = new int[AirDefenseShotsByType.TypeCount];
#pragma warning restore CIVIC031

        /// <summary>FCE (via ADO) adds AA shots of one firing type to the per-frame counter.</summary>
        public static void AddAAShots(AAType type, int count) =>
            Interlocked.Add(ref s_AAShotsPending[(int)type], count);

        /// <summary>
        /// BDS adds ballistic shots to the per-frame counter. Ballistic interception is
        /// Patriot-only (the sole AA type that engages ballistics), so the shot is booked
        /// against Patriot ammo.
        /// </summary>
        public static void AddBallisticShots(int count) =>
            Interlocked.Add(ref s_AAShotsPending[(int)AAType.PatriotSAM], count);

        /// <summary>Atomically drain the per-type counters. Called by the flush system only.</summary>
        public static AirDefenseShotsByType Drain()
        {
            int heritage = Interlocked.Exchange(ref s_AAShotsPending[(int)AAType.HeritageBofors], 0);
            int bofors = Interlocked.Exchange(ref s_AAShotsPending[(int)AAType.Bofors40mm], 0);
            int gepard = Interlocked.Exchange(ref s_AAShotsPending[(int)AAType.Gepard], 0);
            int patriot = Interlocked.Exchange(ref s_AAShotsPending[(int)AAType.PatriotSAM], 0);
            return new AirDefenseShotsByType(heritage, bofors, gepard, patriot);
        }

        /// <summary>Reset all counters — wave reset / domain reset paths.</summary>
        public static void Reset()
        {
            for (int i = 0; i < s_AAShotsPending.Length; i++)
                Interlocked.Exchange(ref s_AAShotsPending[i], 0);
        }
    }
}
