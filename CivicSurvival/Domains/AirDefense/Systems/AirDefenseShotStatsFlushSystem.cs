using Game;
using Game.Simulation;
using Unity.Entities;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Components.Threats;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;
using CivicSurvival.Domains.AirDefense.Logic;

namespace CivicSurvival.Domains.AirDefense.Systems
{
    /// <summary>
    /// S004: Single writer of <see cref="DebriefingShotStats"/>.
    ///
    /// Producers accumulate via <see cref="AirDefenseShotCounter.AddAAShots"/> and
    /// <see cref="AirDefenseShotCounter.AddBallisticShots"/>. This system runs after both
    /// producers and before <c>WaveExecutor</c>, atomically drains the counters, and writes
    /// the total to the debrief singleton in the SAME frame the shots were fired.
    ///
    /// Why split out: previously ADO wrote shots inline in its own update. Because BDS runs
    /// RegisterAfter(ADO)], BDS's frame-N shots only reached the debrief on frame N+1's ADO
    /// flush — so a wave that ended on frame N (e.g. last threat killed by ballistic intercept)
    /// produced WaveEndedEvent with stale ShotsFired and wrong efficiency.
    /// </summary>
    [ActIndependent]
    public partial class AirDefenseShotStatsFlushSystem : CivicSystemBase
    {
        private static readonly LogContext Log = new("ADShotStatsFlush");

        private EntityQuery m_DebriefingQuery;
        private ComponentLookup<DebriefingShotStats> m_ShotStatsLookup;
#pragma warning disable CIVIC229 // System reference — UI stats cache is owned by AirDefenseStateSystem.
        private AirDefenseStateSystem m_StateSystem = null!;
#pragma warning restore CIVIC229

        protected override void OnCreate()
        {
            base.OnCreate();
            m_DebriefingQuery = GetEntityQuery(ComponentType.ReadWrite<DebriefingShotStats>());
            m_ShotStatsLookup = GetComponentLookup<DebriefingShotStats>(false);
            Log.Info("Created (single writer of DebriefingShotStats)");
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            m_StateSystem ??= FeatureRegistry.Instance.Require<AirDefenseStateSystem>();
        }

        protected override void OnUpdateImpl()
        {
            if (!m_DebriefingQuery.TryGetSingletonEntity<DebriefingShotStats>(out var debriefEntity))
                return;

            var shotsByType = AirDefenseShotCounter.Drain();
            int total = shotsByType.Total;
            if (total == 0) return;

            // Each drained shot is one CurrentAmmo-- on a live AA; keep the pause-safe UI
            // stats cache falling in step per AAType, since value-only decrements never trip
            // the owner's structural rebaseline.
            m_StateSystem.RecordUiStatsAmmoSpent(shotsByType);

            m_ShotStatsLookup.Update(this);
            var stats = m_ShotStatsLookup[debriefEntity];
            // Gun rounds (Heritage/Bofors/Gepard) vs Patriot interceptor missiles, for balance
            // telemetry. Same per-type drain that already feeds the UI stats cache — this only books
            // the gun/missile split alongside the existing total.
            int missiles = shotsByType.Get(AAType.PatriotSAM);
            int rounds = total - missiles;
#pragma warning disable CIVIC069 // Debrief counter — overflow after many sessions is acceptable
            stats.ShotsFired += total;
            stats.RoundsConsumed += rounds;
            stats.MissilesConsumed += missiles;
#pragma warning restore CIVIC069
            m_ShotStatsLookup[debriefEntity] = stats;
        }
    }
}
