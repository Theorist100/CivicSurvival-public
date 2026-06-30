using Colossal.Collections;
using Colossal.Mathematics;
using CivicSurvival.Core.Systems.Scheduling;
using Game.Buildings;
using Game.Common;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using CivicSurvival.Core.Components.Domain.AirDefense;
using CivicSurvival.Core.Components.Requests;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Components.Threats;
using CivicSurvival.Core.Events;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Logic;
using CivicSurvival.Core.Utils;
using CivicSurvival.Core.Systems;
using CivicSurvival.Domains.AirDefense.Iterators;
using CivicSurvival.Domains.AirDefense.Jobs;

namespace CivicSurvival.Domains.AirDefense.Logic
{
    /// <summary>
    /// Executes fire control: LOS checks, assignment, intercept rolls.
    /// Stateful per-frame — owns assignment tracking (fired AA, targeted threats).
    ///
    /// Separation:
    /// - FireControlContext = immutable frame data (candidates, AA/threat snapshots, config)
    /// - Constructor deps = mutable ECS runtime (lookups, ECB, tree, random, events)
    /// - FireControlResult = output (shots fired)
    ///
    /// Caller (Orchestrator) is responsible for:
    /// - Flushing ShotStats to singleton (needs SystemState for lookup update)
    /// - Managing InterceptBarrier dependency
    /// - Reading back updated Random state
    /// </summary>
    internal struct FireControlExecutor
    {
        private static readonly LogContext Log = new("FireControl");

        // Mutable ECS dependencies (set at construction)
        private ComponentLookup<Shahed> m_ShahedLookup;
        private ComponentLookup<ShahedCombatState> m_CombatStateLookup;
        private ComponentLookup<ActiveThreat> m_ActiveThreatLookup;
        private ComponentLookup<PendingDestruction> m_PendingDestructionLookup;
        private ComponentLookup<PlayerOutboundThreat> m_PlayerOutboundLookup;
        private ComponentLookup<AirDefenseInstallation> m_AALookup;
        private ComponentLookup<AirDefenseCooldown> m_CooldownLookup;
        private ComponentLookup<Simulate> m_SimulateLookup;
        private ComponentLookup<Deleted> m_DeletedLookup;
        private ComponentLookup<Destroyed> m_DestroyedLookup;
        private ComponentLookup<IdentifiedTarget> m_IdentifiedTargetLookup;
        private ComponentLookup<Building> m_BuildingLookup;
        private EntityStorageInfoLookup m_StorageInfoLookup;
        private InterceptBarrier m_InterceptBarrier;
        private NativeQuadTree<Entity, QuadTreeBoundsXZ> m_StaticTree;
        private SerializableRandom m_Random;
        private readonly double m_NowGameSeconds;
        private IEventBus? m_EventBus;

        // Per-frame ECB command counter — returned via FireControlResult.EcbCommands
        private int m_EcbCommandCount;
        // R9-M5: Intercept-specific ECB commands (InterceptBarrier only)
        private int m_InterceptCommands;

        public FireControlExecutor(
            ComponentLookup<Shahed> shahedLookup,
            ComponentLookup<ShahedCombatState> combatStateLookup,
            ComponentLookup<ActiveThreat> activeThreatLookup,
            ComponentLookup<PendingDestruction> pendingDestructionLookup,
            ComponentLookup<PlayerOutboundThreat> playerOutboundLookup,
            ComponentLookup<AirDefenseInstallation> aaLookup,
            ComponentLookup<AirDefenseCooldown> cooldownLookup,
            ComponentLookup<Simulate> simulateLookup,
            ComponentLookup<Deleted> deletedLookup,
            ComponentLookup<Destroyed> destroyedLookup,
            ComponentLookup<IdentifiedTarget> identifiedTargetLookup,
            ComponentLookup<Building> buildingLookup,
            EntityStorageInfoLookup storageInfoLookup,
            InterceptBarrier interceptBarrier,
            NativeQuadTree<Entity, QuadTreeBoundsXZ> staticTree,
            SerializableRandom random,
            double nowGameSeconds,
            IEventBus? eventBus)
        {
            m_ShahedLookup = shahedLookup;
            m_CombatStateLookup = combatStateLookup;
            m_ActiveThreatLookup = activeThreatLookup;
            m_PendingDestructionLookup = pendingDestructionLookup;
            m_PlayerOutboundLookup = playerOutboundLookup;
            m_AALookup = aaLookup;
            m_CooldownLookup = cooldownLookup;
            m_SimulateLookup = simulateLookup;
            m_DeletedLookup = deletedLookup;
            m_DestroyedLookup = destroyedLookup;
            m_IdentifiedTargetLookup = identifiedTargetLookup;
            m_BuildingLookup = buildingLookup;
            m_StorageInfoLookup = storageInfoLookup;
            m_InterceptBarrier = interceptBarrier;
            m_StaticTree = staticTree;
            m_Random = random;
            m_NowGameSeconds = nowGameSeconds;
            m_EventBus = eventBus;
            m_EcbCommandCount = 0;
            m_InterceptCommands = 0;
        }

