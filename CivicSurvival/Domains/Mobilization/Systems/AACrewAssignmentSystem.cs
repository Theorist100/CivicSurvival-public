using Game;
using Game.Simulation;
using Unity.Entities;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Components.Threats;
using CivicSurvival.Core.Components.Requests;
using CivicSurvival.Core.Events;
using CivicSurvival.Core.Components.Domain.Mobilization;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.Utils;
using System.Threading;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Interfaces;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Interfaces.Domain.Mobilization;
using CivicSurvival.Core.Interfaces.Services;
using Game.Common;

namespace CivicSurvival.Domains.Mobilization.Systems
{
    /// <summary>
    /// Assigns crew to AA installations that request it via RequestCrewTag.
    /// </summary>
    [ActIndependent]
    public partial class AACrewAssignmentSystem : CivicSystemBase, IResettable, IPostLoadValidation
    {
        private static int s_EcbCommandCount;
        public static int EcbCommandCount => Volatile.Read(ref s_EcbCommandCount);
        public static void ResetCounters() => Interlocked.Exchange(ref s_EcbCommandCount, 0);
        private static void IncrementEcbCount() => Interlocked.Increment(ref s_EcbCommandCount);

        private static readonly LogContext Log = new("AACrewAssignmentSystem");

        private EntityQuery m_RequestQuery;
        private ModificationEndBarrier m_ModificationEndBarrier = null!;
        private MobilizationSystem? m_Mobilization;
        private ComponentLookup<Deleted> m_DeletedLookup; // H16: check building still alive before recruiting
        private ComponentLookup<Destroyed> m_DestroyedLookup;
        private EntityStorageInfoLookup m_EntityStorageInfoLookup;

        private readonly System.Collections.Generic.HashSet<(int Index, int Version)> m_NotifiedEntities = new();
        /// <summary>Multi-tick guard: ECB tag removal is deferred, next tick sees same entity.</summary>
        private readonly System.Collections.Generic.HashSet<(int Index, int Version)> m_RecruitedThisFrame = new();
        [System.NonSerialized] private int m_RecruitedFrame = -1;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_RequestQuery = GetEntityQuery(
                ComponentType.ReadWrite<AirDefenseInstallation>(),
                ComponentType.ReadOnly<RequestCrewTag>()
            );
            RequireForUpdate(m_RequestQuery);
            m_ModificationEndBarrier = World.GetOrCreateSystemManaged<ModificationEndBarrier>();
            m_DeletedLookup = GetComponentLookup<Deleted>(true);
            m_DestroyedLookup = GetComponentLookup<Destroyed>(true);
            m_EntityStorageInfoLookup = GetEntityStorageInfoLookup();
            Log.Info("Created (RequireForUpdate on RequestCrewTag)");
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            m_Mobilization ??= FeatureRegistry.Instance.Require<MobilizationSystem>();
        }

