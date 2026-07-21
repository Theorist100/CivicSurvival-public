using System;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Components.Placement;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;
using Game.Prefabs;
using Game.Tools;
using Unity.Entities;

namespace CivicSurvival.Core.Systems.Placement
{
    /// <summary>
    /// Pause-safe generic building-placement service host. UI calls this synchronously
    /// from the trigger callback so the vanilla placement tool activates even while
    /// GameSimulation is paused (Axiom 14). Prefab resolution + tool activation + pending
    /// publication are generic; the per-kind pre-activation check (crew/funds/marker) is
    /// delegated to the registered <see cref="IBuildingPlacementHandler"/>.
    ///
    /// Both the tool activation and the <see cref="BuildingPlacementPending"/> publication
    /// finish in <see cref="TryActivatePlacement"/> before returning — do not convert this
    /// to an entity-command queue drained by OnUpdate, or the click appears to do nothing
    /// until unpause.
    /// </summary>
    // Shared infrastructure service host, created by SystemRegistrar's shared-infrastructure
    // block before any feature module loads (never feature-gated) — so it registers the
    // cross-cutting [InfrastructureService] IBuildingPlacementService (CIVIC463).
    [FrameworkSystem]
    [ActIndependent]
    public partial class BuildingPlacementCommandSystem : CivicSystemBase, IBuildingPlacementService
    {
        private static readonly LogContext Log = new("BuildingPlacementCommand");

        private PrefabSystem m_PrefabSystem = null!;
        private ToolSystem m_ToolSystem = null!;
        private EntityQuery m_PendingPlacementQuery;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_ToolSystem = World.GetOrCreateSystemManaged<ToolSystem>();
            m_PendingPlacementQuery = GetEntityQuery(ComponentType.ReadOnly<BuildingPlacementPending>());

            ServiceRegistry.Instance.Register<IBuildingPlacementService>(this);
            Log.Info("Created");
        }

        protected override void OnUpdateImpl()
        {
            // Service host only. Placement activation and pending publication are synchronous
            // so build placement works while the simulation is paused.
        }

        /// <summary>
        /// Activates the vanilla placement tool and publishes the pending placement marker
        /// synchronously. Do not convert this to an entity-command queue drained by
        /// <see cref="OnUpdateImpl"/>: placement is often started from UI while the
        /// simulation is paused, and the marker must exist before this call returns.
        /// </summary>
        public BuildingPlacementActivationResult TryActivatePlacement(
            BuildingPlacementKind kind,
            string prefabName,
            byte mode,
            RequestToken token)
        {
            if (!BuildingPlacementHandlerRegistry.TryGet(kind, out var handler))
                return Failure(ReasonId.None, $"No placement handler registered for kind: {kind}");

            if (!TryResolvePrefab(prefabName, out var foundPrefab))
                return Failure(ReasonId.None, $"Prefab not found: {prefabName}");

            if (!m_PrefabSystem.TryGetEntity(foundPrefab, out var prefabEntity))
                return Failure(ReasonId.None, $"No ECS entity for prefab: {foundPrefab.name}");

            if (!handler.TryPrepareActivation(World, foundPrefab, prefabEntity, mode, out var reasonId, out var message))
                return Failure(reasonId, message);

            // A third-party ToolSystem.EventPrefabChanged subscriber can throw while vanilla
            // notifies it of our prefab (FindIt's PrefabTrackingSystem throws
            // KeyNotFoundException categorising our modded prefab). Vanilla
            // ObjectToolSystem.set_prefab assigns m_SelectedPrefab and configures the tool BEFORE
            // firing EventPrefabChanged (decompile ObjectToolSystem.cs:2408-2440), so on a throw
            // the prefab is already set but the exception unwound ActivatePrefabTool before it
            // latched activeTool — leaving placement half-activated and our pending marker below
            // unpublished (the dead-click symptom). Re-activate once: the setter's idempotent
            // early-return (value == m_SelectedPrefab) skips the throwing notify the second time,
            // so activation completes without re-tripping the foreign subscriber.
            bool activated;
            try
            {
                activated = m_ToolSystem.ActivatePrefabTool(foundPrefab);
            }
            catch (Exception ex)
            {
                Log.Warn($"ActivatePrefabTool: a third-party ToolSystem.EventPrefabChanged subscriber threw; re-activating to complete placement for {foundPrefab.name}. {ex}");
                try
                {
                    activated = m_ToolSystem.ActivatePrefabTool(foundPrefab);
                }
                catch (Exception ex2)
                {
                    // Idempotent re-activation still threw — the foreign subscriber faults on a path
                    // the setter's early-return did not skip. Error (reaches telemetry) so a
                    // recurring third-party break is visible; the shared !activated return below
                    // reports the placement failure to the caller.
                    activated = false;
                    Log.Error($"ActivatePrefabTool threw twice via a foreign EventPrefabChanged subscriber for {foundPrefab.name}: {ex2}");
                }
            }

            if (!activated)
                return Failure(ReasonId.None, $"ActivatePrefabTool returned false or threw for {foundPrefab.name}");

            ArmRoadSideSnap();

            ReplacePendingImmediate(new BuildingPlacementPending
            {
                PrefabIndex = prefabEntity.Index,
                PrefabVersion = prefabEntity.Version,
                Kind = kind,
                Mode = mode,
                RequestId = token.RequestId,
                StartedFrame = UnityEngine.Time.frameCount,
                ToolDefaultSinceFrame = 0
            });

            Log.Info($"Activated placement tool for {foundPrefab.name} (kind={kind}, requestId={token.RequestId})");
            return BuildingPlacementActivationResult.Success();
        }