        /// <summary>
        /// Run fire control for one frame.
        /// Iterates scored candidates, checks LOS, rolls intercept.
        /// </summary>
        public FireControlResult Execute(in FireControlContext ctx)
        {
            int shotsFired = 0;
            var shotsByType = new int[AirDefenseShotsByType.TypeCount];
            m_EcbCommandCount = 0;
            m_InterceptCommands = 0;

            // Sort scored candidates by score (descending)
            int candidateCount = math.min(ctx.CandidateCount, ctx.ScoredCandidates.Length);
            var sortedCandidates = new NativeArray<EngagementCandidate>(candidateCount, Allocator.Temp);
            NativeArray<EngagementCandidate>.Copy(ctx.ScoredCandidates, sortedCandidates, candidateCount);
            sortedCandidates.Sort(new CandidateScoreDescComparer());

            // Assignment tracking — Temp, scoped to this Execute call only
            var firedAAIndices = new NativeHashSet<int>(64, Allocator.Temp);
            var targetedThreatIndices = new NativeHashSet<int>(256, Allocator.Temp);

            try
            {
                for (int i = 0; i < sortedCandidates.Length; i++)
                {
                    var candidate = sortedCandidates[i];

                    if (AirDefenseScoringRules.IsInvalidScore(candidate.Score)) continue;
                    if (firedAAIndices.Contains(candidate.AAIndex)) continue;
                    if (targetedThreatIndices.Contains(candidate.ThreatIndex)) continue;

                    // Bounds check for N-1 data
                    if (candidate.AAIndex >= ctx.AAData.Length ||
                        candidate.ThreatIndex >= ctx.ThreatData.Length)
                        continue;

                    var aaData = ctx.AAData[candidate.AAIndex];
                    var threatData = ctx.ThreatData[candidate.ThreatIndex];

                    // S16a-1 FIX: Cross-frame entity validation — skip if threat index shifted
                    if (threatData.EntityIndex != candidate.ThreatEntityIndex ||
                        threatData.EntityVersion != candidate.ThreatEntityVersion)
                        continue;

                    if (!TryGetLiveThreat(threatData.GetEntity(), out _, out var liveCombatState))
                        continue;

                    // Candidates are N-1 hints only. Revalidate the live AA before any shot
                    // mutation so stale crew/deletion/cooldown state cannot fire.
                    if (!TryGetReadyAA(aaData.GetEntity(), out var liveAA))
                        continue;

                    if (!CheckLineOfSight(aaData.Position, threatData.Position, ctx.RaycastEpsilon, ctx.LOSHeightMargin, ctx.LOSAltitudeBypass, aaData.Building.Index, aaData.Building.Version))
                        continue;

                    // False positive: unidentified threats have chance of wasted shot
                    if (!threatData.IsPriority)
                    {
                        var identifyEntity = threatData.GetEntity();
                        bool isIdentified = m_IdentifiedTargetLookup.TryGetComponent(identifyEntity, out var identifyData)
                            && identifyData.Identified;

                        if (!isIdentified && m_Random.NextFloat(0f, 1f) < ctx.FalsePositiveChance)
                        {
                            firedAAIndices.Add(candidate.AAIndex); // M4: mark AA used even when exhausted (prevents redundant FP rolls)
                            // AA wastes shot on false contact — consumes ammo + cooldown but no intercept roll
                            // H02 fix: direct write via RW lookup (avoids ECB stomp vs BDS)
                            var fpAaEntity = aaData.GetEntity();
                            // Same burst accounting as a real shot: a wasted engagement still spends a
                            // full burst of shells (1 shell = 1 tracer), just without an intercept roll.
                            int fpRounds = AAParams.ForType(BalanceConfig.Current, aaData.Type).BurstRounds;
                            liveAA.CurrentAmmo = math.max(0, liveAA.CurrentAmmo - fpRounds);
#pragma warning disable CIVIC035 // TryGetReadyAA validated AirDefenseInstallation + alive state for this exact entity.
                            m_AALookup[fpAaEntity] = liveAA;
#pragma warning restore CIVIC035
                            // Cooldown split into AirDefenseCooldown; write-guard for pre-migration entities.
                            if (m_CooldownLookup.HasComponent(fpAaEntity))
                                m_CooldownLookup[fpAaEntity] = new AirDefenseCooldown { ReadyAtGameSeconds = m_NowGameSeconds + liveAA.CooldownDuration };
                            shotsFired += fpRounds;
                            shotsByType[(int)aaData.Type] += fpRounds;
                            liveCombatState.MissedShotsCount++;
#pragma warning disable CIVIC035 // TryGetLiveThreat validated ShahedCombatState immediately before this miss merge.
                            m_CombatStateLookup[identifyEntity] = liveCombatState;
#pragma warning restore CIVIC035
                            // DIAG: one [AA:SHOT] line per real shot, independent of the tracer/event path.
                            // grep "[AA:SHOT]" counts fire-control shots; compare against "[TRACER]" burst
                            // count to tell a render-side tracer glitch from missing fire-control output.
                            if (Log.IsDebugEnabled) Log.Debug($"[AA:SHOT] {aaData.Type} aa={fpAaEntity.Index} threat={identifyEntity.Index} rounds={fpRounds} -> FALSE_POSITIVE (wasted on unidentified) ammo={liveAA.CurrentAmmo}");
                            // False contact (no lock): pass NO threat ref, so a Patriot interceptor flies
                            // to the fired-at point and misses instead of homing on the real threat.
                            m_EventBus?.SafePublish(new AAFireEvent(aaData.Position, threatData.Position, aaData.Type, fpAaEntity.Index), "FireControl");
                            // Do NOT add to targetedThreatIndices — false-positive does not consume
                            // the threat's engagement slot. Other AAs can still target this threat.
                            continue;
                        }
                    }

                    int shot = TryIntercept(aaData, threatData, liveAA, ctx.SpotterPenalty, ctx.DetectionBonus, ctx.IdentifiedTargetBonus);
                    shotsFired += shot;
                    if (shot > 0)
                    {
                        shotsByType[(int)aaData.Type] += shot;
                        firedAAIndices.Add(candidate.AAIndex);
                        targetedThreatIndices.Add(candidate.ThreatIndex);
                    }
                }
            }
            finally
            {
                if (sortedCandidates.IsCreated) sortedCandidates.Dispose();
                if (firedAAIndices.IsCreated) firedAAIndices.Dispose();
                if (targetedThreatIndices.IsCreated) targetedThreatIndices.Dispose();
            }

            return new FireControlResult
            {
                ShotsFired = shotsFired,
                ShotsByType = new AirDefenseShotsByType(
                    shotsByType[(int)AAType.HeritageBofors],
                    shotsByType[(int)AAType.Bofors40mm],
                    shotsByType[(int)AAType.Gepard],
                    shotsByType[(int)AAType.PatriotSAM]),
                EcbCommands = m_EcbCommandCount,
                InterceptCommands = m_InterceptCommands,
                UpdatedRandom = m_Random
            };
        }