        protected override void OnUpdateImpl()
        {
            if (m_RequestQuery.IsEmpty) return;

            var mobilization = m_Mobilization!;
            // FIX H71: Don't cache struct — TryRecruit mutates live state, cached copy goes stale.
            // Re-read singleton only where needed (event/log).
            m_DeletedLookup.Update(this);
            m_DestroyedLookup.Update(this);
            m_EntityStorageInfoLookup.Update(this);

            EntityCommandBuffer ecb = default;
            bool hasChanges = false;
            int frame = UnityEngine.Time.frameCount;
            if (frame != m_RecruitedFrame)
            {
                m_RecruitedThisFrame.Clear();
                m_RecruitedFrame = frame;
            }

            foreach (var (aa, request, entity) in
                SystemAPI.Query<RefRW<AirDefenseInstallation>, RefRO<RequestCrewTag>>()
                .WithEntityAccess())
            {
                if (!hasChanges)
                {
                    ecb = m_ModificationEndBarrier.CreateCommandBuffer();
                    hasChanges = true;
                }

                // Fully crewed — drop the tag. The comparison is against the REQUIRED crew, not
                // zero: a gun trimmed by MobilizationSystem.ForceReleaseExcess still has crew but
                // stands short, and must keep the tag until the shortfall is recruited back.
                if (aa.ValueRO.CrewAssigned >= request.ValueRO.CrewRequired)
                {
                    ecb.RemoveComponent<RequestCrewTag>(entity);
                    IncrementEcbCount();
                    continue;
                }

                // H16: Skip if building already destroyed — recruiting would leak manpower
                // because AACrewReleaseSystem sees CrewAssigned=0 and releases nothing.
                var buildingEntity = aa.ValueRO.GetBuildingEntity();
                if (!m_EntityStorageInfoLookup.Exists(buildingEntity) ||
                    m_DeletedLookup.HasComponent(buildingEntity) ||
                    m_DestroyedLookup.HasComponent(buildingEntity))
                {
                    ecb.RemoveComponent<RequestCrewTag>(entity);
                    IncrementEcbCount();
                    m_NotifiedEntities.Remove((entity.Index, entity.Version));
                    Log.Warn($"AA {entity.Index}: building {buildingEntity.Index} destroyed — skipping crew assignment");
                    continue;
                }

                // Multi-tick / same-frame guard: tag removal is deferred to the
                // phase barrier, so protect the manpower allocation identity.
                if (!m_RecruitedThisFrame.Add((entity.Index, entity.Version)))
                    continue;

                int crewRequired = request.ValueRO.CrewRequired;
                AAType aaType = aa.ValueRO.Type;
                int typeHash = (int)aaType;

                // Recruit the SHORTFALL, not the full crew: a gun trimmed by ForceReleaseExcess
                // still holds part of its crew, and TryRecruit accumulates into the existing
                // allocation entry — asking for the full amount again would double-book the heads
                // already standing at this gun and drain the pool twice for one installation.
                int crewMissing = crewRequired - aa.ValueRO.CrewAssigned;

                // Three-state on purpose. Taking the concrete MobilizationSystem instead of the
                // read contract used to hide this call site from any change to the contract's
                // signature; it does not any more, because the raw AvailableManpower getter is
                // gone from the class as well as from the interface.
                var recruited = mobilization.TryRecruit(crewMissing, typeHash, entity.Index, entity.Version);
                if (recruited == ManpowerCheck.Ok)
                {
                    aa.ValueRW.CrewAssigned = crewRequired;
                    // Persist required-crew on the installation so it is the single source of
                    // truth on subsequent loads (covers legacy saves whose installation predates
                    // the CrewRequired field — value 0 — and migrates them on first manning).
                    aa.ValueRW.CrewRequired = crewRequired;
                    ecb.RemoveComponent<RequestCrewTag>(entity);
                    IncrementEcbCount();
                    m_NotifiedEntities.Remove((entity.Index, entity.Version));

                    Log.Info($"{aaType} {entity.Index}: crew assigned ({crewRequired})");
                }
                else if (recruited == ManpowerCheck.InsufficientManpower)
                {
                    if (m_NotifiedEntities.Add((entity.Index, entity.Version)))
                    {
                        // Reached only on InsufficientManpower, which the pool can only answer
                        // once it has been measured — so the fallback below is unreachable here,
                        // and that is a property of the BRANCH, not of the expression. The
                        // unmeasured case has its own branch and never gets a number at all;
                        // this is what keeps `? x : 0` from being the place a refusal quietly
                        // turns back into a zero.
                        int available = mobilization.TryGetAvailableManpower(out int free) ? free : 0;
                        // Report the SHORTFALL, not the full crew: on a partially crewed gun the
                        // full number would overstate what the player actually has to find.
                        EventBus?.SafePublish(new InsufficientManpowerEvent(EnumName<AAType>.Get(aaType), crewMissing, available));
                        Log.Warn($"{aaType} {entity.Index}: insufficient manpower ({crewMissing} of {crewRequired} still needed, {available} available) - will retry");
                    }
                }
                else
                {
                    // PoolUnmeasured — the request was never judged. Two things must NOT happen
                    // here: telling the player the city is short of people (it may be nothing of
                    // the sort), and adding the installation to the notified set. The set is a
                    // one-shot latch, so spending it on a non-answer would swallow the real
                    // notification for good the next time the pool is genuinely short. The tag
                    // stays on the gun and the next tick asks again.
                    if (Log.IsDebugEnabled)
                        Log.Debug($"{aaType} {entity.Index}: crew request held — manpower pool unmeasured, retrying");
                }
            }

            if (hasChanges)
            {
                m_ModificationEndBarrier.AddJobHandleForProducer(Dependency);
            }
        }

        public void ResetState()
        {
            m_NotifiedEntities.Clear();
            m_RecruitedThisFrame.Clear();
            m_RecruitedFrame = -1;
        }

        public void ValidateAfterLoad() => ResetState();

        protected override void OnStopRunning()
        {
            m_NotifiedEntities.Clear();
            m_RecruitedThisFrame.Clear();
            m_RecruitedFrame = -1;
            base.OnStopRunning();
        }

        protected override void OnDestroy()
        {
            m_NotifiedEntities.Clear();
            m_RecruitedThisFrame.Clear();
            m_RecruitedFrame = -1;
            base.OnDestroy();
        }
    }
}
