using Game.Buildings;
using Game.Common;
using Game.Objects;
using Unity.Entities;
using CivicSurvival.Core.Components.Domain.AirDefense;
using CivicSurvival.Core.Components.Requests;
using CivicSurvival.Core.Components.Threats;

namespace CivicSurvival.Domains.AirDefense.Logic
{
    /// <summary>
    /// Bundle of all ECS lookups that <see cref="FireControlCoordinator"/> forwards
    /// to <see cref="FireControlExecutor"/>. Owned by <c>AirDefenseOrchestrator</c>,
    /// updated each frame, snapshotted into this struct at execute time.
    ///
    /// Lookups are structs containing pointers + safety handles; copying the bundle
    /// once per frame is cheap (no GC, no ECS storage duplication).
    /// </summary>
    internal readonly struct FireControlEcsLookups
    {
        public readonly ComponentLookup<Shahed> Shahed;
        public readonly ComponentLookup<ShahedCombatState> CombatState;
        public readonly ComponentLookup<ActiveThreat> ActiveThreat;
        public readonly ComponentLookup<PendingDestruction> PendingDestruction;
        // Faction safety net for TryGetLiveThreat: the AA candidate query already excludes outbound
        // projectiles (AirDefenseOrchestrator.m_ThreatQuery PERF-LOCK), so this is the belt-and-
        // braces guard if a player outbound counter-strike ever reaches scoring — AA must never
        // fire on the player's own projectile.
        public readonly ComponentLookup<PlayerOutboundThreat> PlayerOutbound;
        public readonly ComponentLookup<AirDefenseInstallation> AA;
        public readonly ComponentLookup<AirDefenseCooldown> CooldownLookup;
        public readonly ComponentLookup<Simulate> Simulate;
        public readonly ComponentLookup<Deleted> Deleted;
        public readonly ComponentLookup<Destroyed> Destroyed;
        public readonly ComponentLookup<IdentifiedTarget> IdentifiedTarget;
        public readonly ComponentLookup<Building> Building;
        public readonly EntityStorageInfoLookup StorageInfo;

        public FireControlEcsLookups(
            ComponentLookup<Shahed> shahed,
            ComponentLookup<ShahedCombatState> combatState,
            ComponentLookup<ActiveThreat> activeThreat,
            ComponentLookup<PendingDestruction> pendingDestruction,
            ComponentLookup<PlayerOutboundThreat> playerOutbound,
            ComponentLookup<AirDefenseInstallation> aa,
            ComponentLookup<AirDefenseCooldown> cooldown,
            ComponentLookup<Simulate> simulate,
            ComponentLookup<Deleted> deleted,
            ComponentLookup<Destroyed> destroyed,
            ComponentLookup<IdentifiedTarget> identifiedTarget,
            ComponentLookup<Building> building,
            EntityStorageInfoLookup storageInfo)
        {
            Shahed = shahed;
            CombatState = combatState;
            ActiveThreat = activeThreat;
            PendingDestruction = pendingDestruction;
            PlayerOutbound = playerOutbound;
            AA = aa;
            CooldownLookup = cooldown;
            Simulate = simulate;
            Deleted = deleted;
            Destroyed = destroyed;
            IdentifiedTarget = identifiedTarget;
            Building = building;
            StorageInfo = storageInfo;
        }
    }
}