        private bool CheckLineOfSight(float3 aaPos, float3 targetPos, float raycastEpsilon, float losHeightMargin, float losAltitudeBypass, int sourceBuildingIndex, int sourceBuildingVersion)
        {
            // Altitude bypass is configurable via BalanceConfig.AirDefense.LOSAltitudeBypass
            if (targetPos.y > losAltitudeBypass) return true;

            // Degenerate segment — AA and target at same position, treat as clear LOS
            if (math.all(aaPos == targetPos)) return true;

            float3 min = math.min(aaPos, targetPos) - new float3(10f, 10f, 10f);
            float3 max = math.max(aaPos, targetPos) + new float3(10f, 10f, 10f);

            // Clamp MinHeight to 0 — negative values (low-altitude AA) disable height filter
            float minHeight = math.max(aaPos.y - losHeightMargin, 0f);

            var losIterator = LineOfSightIterator.Create(
                new Line3.Segment(aaPos, targetPos),
                new Bounds3(min, max),
                minHeight,
                raycastEpsilon,
                m_BuildingLookup,
                sourceBuildingIndex,
                sourceBuildingVersion);

            m_StaticTree.Iterate(ref losIterator);
            return !losIterator.IsBlocked;
        }

        private bool TryGetReadyAA(Entity aaEntity, out AirDefenseInstallation aa)
        {
            aa = default;
            if (!AirDefenseLifecycle.TryGetActiveInstallation(
                    aaEntity,
                    m_AALookup,
                    m_StorageInfoLookup,
                    m_SimulateLookup,
                    m_DeletedLookup,
                    m_DestroyedLookup,
                    out aa))
                return false;
            if (aa.CrewAssigned <= 0 || aa.CurrentAmmo <= 0)
                return false;
            // ReadyAtGameSeconds is persisted game time; it freezes on pause. Read-miss = ready.
            if (m_CooldownLookup.TryGetComponent(aaEntity, out var cd) && m_NowGameSeconds < cd.ReadyAtGameSeconds)
                return false;
            return true;
        }

