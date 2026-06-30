using Game.Common;
using Game.Objects;
using Unity.Entities;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Components.Threats;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Events;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Systems.Scheduling;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;
using CivicSurvival.Domains.AirDefense.Systems;

namespace CivicSurvival.Domains.AirDefense.Logic
{
    /// <summary>
    /// Wraps <see cref="FireControlExecutor"/> with the main-thread wiring that ADO
    /// previously held: the vanilla static search tree fetch and fire-control modifier
    /// reads (spotter penalty, telemarathon detection bonus, raycast/altitude bypass).
    ///
    /// The static search tree is fetched fresh and fenced (<c>Complete()</c>) on every
    /// <see cref="Execute"/> pass, never cached across frames — see the note in Execute
    /// for why the prior 1s cache was a race with vanilla's tree writer.
    ///
    /// Not a system — owned by <see cref="Systems.AirDefenseOrchestrator"/>.
    /// </summary>
    internal sealed class FireControlCoordinator
    {
        private const float LOS_HEIGHT_MARGIN = 20f;
        private const float FALSE_POSITIVE_CHANCE = 0.05f;
        private const float IDENTIFIED_TARGET_BONUS = 0.20f;

        private SearchSystem m_SearchSystem = null!;
        private InterceptBarrier m_InterceptBarrier = null!;
        private EntityQuery m_TelemarathonQuery;
        private EntityQuery m_SpotterPenaltyQuery;

        public void Initialize(
            SearchSystem searchSystem,
            InterceptBarrier interceptBarrier,
            EntityQuery telemarathonQuery,
            EntityQuery spotterPenaltyQuery)
        {
            m_SearchSystem = searchSystem;
            m_InterceptBarrier = interceptBarrier;
            m_TelemarathonQuery = telemarathonQuery;
            m_SpotterPenaltyQuery = spotterPenaltyQuery;
        }

        /// <summary>
        /// Run fire control for one frame using N-1 targeting snapshot.
        /// Caller is responsible for reading back <see cref="FireControlResult.UpdatedRandom"/>
        /// and registering <see cref="InterceptBarrier"/> producer if
        /// <see cref="FireControlResult.InterceptCommands"/> &gt; 0.
        /// </summary>
        public FireControlResult Execute(
            in TargetingSnapshot snap,
            in FireControlEcsLookups lookups,
            SerializableRandom random,
            double nowGameSeconds,
            IEventBus? eventBus)
        {
            // Fetch the vanilla static search tree fresh and fence it on every pass — DO NOT
            // cache across frames. SearchSystem.UpdateSearchTreeJob mutates this borrowed tree
            // from a worker whenever statics change (build/demolish); a stale 1s cache let our
            // synchronous LOS Iterate run concurrently with that writer → native AV in release.
            // GetStaticSearchTree(readOnly:true) returns the live write fence; Complete() before
            // the synchronous Iterate guarantees the writer is done, independent of system order.
            // Measured cost ~0 (PERF.log: ProcessResults 0.3ms/103 calls, static sync a subset) —
            // the old cache saved nothing and only opened the race.
            // See Docs/Plans/Crash/03-firecontrol-search-tree.md.
            var staticTree = m_SearchSystem.GetStaticSearchTree(readOnly: true, out var staticDeps);
            staticDeps.Complete();

            ReadModifiers(
                out float spotterPenalty,
                out float detectionBonus,
                out float raycastEpsilon,
                out float losAltitudeBypass);

            var ctx = new FireControlContext(
                snap.ScoredCandidates,
                snap.CandidateCount,
                snap.AAs,
                snap.Threats,
                spotterPenalty,
                detectionBonus,
                raycastEpsilon,
                FALSE_POSITIVE_CHANCE,
                IDENTIFIED_TARGET_BONUS,
                LOS_HEIGHT_MARGIN,
                losAltitudeBypass);

            var executor = new FireControlExecutor(
                lookups.Shahed,
                lookups.CombatState,
                lookups.ActiveThreat,
                lookups.PendingDestruction,
                lookups.PlayerOutbound,
                lookups.AA,
                lookups.CooldownLookup,
                lookups.Simulate,
                lookups.Deleted,
                lookups.Destroyed,
                lookups.IdentifiedTarget,
                lookups.Building,
                lookups.StorageInfo,
                m_InterceptBarrier,
                staticTree,
                random,
                nowGameSeconds,
                eventBus);

            return executor.Execute(in ctx);
        }

        private void ReadModifiers(
            out float spotterPenalty,
            out float detectionBonus,
            out float raycastEpsilon,
            out float losAltitudeBypass)
        {
            // Cache singletons once (avoid per-candidate SystemAPI sync checks)
            spotterPenalty = (m_SpotterPenaltyQuery.TryGetSingleton<SpotterPenaltyState>(out var sp)
                ? sp : SpotterPenaltyState.Default).GlobalPenalty;

            var telemarathon = m_TelemarathonQuery.TryGetSingleton<TelemarathonRuntimeState>(out var tm)
                ? tm : TelemarathonRuntimeState.Default;
            detectionBonus = (telemarathon.IsActive && !telemarathon.IsInShock)
                ? telemarathon.SpotterDetectionBonus * telemarathon.EffectivenessMult
                : 0f;

            var airDefCfg = BalanceConfig.Current.AirDefense;
            raycastEpsilon = airDefCfg.RaycastEndpointEpsilon;
            losAltitudeBypass = airDefCfg.LOSAltitudeBypass;
        }
    }
}
