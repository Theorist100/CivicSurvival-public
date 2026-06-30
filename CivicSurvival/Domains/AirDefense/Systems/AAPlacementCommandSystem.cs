using System;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Components.Domain.AirDefense;
using CivicSurvival.Core.Components.Requests;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces.Domain.Mobilization;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using Unity.Entities;

namespace CivicSurvival.Domains.AirDefense.Systems
{
    /// <summary>
    /// Pause-safe AA placement service host. UI calls this synchronously from the
    /// trigger callback so the vanilla placement tool activates even while
    /// GameSimulation is paused. This system intentionally does not stage placement
    /// through <see cref="OnUpdateImpl"/>: both vanilla tool activation and
    /// <see cref="AAPlacementPending"/> publication must finish in
    /// <see cref="TryActivatePlacement"/> before returning.
    /// </summary>
    [ActIndependent]
    [HandlesRequestKind(RequestKind.AirDefensePlacement)]
    public partial class AAPlacementCommandSystem : CivicSystemBase, IAAPlacementCommandService
    {
        private static readonly LogContext Log = new("AAPlacementCommand");

        private PrefabSystem m_PrefabSystem = null!;
        private ToolSystem m_ToolSystem = null!;
        private EntityQuery m_PendingPlacementQuery;
        private IMobilizationManpowerReader m_MobilizationReader = null!;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_ToolSystem = World.GetOrCreateSystemManaged<ToolSystem>();
            m_PendingPlacementQuery = GetEntityQuery(ComponentType.ReadOnly<AAPlacementPending>());

            ServiceRegistry.Instance.Register<IAAPlacementCommandService>(this);
            Log.Info("Created");
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            m_MobilizationReader = ServiceRegistry.Instance.Require<IMobilizationManpowerReader>();
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
        public AAPlacementActivationResult TryActivatePlacement(
            string prefabName,
            AAPlacementMode mode,
            RequestToken token)
        {
            if (!TryResolveAAPrefab(prefabName, out var foundPrefab))
                return Failure(ReasonIds.AaPlacementFailed, $"Prefab not found: {prefabName}");

            if (!m_PrefabSystem.TryGetEntity(foundPrefab, out var prefabEntity))
                return Failure(ReasonIds.AaPlacementFailed, $"No ECS entity for prefab: {foundPrefab.name}");

            if (!EntityManager.HasComponent<AirDefensePrefabData>(prefabEntity))
                return Failure(ReasonIds.AaPlacementFailed, $"AirDefensePrefabData marker missing: {foundPrefab.name}");

            var prefabData = EntityManager.GetComponentData<AirDefensePrefabData>(prefabEntity);
            int crewRequired = mode == AAPlacementMode.Heritage
                ? AAParams.ForType(BalanceConfig.Current, AAType.HeritageBofors).CrewRequired
                : prefabData.CrewRequired;

            if (!m_MobilizationReader.CanRecruit(crewRequired))
                return Failure(
                    ReasonIds.AaInsufficientManpower,
                    $"Insufficient manpower: {crewRequired} required, {m_MobilizationReader.AvailableManpower} available");

            if (!m_ToolSystem.ActivatePrefabTool(foundPrefab))
                return Failure(ReasonIds.AaPlacementFailed, $"ActivatePrefabTool returned false for {foundPrefab.name}");

            ReplaceAAPlacementPendingImmediate(new AAPlacementPending
            {
                PrefabIndex = prefabEntity.Index,
                PrefabVersion = prefabEntity.Version,
                Mode = mode,
                RequestId = token.RequestId,
                StartedFrame = UnityEngine.Time.frameCount,
                ToolDefaultSinceFrame = 0
            });

            Log.Info($"Activated placement tool for {foundPrefab.name} (requestId={token.RequestId})");
            return AAPlacementActivationResult.Success();
        }

        private bool TryResolveAAPrefab(string prefabName, out PrefabBase foundPrefab)
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

        private void ReplaceAAPlacementPendingImmediate(AAPlacementPending pending)
        {
            // Immediate structural writes are part of the pause-safe placement contract.
            // A deferred ECS update would delay detector/lifecycle state until unpause.
            EntityManager.DestroyEntity(m_PendingPlacementQuery);

            var entity = EntityManager.CreateEntity();
            EntityManager.AddComponentData(entity, pending);
        }

        private static AAPlacementActivationResult Failure(ReasonId reasonId, string logMessage)
        {
            Log.Warn(logMessage);
            return AAPlacementActivationResult.Failure(reasonId, logMessage);
        }

        protected override void OnDestroy()
        {
            if (ServiceRegistry.IsInitialized)
                ServiceRegistry.Instance.Unregister<IAAPlacementCommandService>(this);
            base.OnDestroy();
        }
    }
}