        private bool TryGetLiveThreat(Entity threatEntity, out Shahed shahed, out ShahedCombatState combatState)
        {
            shahed = default;
            combatState = default;
            if (!m_StorageInfoLookup.Exists(threatEntity))
                return false;
            // Faction safety net: never engage the player's own outbound counter-strike. The AA
            // candidate query (AirDefenseOrchestrator.m_ThreatQuery) already excludes outbound
            // projectiles on the hot path; this guards the case where the enable-bit flipped within
            // the scoring frame after the candidate snapshot was taken.
            if (m_PlayerOutboundLookup.HasComponent(threatEntity) && m_PlayerOutboundLookup.IsComponentEnabled(threatEntity))
                return false;
            if (!m_ShahedLookup.TryGetComponent(threatEntity, out shahed))
                return false;
            if (!m_CombatStateLookup.TryGetComponent(threatEntity, out combatState))
                return false;
            if (!m_ActiveThreatLookup.HasComponent(threatEntity) || !m_ActiveThreatLookup.IsComponentEnabled(threatEntity))
                return false;
            if (m_DeletedLookup.HasComponent(threatEntity)
                || (m_PendingDestructionLookup.HasComponent(threatEntity) && m_PendingDestructionLookup.IsComponentEnabled(threatEntity)))
                return false;
            if (combatState.IsIntercepted || combatState.IsLeaked || shahed.IsArrived)
                return false;

            return true;
        }

