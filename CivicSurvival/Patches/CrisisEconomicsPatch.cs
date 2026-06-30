using System;
using Game.Simulation;
using HarmonyLib;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using CivicSurvival.Core.Systems;
using CivicSurvival.Domains.Economics;
using CivicSurvival.Domains.Economics.Systems;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Patches
{
    /// <summary>
    /// Harmony patches for crisis economics.
    /// Applies economic multipliers during Crisis act to vanilla systems.
    ///
    /// Patched systems:
    /// - <c>TaxSystem</c>: reduces tax income via <c>m_TaxPaidMultiplier</c> field (Prefix).
    /// - <c>CommercialDemandSystem.GetBuildingDemands</c> /
    ///   <c>GetResourceDemands</c> (Postfix): returns scaled per-resource copies
    ///   without mutating vanilla's serialized backing NativeArrays.
    /// - <c>CommercialDemandSystem.companyDemand</c> / <c>buildingDemand</c> property
    ///   getters (Postfix): post-read transform of <c>m_LastCompanyDemand</c> /
    ///   <c>m_LastBuildingDemand</c>. We do NOT write the backing field — vanilla
    ///   serializes those ints (lines 486-489 of decompiled CommercialDemandSystem),
    ///   so writing them would leak our scale into the save file. By transforming
    ///   only the returned value, downstream readers (CommercialSpawnSystem,
    ///   ZoneSpawnSystem, UI) see the scaled demand while the persisted field stays
    ///   at vanilla's raw value.
    ///
    /// Note: Loans and Tourism are handled directly via ECS components
    /// in CrisisEconomicsSystem (no patch needed).
    ///
    /// Note: <c>IndustrialDemandSystem</c> exposes an identical
    /// <c>GetResourceDemands</c>/<c>GetBuildingDemands</c> surface but is
    /// intentionally NOT patched. Crisis economics scales commerce, tax, tourism
    /// and loans only — industrial supply chain is left at vanilla rates.
    /// </summary>
    public static class CrisisEconomicsPatch
    {
        private static readonly LogContext Log = new("CrisisEconomicsPatch");

        // M3.4: Version resilience - fallback method names for system updates
        private static readonly string[] SystemUpdateCandidates = { "OnUpdate", "Update", "OnSystemUpdate" };

        /// <summary>
        /// Runtime validation - tracks whether TaxPatch is active.
        /// </summary>
        public static bool IsTaxPatchActive { get; private set; }

        /// <summary>
        /// Runtime validation - tracks whether CommercePatch (OnUpdate postfix) is active.
        /// </summary>
        public static bool IsCommercePatchActive { get; private set; }

        /// <summary>
        /// Runtime validation - tracks whether commerce demand getter postfixes are active.
        /// Both getters (<c>companyDemand</c>, <c>buildingDemand</c>) must succeed.
        /// </summary>
        public static bool IsCommerceDemandGettersActive { get; private set; }

        /// <summary>
        /// Cleanup process-local patch state. Call from Mod.OnDispose() with the
        /// active world (used for hot-reload safety net — restores tax multiplier
        /// via handler if world is still alive).
        /// </summary>
        public static void Cleanup(World? world = null)
        {
            try
            {
                if (world != null && world.IsCreated)
                {
                    var handler = world.GetExistingSystemManaged<CivicSurvival.Core.Systems.TaxPatchHandlerSystem>();
                    handler?.RestoreSavedMultiplier();
                }
                CommercePatch.Cleanup();
            }
            finally
            {
                IsTaxPatchActive = false;
                IsCommercePatchActive = false;
                IsCommerceDemandGettersActive = false;
                Log.Info("Cleanup complete");
            }
        }

        /// <summary>
        /// Apply all crisis economics patches.
        /// </summary>
        public static void Apply(Harmony harmony)
        {
            Log.Info("Applying crisis economics patches...");

            ApplyTaxPatch(harmony);
            ApplyCommercePatch(harmony);
            ApplyCommerceDemandGetters(harmony);

            Log.Info("Crisis economics patches applied.");
        }

        /// <summary>
        /// Apply Tax patch - modifies m_TaxPaidMultiplier before TaxSystem.OnUpdate runs.
        /// </summary>
        private static void ApplyTaxPatch(Harmony harmony)
        {
            if (IsTaxPatchActive)
            {
                Log.Info("Tax patch apply skipped — already active");
                return;
            }

            var targetType = typeof(TaxSystem);
            var prefix = new HarmonyMethod(typeof(TaxPatch), nameof(TaxPatch.Prefix));
            bool patched = false;
            System.Exception? lastException = null;

            foreach (var methodName in SystemUpdateCandidates)
            {
                var method = AccessTools.Method(targetType, methodName);
                if (method == null) continue;

                try
                {
                    harmony.Patch(method, prefix: prefix);
                }
                catch (System.Exception ex)
                {
                    lastException = ex;
                    Log.Exception($"harmony.Patch threw for {targetType.Name}.{methodName}", ex);
                    continue;
                }

                Log.Info($"Patched {targetType.Name}.{methodName} for crisis tax multiplier");
                IsTaxPatchActive = true;
                patched = true;
                PatchStatusTracker.ReportSuccess("TaxPatch");
                break;
            }

            if (!patched)
            {
                IsTaxPatchActive = false;
                string reason = lastException != null
                    ? $"harmony.Patch failed: {lastException.GetType().Name}: {lastException.Message}"
                    : $"No matching method found. Tried: {string.Join(", ", SystemUpdateCandidates)}";
                PatchStatusTracker.ReportFailure("TaxPatch", reason);
            }
        }

        /// <summary>
        /// Apply Commerce patch - modifies published demand surfaces at read time.
        /// </summary>
        private static void ApplyCommercePatch(Harmony harmony)
        {
            if (IsCommercePatchActive)
            {
                Log.Info("Commerce patch apply skipped — already active");
                return;
            }

            var targetType = typeof(CommercialDemandSystem);
            bool updateOk = TryPatchMethod(harmony, targetType, "OnUpdate",
                new HarmonyMethod(typeof(CommercePatch), nameof(CommercePatch.OnUpdate_Postfix)));
            bool resourceOk = TryPatchMethod(harmony, targetType, "GetResourceDemands",
                new HarmonyMethod(typeof(CommercePatch), nameof(CommercePatch.ResourceDemandsGetter_Postfix)));
            bool buildingOk = TryPatchMethod(harmony, targetType, "GetBuildingDemands",
                new HarmonyMethod(typeof(CommercePatch), nameof(CommercePatch.BuildingDemandsGetter_Postfix)));

            if (updateOk && resourceOk && buildingOk)
            {
                IsCommercePatchActive = true;
                PatchStatusTracker.ReportSuccess("CommercePatch");
            }
            else
            {
                IsCommercePatchActive = false;
                PatchStatusTracker.ReportFailure("CommercePatch",
                    $"update={updateOk}, resource={resourceOk}, building={buildingOk}");
            }
        }

        /// <summary>
        /// Apply post-read transforms on <c>companyDemand</c> / <c>buildingDemand</c>
        /// property getters. Both must succeed for the patch group to be "active".
        /// </summary>
        private static void ApplyCommerceDemandGetters(Harmony harmony)
        {
            if (IsCommerceDemandGettersActive)
            {
                Log.Info("Commerce demand getter patches apply skipped — already active");
                return;
            }

            var targetType = typeof(CommercialDemandSystem);
            bool companyOk = TryPatchGetter(harmony, targetType, "companyDemand",
                new HarmonyMethod(typeof(CommercePatch), nameof(CommercePatch.CompanyDemandGetter_Postfix)));
            bool buildingOk = TryPatchGetter(harmony, targetType, "buildingDemand",
                new HarmonyMethod(typeof(CommercePatch), nameof(CommercePatch.BuildingDemandGetter_Postfix)));

            if (companyOk && buildingOk)
            {
                IsCommerceDemandGettersActive = true;
                PatchStatusTracker.ReportSuccess("CommerceDemandGetters");
            }
            else
            {
                IsCommerceDemandGettersActive = false;
                PatchStatusTracker.ReportFailure("CommerceDemandGetters",
                    $"company={companyOk}, building={buildingOk}");
            }
        }

        private static bool TryPatchGetter(Harmony harmony, Type targetType, string propertyName, HarmonyMethod postfix)
        {
            var getter = AccessTools.PropertyGetter(targetType, propertyName);
            if (getter == null)
            {
                Log.Warn($"Property getter {targetType.Name}.{propertyName} not found");
                return false;
            }
            try
            {
                harmony.Patch(getter, postfix: postfix);
                Log.Info($"Patched {targetType.Name}.{propertyName} getter for crisis demand scaling");
                return true;
            }
            catch (System.Exception ex)
            {
                Log.Exception($"harmony.Patch threw for {targetType.Name}.{propertyName} getter", ex);
                return false;
            }
        }

        private static bool TryPatchMethod(Harmony harmony, Type targetType, string methodName, HarmonyMethod postfix)
        {
            var method = AccessTools.Method(targetType, methodName);
            if (method == null)
            {
                Log.Warn($"Method {targetType.Name}.{methodName} not found");
                return false;
            }
            try
            {
                harmony.Patch(method, postfix: postfix);
                Log.Info($"Patched {targetType.Name}.{methodName} for crisis demand scaling");
                return true;
            }
            catch (System.Exception ex)
            {
                Log.Exception($"harmony.Patch threw for {targetType.Name}.{methodName}", ex);
                return false;
            }
        }
    }

    /// <summary>
    /// Tax system patch - reduces tax income during Crisis act.
    ///
    /// TaxSystem uses m_TaxPaidMultiplier (float3) to scale tax payments:
    /// - x = residential tax multiplier
    /// - y = commercial tax multiplier
    /// - z = industrial tax multiplier
    ///
    /// During crisis, we set all to SHOCK_TAX_MULTIPLIER (0.2 = 20% of normal).
    /// </summary>
    public static class TaxPatch
    {
        private static readonly LogContext Log = new("TaxPatch");

        /// <summary>
        /// Prefix patch for TaxSystem.OnUpdate. Pure bridge — looks up the per-World
        /// handler and delegates. State and reflection cache live in
        /// <see cref="CivicSurvival.Core.Systems.TaxPatchHandlerSystem"/>.
        /// </summary>
        public static void Prefix(TaxSystem __instance)
        {
            using var _ = PerformanceProfiler.Measure("Patch.CrisisTax");

            try
            {
                if (__instance == null)
                    return;

                var world = __instance.World;
                if (world == null)
                    return;

                float taxMultiplier = CrisisEconomicsAdapter.GetTaxMultiplier(world);

                var handler = world.GetExistingSystemManaged<CivicSurvival.Core.Systems.TaxPatchHandlerSystem>();
                handler?.ApplyTaxMultiplier(__instance, taxMultiplier);
            }
            catch (System.Exception ex)
            {
                // Critical: Log but NEVER rethrow - patch must never crash game
                Log.Exception("Prefix error (patch disabled this frame)", ex);
            }
        }
    }

    /// <summary>
    /// Commercial demand patch — reduces commercial demand during Crisis act.
    ///
    /// <para>CommercialDemandSystem publishes demand via:</para>
    /// <list type="bullet">
    ///   <item><c>companyDemand => m_LastCompanyDemand</c> (aggregate, guards CommercialSpawnSystem) — handled by getter postfix below.</item>
    ///   <item><c>buildingDemand => m_LastBuildingDemand</c> (aggregate, guards ZoneSpawnSystem + UI) — handled by getter postfix below.</item>
    ///   <item><c>GetBuildingDemands()</c> → vanilla building-demand NativeArray (per-resource, drives spawn job) — handled by method postfix below.</item>
    ///   <item><c>GetResourceDemands()</c> → vanilla resource-demand NativeArray — handled by method postfix below.</item>
    /// </list>
    ///
    /// <para>Demand-array scaling design:</para>
    /// <list type="bullet">
    ///   <item><b>NativeArrays:</b> vanilla serializes its demand backing arrays.
    ///   We therefore never mutate them. Getter postfixes complete vanilla's write
    ///   dependency, then build a per-call scaled copy from <c>World.UpdateAllocator</c>
    ///   synchronously on the main thread (the arrays are per-resource sized, so an
    ///   async job buys nothing and risks outliving the rewindable allocator). Vanilla
    ///   spawn jobs consume reader-owned temporary storage rather than shared mod
    ///   Persistent storage.</item>
    ///   <item><b><c>m_Last*Demand</c> ints:</b> vanilla serializes these too (lines
    ///   486-489). Getter postfixes transform only the returned value — backing
    ///   fields stay raw and save-clean.</item>
    /// </list>
    /// </summary>
    public static class CommercePatch
    {
        private static readonly LogContext Log = new("CommercePatch");

        /// <summary>
        /// Cleanup hook retained for symmetry with <c>CrisisEconomicsPatch.Cleanup</c>.
        /// Demand-array copies are allocated from <c>World.UpdateAllocator</c>, so no
        /// process-lifetime cleanup is required here.
        /// </summary>
        public static void Cleanup()
        {
            // Intentionally empty: per-call demand copies are update-allocator owned.
        }

        /// <summary>
        /// Postfix on <c>CommercialDemandSystem.OnUpdate()</c>. Retained as part of
        /// the patch group so status tracking still verifies the vanilla update
        /// surface, but demand-array scaling now happens entirely in the getter
        /// postfixes with per-call update-allocator copies.
        /// </summary>
        public static void OnUpdate_Postfix(CommercialDemandSystem __instance)
        {
            try
            {
                var world = __instance?.World;
                if (world == null || !world.IsCreated)
                    return;
            }
            catch (System.Exception ex)
            {
                Log.Exception("OnUpdate postfix bridge error", ex);
            }
        }

        /// <summary>
        /// Postfix on <c>CommercialDemandSystem.companyDemand</c> getter. Scales the
        /// returned value by the current commerce multiplier. The backing field
        /// <c>m_LastCompanyDemand</c> is NOT modified — preserves clean save state
        /// (vanilla serializes the raw field).
        /// </summary>
        public static void CompanyDemandGetter_Postfix(CommercialDemandSystem __instance, ref int __result)
        {
            try
            {
                var world = __instance?.World;
                if (world == null) return;
                float m = CrisisEconomicsAdapter.GetCommerceMultiplier(world);
                if (m < 1f) __result = (int)Math.Round(__result * m);
            }
            catch (System.Exception ex)
            {
                Log.Exception("companyDemand getter postfix error (returning vanilla value)", ex);
            }
        }

        /// <summary>
        /// Postfix on <c>CommercialDemandSystem.buildingDemand</c> getter. Same shape
        /// as <see cref="CompanyDemandGetter_Postfix"/>.
        /// </summary>
        public static void BuildingDemandGetter_Postfix(CommercialDemandSystem __instance, ref int __result)
        {
            try
            {
                var world = __instance?.World;
                if (world == null) return;
                float m = CrisisEconomicsAdapter.GetCommerceMultiplier(world);
                if (m < 1f) __result = (int)Math.Round(__result * m);
            }
            catch (System.Exception ex)
            {
                Log.Exception("buildingDemand getter postfix error (returning vanilla value)", ex);
            }
        }

        /// <summary>
        /// Postfix on <c>CommercialDemandSystem.GetResourceDemands(out deps)</c>.
        /// Returns a scaled copy so the serialized backing array remains raw.
        /// </summary>
        public static void ResourceDemandsGetter_Postfix(
            CommercialDemandSystem __instance,
            ref NativeArray<int> __result,
            ref JobHandle deps)
        {
            ScaleDemandGetterResult(__instance, ref __result, ref deps, "GetResourceDemands");
        }

        /// <summary>
        /// Postfix on <c>CommercialDemandSystem.GetBuildingDemands(out deps)</c>.
        /// Returns a scaled copy so the serialized backing array remains raw.
        /// </summary>
        public static void BuildingDemandsGetter_Postfix(
            CommercialDemandSystem __instance,
            ref NativeArray<int> __result,
            ref JobHandle deps)
        {
            ScaleDemandGetterResult(__instance, ref __result, ref deps, "GetBuildingDemands");
        }

        private static void ScaleDemandGetterResult(
            CommercialDemandSystem instance,
            ref NativeArray<int> result,
            ref JobHandle deps,
            string surface)
        {
            try
            {
                if (instance == null || !result.IsCreated)
                    return;

                var world = instance.World;
                if (world == null)
                    return;

                float multiplier = CrisisEconomicsAdapter.GetCommerceMultiplier(world);
                if (multiplier >= 1f)
                    return;

                // Return a scaled copy without mutating vanilla's serialized backing
                // array and without stalling the main thread. Schedule a small copy
                // job after vanilla's incoming write dependency and hand its handle
                // back as `deps` — exactly the contract vanilla's getter already uses,
                // so consumers (ZoneSpawnSystem / CommercialSpawnSystem) chain their
                // read jobs on it. No Complete() here ⇒ no sync point during Crisis.
                //
                // The copy MUST be allocated via CollectionHelper.CreateNativeArray.
                // World.UpdateAllocator is a custom rewindable allocator; the plain
                // `new NativeArray<int>(len, allocator, …)` constructor only understands
                // the built-in Temp/TempJob/Persistent enums and silently yields a null
                // buffer for a custom allocator handle — the job's first element write
                // then threw a NullReferenceException. That was the original crash.
                var scaled = CollectionHelper.CreateNativeArray<int>(
                    result.Length,
                    world.UpdateAllocator.ToAllocator,
                    NativeArrayOptions.UninitializedMemory);
                var job = new ScaleDemandArrayJob
                {
                    Source = result,
                    Destination = scaled,
                    Multiplier = multiplier,
                };

                deps = job.Schedule(deps);
                result = scaled;
            }
            catch (System.Exception ex)
            {
                Log.Exception($"{surface} postfix error (returning vanilla demand array)", ex);
            }
        }

        // Copies vanilla's demand array into a scaled per-call copy. Tiny (~per-resource
        // sized), so a plain IJob — no IJobParallelFor range overhead. Reads run after
        // vanilla's write dependency (passed to Schedule); the returned handle gates
        // consumers, so vanilla's backing array is never read mid-write.
        private struct ScaleDemandArrayJob : IJob
        {
            [ReadOnly] public NativeArray<int> Source;
            [WriteOnly] public NativeArray<int> Destination;
            public float Multiplier;

            public void Execute()
            {
                for (int i = 0; i < Source.Length; i++)
                    Destination[i] = (int)math.round(Source[i] * Multiplier);
            }
        }
    }
}
