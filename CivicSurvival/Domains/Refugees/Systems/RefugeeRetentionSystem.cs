using System.Collections.Generic;
using Game.Citizens;
using Game.Common;
using Game.Economy;
using Unity.Entities;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Systems;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Systems.Scheduling;
using CivicSurvival.Core.Utils;
using CivicSurvival.Domains.Refugees.Data;

namespace CivicSurvival.Domains.Refugees.Systems
{
    /// <summary>
    /// Keeps refugee households in the city permanently.
    ///
    /// Vanilla pushes households out through one funnel: every cause (NotHappy /
    /// NoMoney / NoAdults in HouseholdBehaviorSystem, failed property search in
    /// HouseholdFindPropertySystem) adds MovingAway, and HouseholdMoveAwaySystem
    /// later deletes the household once all its citizens reach an outside
    /// connection. This system strips MovingAway from RefugeeHousehold entities
    /// (registered before HouseholdMoveAwaySystem) and repairs citizen-side traces:
    /// - clears CitizenFlags.MovingAwayReachOC — CitizenBehaviorSystem deletes
    ///   flagged citizens on their next tick (CitizenBehaviorSystem.cs:657);
    /// - purges TripNeeded entries with Purpose.MovingAway so citizens do not
    ///   start walking to the border.
    /// The removal goes through an ECB, so vanilla jobs in the strip frame can
    /// still flag citizens after our pass — the follow-up sweep re-repairs each
    /// stripped household for a few extra ticks. A household processed once by
    /// HouseholdMoveAwaySystem may lose its m_TempHome; RefugeeMigrationSystem's
    /// orphan pass reassigns it to a park.
    ///
    /// NoMoney is a standing condition for homeless households (vanilla drains
    /// 1 money per household tick, threshold wealth+income &lt; -1000), so without
    /// help the strip would re-fire on every household tick forever. The aid pass
    /// tops the household wallet up to the middle-class wealth floor (see
    /// RefugeeWealthUtil; multiplier 0 degrades to "keep out of the negative")
    /// every RefugeeAidIntervalHours — silencing the trigger and keeping refugees
    /// from sliding back into the Wretched class while jobless. The money is the
    /// entity wallet only — the budget side of refugee support is already paid
    /// via RefugeeSupportCostSystem.
    /// </summary>
    [ActIndependent]
    public partial class RefugeeRetentionSystem : CivicSystemBase
    {
        private static readonly LogContext Log = new("RefugeeRetentionSystem");

        // Vanilla can set MovingAwayReachOC only while the household still carries
        // MovingAway; our ECB removal lands at the barrier, leaving at most ~1 frame
        // of exposure. A few re-repair ticks close it with margin.
        private const int FOLLOWUP_TICKS = 4;

        private struct FollowUp
        {
            public Entity Household;
            public int TicksLeft;
        }

        private EntityQuery m_AnyRefugeeQuery;
        private EntityQuery m_MovingAwayRefugeeQuery;
        private EntityQuery m_HappinessParamQuery;
        private GameSimulationEndBarrier m_ECBSystem = null!;

        private ComponentLookup<Citizen> m_CitizenLookup;
        private ComponentLookup<Deleted> m_DeletedLookup;
        private BufferLookup<HouseholdCitizen> m_HouseholdCitizenLookup;
        private BufferLookup<TripNeeded> m_TripNeededLookup;

        private readonly List<FollowUp> m_FollowUps = new();
        // Transient throttle: re-anchors after load (game hours are persistent, so a
        // stale 0 just means one immediate, cheap aid pass).
        private double m_LastAidGameHours;

        protected override void OnCreate()
        {
            base.OnCreate();

            // Gate: every job here (strip MovingAway, follow-up repairs, wallet aid) only
            // applies to refugees, so the system is useless while none exist. RefugeeHousehold
            // is a permanent (never-removed) plain tag, so IsEmptyIgnoreFilter honours it and
            // ShouldRunSystem skips OnUpdateImpl entirely in a city with no refugees.
            m_AnyRefugeeQuery = GetEntityQuery(
                ComponentType.ReadOnly<RefugeeHousehold>(),
                ComponentType.Exclude<Deleted>()
            );
            RequireForUpdate(m_AnyRefugeeQuery);

            m_MovingAwayRefugeeQuery = GetEntityQuery(
                ComponentType.ReadOnly<RefugeeHousehold>(),
                ComponentType.ReadOnly<Game.Agents.MovingAway>(),
                ComponentType.Exclude<Deleted>()
            );

            m_HappinessParamQuery = GetEntityQuery(
                ComponentType.ReadOnly<Game.Prefabs.CitizenHappinessParameterData>());

            m_CitizenLookup = GetComponentLookup<Citizen>(false);
            m_DeletedLookup = GetComponentLookup<Deleted>(true);
            m_HouseholdCitizenLookup = GetBufferLookup<HouseholdCitizen>(true);
            m_TripNeededLookup = GetBufferLookup<TripNeeded>(false);

            m_ECBSystem = World.GetOrCreateSystemManaged<GameSimulationEndBarrier>();

            Log.Info("Created");
        }