        private int TryIntercept(AAData aaData, ThreatData threatData, AirDefenseInstallation aa, float spotterPenalty, float detectionBonus, float identifiedTargetBonus)
        {
            var aaEntity = aaData.GetEntity();
            var threatEntity = threatData.GetEntity();

            if (!TryGetLiveThreat(threatEntity, out _, out var liveCombatState))
                return 0;

            // Cache the config ref once (read twice below: burst rounds + focus multiplier).
            var balanceCfg = BalanceConfig.Current;

            // One engagement spends a full burst of shells (1 shell = 1 tracer); ammo is decremented
            // by the burst size so the counter matches the tracers drawn by TracerSpawnSystem. Clamp
            // at 0 for the final partial burst of a near-empty gun. The intercept roll below is still
            // ONE per engagement — burst size is shells spent, not extra attempts (balance unchanged).
            int rounds = AAParams.ForType(balanceCfg, aaData.Type).BurstRounds;
            aa.CurrentAmmo = math.max(0, aa.CurrentAmmo - rounds);
#pragma warning disable CIVIC035 // TryGetReadyAA validated AirDefenseInstallation + alive state before TryIntercept.
            m_AALookup[aaEntity] = aa;
#pragma warning restore CIVIC035
            // Cooldown split into AirDefenseCooldown; write-guard for pre-migration entities.
            if (m_CooldownLookup.HasComponent(aaEntity))
                m_CooldownLookup[aaEntity] = new AirDefenseCooldown { ReadyAtGameSeconds = m_NowGameSeconds + aa.CooldownDuration };

            m_EventBus?.SafePublish(new AAFireEvent(aaData.Position, threatData.Position, aaData.Type, aaEntity.Index, threatEntity.Index, threatEntity.Version), "FireControl");

            // Accuracy bonus for identified targets (player engagement pipeline)
            float identifiedBonus = 0f;
            if (m_IdentifiedTargetLookup.TryGetComponent(threatEntity, out var identifyState) && identifyState.Identified)
            {
                identifiedBonus = identifiedTargetBonus;
                if (Log.IsDebugEnabled) Log.Debug($"[IDENTIFY] +{identifiedTargetBonus * 100f:F0}% identified bonus for threat {threatEntity.Index}");
            }

            float chance = AALogic.CalculateInterceptChance(
                aaData.InterceptChance + identifiedBonus,
                aa.CurrentAmmo + 1, // pre-shot count — decrement happened above
                aa.MaxAmmo,
                spotterPenalty,
                liveCombatState.MissedShotsCount,
                detectionBonus
            );

            // Focus-cluster (saturation) drones are harder to intercept — a coordinated,
            // concentrated strike degrades point-defense accuracy. Reduced, not zero: good AA
            // placement still thins the cluster, it is no longer flatly unstoppable.
            if (liveCombatState.IsFocusStrike)
                chance *= balanceCfg.Waves.FocusInterceptMultiplier;

            float roll = m_Random.NextFloat(0f, 1f);
            bool hit = roll < chance;

            // DIAG: one [AA:SHOT] line per real shot (HIT or MISS), emitted on the fire-control path
            // — NOT the AAFireEvent/tracer path. grep "[AA:SHOT]" = real shots fired this run; compare
            // with "[TRACER] Spawned N" (N = burst per shot: Bofors 4, Heritage 3) to separate a tracer
            // render glitch from genuinely-low fire output. chance/roll expose why a shot missed.
            if (Log.IsDebugEnabled)
                Log.Debug($"[AA:SHOT] {aaData.Type} aa={aaEntity.Index} threat={threatEntity.Index} rounds={rounds} chance={chance:P0} roll={roll:F2} -> {(hit ? "HIT" : "MISS")} ammo={aa.CurrentAmmo}");

            if (hit)
            {
                OnInterceptSuccess(threatEntity, threatData.Position, aaData.Type);
                if (Log.IsDebugEnabled) Log.Debug($"[{aaData.Type}] intercept request queued");
            }
            else
            {
                // Update missed shots — writes ShahedCombatState only (avoids ECB full-struct stomp on Shahed)
                liveCombatState.MissedShotsCount++;
#pragma warning disable CIVIC035 // TryGetLiveThreat revalidated ShahedCombatState immediately before shot side effects.
                m_CombatStateLookup[threatEntity] = liveCombatState;
#pragma warning restore CIVIC035
            }

            // Returns shells spent (= tracers drawn = ammo consumed) so shot stats stay in shell units.
            return rounds;
        }

        private void OnInterceptSuccess(Entity threat, float3 position, AAType type)
        {
            m_InterceptCommands++;
            var interceptEcb = m_InterceptBarrier.CreateCommandBuffer();

            if (TryGetLiveThreat(threat, out _, out var combatState))
            {
                combatState.IsIntercepted = true;
                // Missile launchers fire a visible interceptor → defer the explosion until it arrives
                // (coast). Guns have no missile → leave false → freeze + explode immediately as
                // before. The kill is decided HERE (IsIntercepted) regardless — PvP-safe. Weapon class
                // is owned by AATypeWeapon (same axis as the tracer/interceptor spawn gates).
                combatState.AwaitingInterceptorImpact = type.FiresInterceptorMissile();
#pragma warning disable CIVIC035 // TryGetLiveThreat validated ShahedCombatState immediately before terminal intercept.
                m_CombatStateLookup[threat] = combatState;
#pragma warning restore CIVIC035
            }

            var requestEntity = interceptEcb.CreateEntity();
            m_EcbCommandCount++;
            interceptEcb.AddComponent(requestEntity, new InterceptRequest
            {
                ThreatEntityIndex = threat.Index,
                ThreatEntityVersion = threat.Version,
                Position = position,
                IsBallistic = false
            });
            m_EcbCommandCount++;

            // InterceptProcessingSystem publishes ThreatInterceptEvent after the
            // per-wave leak floor accepts this request.
        }

        private struct CandidateScoreDescComparer : System.Collections.Generic.IComparer<EngagementCandidate>
        {
            public int Compare(EngagementCandidate a, EngagementCandidate b) => b.Score.CompareTo(a.Score);
        }
    }
}