        /// <summary>
        /// Synchronous cancel, same pause-safe contract as activation (Axiom 14): the
        /// player clicks the active tile to put the tool away, possibly while paused.
        /// Only deactivates the tool — the pending request is completed by
        /// <c>BuildingPlacementLifecycleSystem</c> observing the Default tool with no
        /// matching Created entity, exactly as it does for a vanilla Esc cancel. Gated
        /// on OUR pending marker so a stale click cannot close an unrelated tool the
        /// player has since opened.
        /// </summary>
        public void CancelActivePlacement()
        {
            if (m_PendingPlacementQuery.IsEmptyIgnoreFilter)
                return;

            m_ToolSystem.activeTool = World.GetOrCreateSystemManaged<DefaultToolSystem>();
            Log.Info("Placement cancelled from UI — tool returned to Default; lifecycle completes the request.");
        }

        /// <summary>
        /// Arm the road-side preview snap for the just-activated placement. The vanilla
        /// snap state (<c>ObjectToolSystem.selectedSnap</c>) defaults to no toggleable bits
        /// until the player touches the snap options, and <c>NetSide</c> — the bit that pulls
        /// a RoadSide prefab's ghost to the roadside AND turns it to face the road — is
        /// toggleable (<c>GetAvailableSnapMask</c> puts it in both onMask and offMask). With
        /// the bit unarmed the preview neither snaps nor rotates and only the commit-side
        /// alignment squares the building up — visibly "wrong ghost, right building". The
        /// actual mask is <c>(selected | ~offMask) &amp; onMask</c>, so arming the bit is a
        /// no-op for prefabs whose mask does not offer NetSide.
        /// </summary>
        private void ArmRoadSideSnap()
        {
            var objectTool = World.GetOrCreateSystemManaged<ObjectToolSystem>();
            objectTool.selectedSnap |= Snap.NetSide;
        }

        private bool TryResolvePrefab(string prefabName, out PrefabBase foundPrefab)
        {
            foundPrefab = null!;
            if (!VanillaReflectionRegistry.TryGetPrefabSystemPrefabs(m_PrefabSystem, out var prefabs))
            {
                Log.Error("PrefabSystem.m_Prefabs is null");
                return false;
            }

            foreach (var prefab in prefabs)
            {
                if (prefab?.name == null || !prefab.name.Equals(prefabName, StringComparison.Ordinal))
                    continue;

                foundPrefab = prefab;
                return true;
            }

            return false;
        }

        private void ReplacePendingImmediate(BuildingPlacementPending pending)
        {
            // Immediate structural writes are part of the pause-safe placement contract.
            // A deferred ECS update would delay detector/lifecycle state until unpause.
            EntityManager.DestroyEntity(m_PendingPlacementQuery);

            var entity = EntityManager.CreateEntity();
            EntityManager.AddComponentData(entity, pending);
        }

        private static BuildingPlacementActivationResult Failure(ReasonId reasonId, string logMessage)
        {
            Log.Warn(logMessage);
            return BuildingPlacementActivationResult.Failure(reasonId, logMessage);
        }

        protected override void OnDestroy()
        {
            if (ServiceRegistry.IsInitialized)
                ServiceRegistry.Instance.Unregister<IBuildingPlacementService>(this);
            base.OnDestroy();
        }
    }
}
