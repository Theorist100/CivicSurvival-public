using Game;
using Game.Simulation;
using Unity.Entities;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Components.Threats;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Systems;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;
using CivicSurvival.Domains.Attention.Data;
using CivicSurvival.Core.Systems.Scheduling;

namespace CivicSurvival.Domains.Attention.Systems
{
    /// <summary>
    /// Processes shock from casualties and destroyed buildings. READ-ONLY — does not write to WorldShockState.
    /// WorldShockSystem reads the output deltas and applies the single write.
    ///
    /// Runs only on frames carrying CasualtyEvent/DestroyedBuildingEvent (RequireAnyForUpdate) —
    /// 0 work when idle. Bumps ProducedEpoch on each run; WorldShockSystem applies these deltas
    /// exactly once per epoch, so a gated idle frame can never re-apply last run's stale values.
    /// Uses ECB for deferred event destruction.
    ///
    /// Ordering contract: AttentionDomain registers a direct
    /// WorldShockReactionSystem -> WorldShockSystem edge. WorldShockSystem must
    /// read these per-frame deltas after Reaction has published them.
    /// </summary>
    [ActIndependent]
    public partial class WorldShockReactionSystem : CivicSystemBase
    {
        private static readonly LogContext Log = new("WorldShockReactionSystem");

        private EntityQuery m_CasualtyEventQuery;
        private EntityQuery m_DestroyedBuildingEventQuery;
        private GameSimulationEndBarrier m_ECBSystem = null!;

        // Preserve historical PERF.log marker name (without "System" suffix) so
        // existing dashboards / analyzers do not see a renamed entry after the
        // GameSystemBase -> CivicSystemBase migration. CivicSystemBase wraps
        // OnUpdateImpl with PerformanceProfiler.Measure(ProfileName) automatically.
        protected override string ProfileName => "WorldShockReaction.OnUpdate";

        // ===== Output deltas (read by WorldShockSystem) =====

        /// <summary>Shock gain this frame (positive or 0). Reset each frame.</summary>
        public float ShockGain { get; private set; }

        /// <summary>Casualties this frame. Reset each frame.</summary>
        public int Casualties { get; private set; }

        /// <summary>Buildings destroyed this frame. Reset each frame.</summary>
        public int BuildingsDestroyed { get; private set; }

        /// <summary>Civilian (non-PP) buildings destroyed this frame. Subset of BuildingsDestroyed.</summary>
        public int CivilianBuildingsDestroyed { get; private set; }

        /// <summary>Critical hits this frame. Reset each frame.</summary>
        public int CriticalHits { get; private set; }

        /// <summary>Last tragedy time (game hours). 0 = no tragedy this frame. Reset each frame.</summary>
        public double LastTragedyTime { get; private set; }

        /// <summary>
        /// Bumped once per production run (i.e. per event frame). WorldShockSystem applies the
        /// deltas above only when this value changed since its last apply, so a skipped idle frame
        /// (RequireAnyForUpdate gate) never re-applies stale deltas — CRIT-C1 root fix.
        /// </summary>
        public uint ProducedEpoch { get; private set; }

        protected override void OnCreate()
        {
            base.OnCreate();

            m_CasualtyEventQuery = GetEntityQuery(
                ComponentType.ReadOnly<CasualtyEvent>()
            );

            // DestroyedBuildingEvent is now on SEPARATE entities (not on vanilla buildings)
            m_DestroyedBuildingEventQuery = GetEntityQuery(
                ComponentType.ReadOnly<DestroyedBuildingEvent>()
            );

            m_ECBSystem = World.GetOrCreateSystemManaged<GameSimulationEndBarrier>();

            // PERF-LOCK: gate to event frames only — 0 work (no OnUpdate, no profiler wrap) when
            // there are no casualty/destroyed-building events. The former CRIT-C1 (a skipped idle
            // frame leaving stale deltas that WorldShockSystem re-applied, racing shock to 100%) is
            // fixed on the CONSUMER side via ProducedEpoch: WorldShockSystem applies the deltas only
            // when the epoch changed. Do NOT remove this gate to "fix" stale deltas — dropping the
            // gate was the workaround; the epoch check is the root fix.
            RequireAnyForUpdate(m_CasualtyEventQuery, m_DestroyedBuildingEventQuery);

            Log.Info("Created — event-driven shock processing (read-only, no WorldShockState write)");
        }

