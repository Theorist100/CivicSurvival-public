using HarmonyLib;
using Game;
using Game.Simulation;
using Unity.Entities;
using CivicSurvival.Core.Utils;
using CivicSurvival.Core.Events;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Components.Domain.Economy;
using CivicSurvival.Core.Components.CrossDomain;

namespace CivicSurvival.Patches
{
    /// <summary>
    /// Harmony patches for electricity system.
    /// Used to intercept and modify vanilla game calculations.
    /// </summary>
    public static class ElectricityPatch
    {
        private static readonly LogContext Log = new("ElectricityPatch");

        // Candidate method names for version resilience
        private static readonly string[] MethodCandidates = { "OnUpdate", "Update", "OnSystemUpdate", "OnElectricityUpdate" };

        /// <summary>
        /// SYS-004: Runtime validation - tracks whether IsolatedGridPatch is active.
        /// </summary>
        public static bool IsIsolatedGridPatchActive { get; private set; }

        /// <summary>
        /// HarmonyPrepare: Check if any target method exists before applying patch.
        /// Returns false if no valid target found (patch will not apply).
        /// </summary>
        [HarmonyPrepare]
        public static bool Prepare()
        {
            foreach (var name in MethodCandidates)
            {
                if (AccessTools.Method(typeof(ElectricityFlowSystem), name) != null)
                    return true;
            }
            Log.Warn("No target method found - patch will not apply");
            return false;
        }

        /// <summary>
        /// Apply all electricity-related patches.
        /// </summary>
        public static void Apply(Harmony harmony)
        {
            Log.Info("Applying electricity patches...");

            // Patch ElectricityFlowSystem to limit imports
            ApplyIsolatedGridPatch(harmony);

            Log.Info("Electricity patches applied.");
        }

        /// <summary>
        /// Cleanup resources before unpatch. Call from Mod.OnDispose() with the
        /// active world (used by IsolatedGridPatch for handler-restore safety net).
        /// </summary>
        public static void Cleanup(World? world = null)
        {
            IsolatedGridPatch.Cleanup(world);
            IsIsolatedGridPatchActive = false;
        }

        /// <summary>
        /// Apply Isolated Grid patch - limits electricity import from outside connections.
        /// SYS-004: Multi-target patching for game version resilience.
        /// </summary>
        private static void ApplyIsolatedGridPatch(Harmony harmony)
        {
            if (IsIsolatedGridPatchActive)
            {
                Log.Info("Isolated Grid patch apply skipped — already active");
                return;
            }

            // SYS-004: Try multiple method names (version resilience)
            var targetType = typeof(ElectricityFlowSystem);
            var postfix = new HarmonyMethod(typeof(IsolatedGridPatch), nameof(IsolatedGridPatch.OnElectricityFlowPostfix));
            bool patched = false;
            System.Exception? lastException = null;

            foreach (var methodName in MethodCandidates)
            {
                var method = AccessTools.Method(targetType, methodName);
                if (method == null) continue;

                try
                {
                    harmony.Patch(method, postfix: postfix);
                }
                catch (System.Exception ex)
                {
                    // harmony.Patch can throw on IL conflict / signature mismatch — never let
                    // it escape and abort the whole Mod.OnLoad (other patches must still run).
                    lastException = ex;
                    Log.Exception($"harmony.Patch threw for {targetType.Name}.{methodName}", ex);
                    continue;
                }

                Log.Info($"Patched {targetType.Name}.{methodName} for Isolated Grid");
                IsIsolatedGridPatchActive = true;
                patched = true;
                PatchStatusTracker.ReportSuccess("IsolatedGridPatch");
                break;
            }

            if (!patched)
            {
                // SYS-004: Make patch failure visible (runtime validation)
                IsIsolatedGridPatchActive = false;
                string reason = lastException != null
                    ? $"harmony.Patch failed: {lastException.GetType().Name}: {lastException.Message}"
                    : $"No matching method found. Tried: {string.Join(", ", MethodCandidates)}";
                PatchStatusTracker.ReportFailure("IsolatedGridPatch", reason);
            }
        }
    }

    /// <summary>
    /// Isolated Grid patch - limits electricity import from outside connections.
    /// Makes electricity a scarce resource that requires planning.
    ///
    /// Import limit = BaseLimit (100 MW) + ShadowImportMW (from corruption)
    /// Shadow Import is the ONLY way to bypass the base limit.
    /// REFACTORED: Reads ShadowImportMW from ShadowImportState ECS singleton.
    /// </summary>
    public static class IsolatedGridPatch
    {
        private static readonly LogContext Log = new("IsolatedGridPatch");