        protected override void OnUpdateImpl()
        {
            var scenarioCfg = BalanceConfig.Current.Scenario;
            bool hasMovingAway = !m_MovingAwayRefugeeQuery.IsEmptyIgnoreFilter;
            bool hasGameHours = GameTimeSystem.TryGetGameHours(out var currentGameHours);
            bool aidDue = hasGameHours
                && currentGameHours - m_LastAidGameHours >= scenarioCfg.RefugeeAidIntervalHours;

            if (!hasMovingAway && m_FollowUps.Count == 0 && !aidDue)
                return;

            m_CitizenLookup.Update(this);
            m_DeletedLookup.Update(this);
            m_HouseholdCitizenLookup.Update(this);
            m_TripNeededLookup.Update(this);

            if (hasMovingAway)
                StripMovingAway();

            if (m_FollowUps.Count > 0)
                RunFollowUps();

            if (aidDue)
            {
                m_LastAidGameHours = currentGameHours;
                TopUpWallets(scenarioCfg.RefugeeWealthFloorMultiplier);
            }
        }

        private void StripMovingAway()
        {
            var ecb = m_ECBSystem.CreateCommandBuffer();
            int stripped = 0;

            foreach (var (_, entity) in
                SystemAPI.Query<RefRO<Game.Agents.MovingAway>>()
                .WithAll<RefugeeHousehold>()
                .WithNone<Deleted>()
                .WithEntityAccess())
            {
                ecb.RemoveComponent<Game.Agents.MovingAway>(entity);
                RepairCitizens(entity);
                m_FollowUps.Add(new FollowUp { Household = entity, TicksLeft = FOLLOWUP_TICKS });
                stripped++;
            }

            if (stripped > 0 && Log.IsDebugEnabled)
                Log.Debug($"Stripped MovingAway from {stripped} refugee household(s)");
        }

        private void RunFollowUps()
        {
            for (int i = m_FollowUps.Count - 1; i >= 0; i--)
            {
                var followUp = m_FollowUps[i];
                // HasBuffer is false for destroyed entities too — no Exists check needed.
                if (!m_HouseholdCitizenLookup.HasBuffer(followUp.Household)
                    || m_DeletedLookup.HasComponent(followUp.Household))
                {
                    m_FollowUps.RemoveAt(i);
                    continue;
                }

                RepairCitizens(followUp.Household);
                followUp.TicksLeft--;
                if (followUp.TicksLeft <= 0)
                    m_FollowUps.RemoveAt(i);
                else
                    m_FollowUps[i] = followUp;
            }
        }

        private void RepairCitizens(Entity household)
        {
            if (!m_HouseholdCitizenLookup.TryGetBuffer(household, out var citizens))
                return;

            for (int i = 0; i < citizens.Length; i++)
            {
                Entity citizen = citizens[i].m_Citizen;

                if (m_CitizenLookup.TryGetComponent(citizen, out var citizenData)
                    && (citizenData.m_State & CitizenFlags.MovingAwayReachOC) != CitizenFlags.None)
                {
                    citizenData.m_State &= ~CitizenFlags.MovingAwayReachOC;
                    m_CitizenLookup[citizen] = citizenData;
                }

                if (m_TripNeededLookup.TryGetBuffer(citizen, out var trips))
                {
                    for (int t = trips.Length - 1; t >= 0; t--)
                    {
                        if (trips[t].m_Purpose == Purpose.MovingAway)
                            trips.RemoveAt(t);
                    }
                }
            }
        }

        private void TopUpWallets(float wealthFloorMultiplier)
        {
            // Middle-class floor from the vanilla happiness singleton; without it
            // (or with multiplier 0) degrade to "keep wallets out of the negative".
            int wealthFloor = 0;
            if (m_HappinessParamQuery.TryGetSingleton<Game.Prefabs.CitizenHappinessParameterData>(out var happinessParams))
                wealthFloor = RefugeeWealthUtil.GetWealthFloor(happinessParams, wealthFloorMultiplier);

            int topped = 0;

            foreach (var resources in
                SystemAPI.Query<DynamicBuffer<Game.Economy.Resources>>()
                .WithAll<RefugeeHousehold>()
                .WithNone<Deleted>())
            {
                if (EconomyUtils.GetResources(Resource.Money, resources) < wealthFloor)
                {
                    EconomyUtils.SetResources(Resource.Money, resources, wealthFloor);
                    topped++;
                }
            }

            if (topped > 0 && Log.IsDebugEnabled)
                Log.Debug($"Aid pass: topped up {topped} refugee wallet(s) to {wealthFloor}");
        }
    }
}