        protected override void OnUpdateImpl()
        {
            // Reset deltas each run
            ShockGain = 0f;
            Casualties = 0;
            BuildingsDestroyed = 0;
            CivilianBuildingsDestroyed = 0;
            CriticalHits = 0;
            LastTragedyTime = 0.0;

            // New production generation — consumer applies the deltas exactly once per epoch.
            ProducedEpoch++;

            // FIX AT-01: Use GameTimeSystem for consistent game hours (not wall-clock time)
            var timeProvider = GameTimeSystem.Instance;
            if (timeProvider == null)
            {
                Log.Error("[WorldShockReactionSystem] GameTimeSystem unavailable — skipping update");
                return;
            }

            bool hasCasualties = !m_CasualtyEventQuery.IsEmpty;
            bool hasDestroyed = !m_DestroyedBuildingEventQuery.IsEmpty;
            if (!hasCasualties && !hasDestroyed)
                return;

            double currentTime = timeProvider.Current.TotalGameHours;

#pragma warning disable CIVIC145 // ECB passed to ProcessCasualtyEvents/ProcessDestroyedBuildingEvents helpers
            var ecb = m_ECBSystem.CreateCommandBuffer();
#pragma warning restore CIVIC145

            if (hasCasualties)
                ProcessCasualtyEvents(currentTime, ecb);

            if (hasDestroyed)
                ProcessDestroyedBuildingEvents(currentTime, ecb);

        }

        private void ProcessCasualtyEvents(double currentTime, EntityCommandBuffer ecb)
        {
            var attn = BalanceConfig.Current.Attention;
            bool anyEventProcessed = false;

            foreach (var (evtRef, entity) in
                SystemAPI.Query<RefRO<CasualtyEvent>>()
                .WithEntityAccess())
            {
                var evt = evtRef.ValueRO;
                float modifier = evt.Type switch
                {
                    CasualtyType.Residential => 1f,
                    CasualtyType.Hospital => attn.MultHospital,
                    CasualtyType.School => attn.MultSchool,
                    CasualtyType.CriticalInfra => attn.MultCriticalInfra,
                    _ => 1f // all values covered — unreachable
                };

                float shockGain = evt.Count * attn.ShockPerCasualty * modifier;

                if (evt.Count >= attn.MassCasualtyThreshold)
                {
                    shockGain += attn.ShockMassCasualtyBonus;
                    Log.Warn($"MASS CASUALTY EVENT: {evt.Count} casualties!");
                }

                ShockGain += shockGain;
                Casualties += evt.Count;
                anyEventProcessed = true;

                // Destroy via ECB (deferred, safe for vanilla jobs)
                ecb.DestroyEntity(entity);
            }

            // Row W2-54: track tragedy time on event presence, not on ShockGain>0.
            // - CriticalHits is incremented only in ProcessDestroyedBuildingEvents (runs AFTER),
            //   so the old `CriticalHits > 0` clause here was always false (dead).
            // - A zero-count casualty event is still a tragedy signal (the system fired it for a
            //   reason); the old gate silently swallowed it because shockGain == 0.
            if (anyEventProcessed)
            {
                LastTragedyTime = currentTime;
            }
        }

        private void ProcessDestroyedBuildingEvents(double currentTime, EntityCommandBuffer ecb)
        {
            float shockGainFromBuildings = 0f;
            int destroyedCount = 0;

            var attnCfg = BalanceConfig.Current.Attention;

            foreach (var (evtRef, entity) in
                SystemAPI.Query<RefRO<DestroyedBuildingEvent>>()
                .WithEntityAccess())
            {
                float shock = attnCfg.ShockPerBuilding;

                if (evtRef.ValueRO.IsCritical)
                {
                    shock += attnCfg.ShockCriticalHitBonus;
                    CriticalHits++;
                }

                shockGainFromBuildings += shock;
                destroyedCount++;
                if (!evtRef.ValueRO.IsPowerPlant)
                    CivilianBuildingsDestroyed++;

                // Destroy via ECB (deferred, safe for vanilla jobs)
                ecb.DestroyEntity(entity);
            }

            BuildingsDestroyed += destroyedCount;

            if (shockGainFromBuildings > 0)
                ShockGain += shockGainFromBuildings;
            if (shockGainFromBuildings > 0 || CriticalHits > 0)
                LastTragedyTime = currentTime;
        }
    }
}