        /// <summary>
        /// Cleanup — resets <c>ImportCapRuntimeState</c> via the handler. Harmony
        /// unpatching is owned by <c>Mod.OnDispose</c> through <c>UnpatchAll</c>;
        /// this method only tears down process/runtime state. On hot-reload (world
        /// still alive) the handler hasn't run OnDestroy yet; without explicit reset,
        /// <c>PowerCapacityMath</c> would keep reading the last-published import cap
        /// from the static state after mod unload.
        /// </summary>
        public static void Cleanup(World? world = null)
        {
            if (world != null && world.IsCreated)
            {
                var handler = world.GetExistingSystemManaged<CivicSurvival.Core.Systems.IsolatedGridHandlerSystem>();
                handler?.ResetForUnload();
                RestoreExportEdgeCapacities(world);
            }
            // Always clear static import-cap state. `handler?.ResetForUnload()` is a
            // no-op when the handler is missing (boot race / not registered) — without
            // this unconditional reset, PowerCapacityMath would keep reading the
            // last-published cap after mod unload.
            CivicSurvival.Core.Utils.ImportCapRuntimeState.Reset();

            Log.Info("Cleanup complete");
        }

        /// <summary>
        /// Hot-reload safety net: give every export trade edge (tradeNode → sinkNode)
        /// its vanilla max capacity back. Vanilla writes the trade-edge capacity exactly
        /// once on Created and never rewrites it, so a cap left behind by the mod would
        /// survive in the running world and in any save made from it. A full uninstall
        /// cannot run this code — a save made with cap 0 keeps export at zero; that is
        /// an accepted, documented allowance (see Generation_Spam_Defense). Direct
        /// EntityManager data write: unload runs on the main thread and the resolver's
        /// ECB infrastructure is no longer available here.
        /// </summary>
        private static void RestoreExportEdgeCapacities(World world)
        {
            try
            {
                // GetOrCreate: the vanilla flow system always exists in a created game world,
                // and the GetOrCreate path cannot return null (CIVIC468 mandatory-host pattern,
                // same as the resolver's OnCreate resolve).
                var flowSystem = world.GetOrCreateSystemManaged<ElectricityFlowSystem>();
                Entity sinkNode = flowSystem.sinkNode;
                if (sinkNode == Entity.Null)
                {
                    Log.Info("Export-edge restore skipped: flow graph has no sink node (flow system never ran in this world)");
                    return;
                }

                var em = world.EntityManager;
                using var markerQuery = em.CreateEntityQuery(
                    ComponentType.ReadOnly<Game.Objects.ElectricityOutsideConnection>(),
                    ComponentType.ReadOnly<Game.Common.Owner>(),
                    ComponentType.Exclude<Game.Tools.Temp>(),
                    ComponentType.Exclude<Game.Common.Deleted>());
                using var markers = markerQuery.ToEntityArray(Unity.Collections.Allocator.Temp);
                int restored = 0;
                for (int i = 0; i < markers.Length; i++)
                {
                    var owner = em.GetComponentData<Game.Common.Owner>(markers[i]);
                    if (!em.HasComponent<ElectricityNodeConnection>(owner.m_Owner))
                        continue;
                    Entity node = em.GetComponentData<ElectricityNodeConnection>(owner.m_Owner).m_ElectricityNode;
                    if (!em.HasBuffer<ConnectedFlowEdge>(node))
                        continue;
                    var edges = em.GetBuffer<ConnectedFlowEdge>(node, isReadOnly: true);
                    for (int j = 0; j < edges.Length; j++)
                    {
                        Entity edgeEntity = edges[j].m_Edge;
                        if (!em.HasComponent<ElectricityFlowEdge>(edgeEntity))
                            continue;
                        var edge = em.GetComponentData<ElectricityFlowEdge>(edgeEntity);
                        if (edge.m_Start != node || edge.m_End != sinkNode)
                            continue;
                        if (edge.m_Capacity == ElectricityFlowSystem.kMaxEdgeCapacity)
                            continue;
                        edge.m_Capacity = ElectricityFlowSystem.kMaxEdgeCapacity;
                        em.SetComponentData(edgeEntity, edge);
                        restored++;
                    }
                }

                if (restored > 0)
                    Log.Info($"Restored vanilla max capacity on {restored} export trade edge(s)");
            }
            catch (System.Exception ex)
            {
                Log.Exception("Export edge capacity restore failed (non-fatal)", ex);
            }
        }

        /// <summary>
        /// Postfix patch for ElectricityFlowSystem.OnUpdate. Pure Harmony bridge —
        /// looks up the per-World handler and delegates. State, EntityQuery, and
        /// import-cap logic live in <see cref="CivicSurvival.Core.Systems.IsolatedGridHandlerSystem"/>.
        /// </summary>
        public static void OnElectricityFlowPostfix(ElectricityFlowSystem __instance)
        {
            using var _ = PerformanceProfiler.Measure("Patch.IsolatedGrid");

            try
            {
                if (__instance == null)
                {
                    Log.Error("ElectricityFlowSystem instance is null");
                    return;
                }

                var world = __instance.World;
                if (world == null)
                    return;

                ServiceRegistry registry;
                try
                {
                    if (!ServiceRegistry.IsInitialized)
                        return;
                    registry = ServiceRegistry.Instance;
                }
                catch (System.InvalidOperationException)
                {
                    return;
                }

                var handler = world.GetExistingSystemManaged<CivicSurvival.Core.Systems.IsolatedGridHandlerSystem>();
                handler?.PublishImportLimit(registry);
            }
            catch (System.Exception ex)
            {
                Log.Exception("Postfix error (patch disabled this frame)", ex);
            }
        }

    }
}
