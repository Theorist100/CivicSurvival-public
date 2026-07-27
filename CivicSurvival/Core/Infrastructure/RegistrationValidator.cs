using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Game;
using Unity.Entities;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Core.Infrastructure
{
    /// <summary>
    /// Tracks all RegisterAt calls and validates no CivicSystemBase subclasses are orphaned.
    ///
    /// Every CivicSystemBase subclass MUST be registered via updateSystem.RegisterAt
    /// in a Domain or SystemRegistrar. Systems without registration compile and OnCreate runs,
    /// but OnUpdate is NEVER called — making them silently dead.
    ///
    /// Call Validate() after all registrations to detect orphans at startup.
    /// </summary>
    internal static class RegistrationValidator
    {
        private static readonly LogContext Log = new("RegistrationValidator");
        private static readonly HashSet<Type> s_Registered = new();
        // Every (system, phase) we register, for the vanilla-misphasing self-check in Validate().
        private static readonly List<(Type Type, SystemUpdatePhase Phase)> s_RegisteredWithPhase = new();
        private static readonly object s_Lock = new();

        internal static void Track(Type type, SystemUpdatePhase phase)
        {
            bool duplicate;
            lock (s_Lock)
            {
                duplicate = !s_Registered.Add(type);
                s_RegisteredWithPhase.Add((type, phase));
            }
            if (duplicate)
            {
                Log.Error(
                    $"Duplicate ECS registration for {type.FullName}. " +
                    "Vanilla UpdateSystem does not deduplicate regular systems; " +
                    "a second RegisterAfter/RegisterBefore/RegisterAt can schedule OnUpdate more than once.");
            }
        }

        internal static void Clear()
        {
            lock (s_Lock)
            {
                s_Registered.Clear();
                s_RegisteredWithPhase.Clear();
            }
        }

        /// <summary>
        /// Scans assembly for all concrete CivicSystemBase subclasses.
        /// Any not in the registered set are logged as ORPHANED.
        /// </summary>
        internal static void Validate()
        {
            var civicBase = typeof(CivicSystemBase);
            var civicUIBase = typeof(CivicUISystemBase);
            var allTypes = GetTypesWithPartialLoadFallback(civicBase.Assembly)
                .Where(t => !t.IsAbstract
                    && (civicBase.IsAssignableFrom(t) || civicUIBase.IsAssignableFrom(t)))
                .ToList();

            HashSet<Type> registeredSnapshot;
            lock (s_Lock)
            {
                registeredSnapshot = new HashSet<Type>(s_Registered);
            }

            var missing = allTypes.Where(t => !registeredSnapshot.Contains(t)).ToList();
            var orphans = new List<Type>();
            var unavailable = new List<(Type Type, string FeatureId, FeatureUnavailableReason Reason)>();

            foreach (var type in missing)
            {
                var typeName = type.FullName;
                if (string.IsNullOrEmpty(typeName)
                    || !GeneratedFeatureSystemOwners.Owners.TryGetValue(typeName, out var featureId))
                {
                    orphans.Add(type);
                    continue;
                }

                if (!FeatureRegistry.IsInitialized)
                {
                    orphans.Add(type);
                    continue;
                }

                var registry = FeatureRegistry.Instance;
                if (!registry.IsKnownFeatureId(featureId))
                {
                    orphans.Add(type);
                    continue;
                }

                if (registry.IsUnavailable(featureId, out var reason)
                    && IsExpectedUnavailable(reason))
                {
                    unavailable.Add((type, featureId, reason));
                    continue;
                }

                orphans.Add(type);
            }

            if (orphans.Count > 0)
            {
                Log.Error($"╔══ ORPHANED SYSTEMS ({orphans.Count}) — no RegisterAt registration! ══╗");
                foreach (var t in orphans)
                {
                    var typeName = t.FullName;
                    if (!string.IsNullOrEmpty(typeName)
                        && GeneratedFeatureSystemOwners.Owners.TryGetValue(typeName, out var featureId))
                    {
                        Log.Error($"║  {t.Name} (feature {featureId})");
                    }
                    else
                    {
                        Log.Error($"║  {t.Name}");
                    }
                }
                Log.Error("╚══ Fix: add updateSystem.RegisterAt<T>() in Domain or SystemRegistrar ══╝");
            }
            else
            {
                Log.Info($"All {allTypes.Count - unavailable.Count} executable CivicSystemBase systems registered");
                Log.Info($"[FEATURE-VERIFY] registeredExecutableSystems={allTypes.Count - unavailable.Count}");
            }

            if (unavailable.Count > 0)
            {
                Log.Info($"Skipped {unavailable.Count} systems owned by unavailable features");
                Log.Info($"[FEATURE-VERIFY] gatedSystems={unavailable.Count}");
                foreach (var (type, featureId, reason) in unavailable)
                {
                    Log.Info($"  gated: {type.Name} (feature {featureId}, reason={reason})");
                    Log.Info($"[FEATURE-VERIFY] system={type.FullName} feature={featureId} reason={reason} registered=false");
                }
            }

            LogVanillaRegistrationSelfCheck();

            LogSoftFeatureEdges();
        }

        // Self-check: we must NEVER register a vanilla (Game.*) system into any phase.
        // Doing so mis-phases a vanilla ECB producer (e.g. UpdateGroupSystem into
        // GameSimulation) → "Trying to create EntityCommandBuffer when it's not allowed!"
        // every frame it has work, plus the leaked TempJob chunk list. This catches our
        // DIRECT RegisterAt/After/Before calls at registration time; SystemOrderAuditSystem
        // catches the resolved post-Refresh order (incl. indirect RefMap placement).
        private static void LogVanillaRegistrationSelfCheck()
        {
            List<(Type Type, SystemUpdatePhase Phase)> vanilla;
            lock (s_Lock)
            {
                vanilla = s_RegisteredWithPhase
                    .Where(r => r.Type != null
                        && r.Type.Assembly == typeof(UpdateSystem).Assembly
                        && !IsOwnedBarrierGateOpener(r.Type))
                    .ToList();
            }

            if (vanilla.Count == 0)
            {
                Log.Info("[ORDER-AUDIT] registration self-check clean — no vanilla systems registered by us");
                return;
            }

            foreach (var (type, phase) in vanilla)
            {
                Log.Error(
                    $"We registered a VANILLA system '{type.FullName}' into phase '{phase}'. " +
                    "Mods must never register vanilla systems — this mis-phases a vanilla ECB producer and causes " +
                    "'Trying to create EntityCommandBuffer when it's not allowed!'. Remove this RegisterAt/After/Before.");
            }
        }

        // AllowBarrier<T> is a Game.dll generic, so the assembly check above flags it — but
        // registering AllowBarrier<OurBarrier> is the REQUIRED pattern to open our own
        // SafeCommandBufferSystem's gate (SystemRegistrar barrier wiring), not an accidental
        // vanilla registration. Whitelist it (when parameterised with one of OUR types); a bare
        // vanilla system (e.g. a stray UpdateGroupSystem) is still flagged.
        private static bool IsOwnedBarrierGateOpener(Type type)
        {
            if (!type.IsGenericType || type.GetGenericTypeDefinition() != typeof(AllowBarrier<>))
                return false;
            return type.GetGenericArguments()[0].Assembly == typeof(RegistrationValidator).Assembly;
        }

        private static void LogSoftFeatureEdges()
        {
            var softEdges = GeneratedFeatureDependencyManifest.SoftEdges;
            if (softEdges.Length == 0 || !Log.IsDebugEnabled)
                return;

            Log.Debug($"Soft feature dependency observations: {softEdges.Length}");
            foreach (var edge in softEdges)
                Log.Debug($"  soft: {edge}");
        }

        private static bool IsExpectedUnavailable(FeatureUnavailableReason reason)
        {
            return reason == FeatureUnavailableReason.Closed
                || reason == FeatureUnavailableReason.WaveLocked
                || reason == FeatureUnavailableReason.DependencySkipped;
        }

        private static IEnumerable<Type> GetTypesWithPartialLoadFallback(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                Log.Warn($"[FEATURE-VERIFY] registration validation degraded: {ex.LoaderExceptions.Length} type(s) failed to load; scanning loaded subset");
                foreach (var loaderException in ex.LoaderExceptions)
                {
                    if (loaderException != null)
                        Log.Warn($"[FEATURE-VERIFY] loader exception: {loaderException}");
                }

                return ex.Types.Where(t => t != null)!;
            }
        }
    }

    /// <summary>
    /// The single, tracked registration surface for ECS systems. Every vanilla
    /// <see cref="UpdateSystem"/> registration verb has a wrapper here that also
    /// calls <see cref="RegistrationValidator.Track"/>, so "a system is
    /// registered" is equivalent to "a system is tracked" by construction.
    /// Raw <c>updateSystem.UpdateAt/UpdateBefore/UpdateAfter/RegisterGPUSystem</c>
    /// calls are banned outside this class (CIVIC424) — using them would
    /// register a system that runs but is invisible to the validator.
    /// Drop-in replacements: <c>UpdateAt → RegisterAt</c>,
    /// <c>UpdateBefore → RegisterBefore</c>, <c>UpdateAfter → RegisterAfter</c>.
    /// </summary>
    internal static class UpdateSystemRegistrationExtensions
    {
        internal static void RegisterAt<T>(this UpdateSystem updateSystem, SystemUpdatePhase phase)
            where T : ComponentSystemBase
        {
            updateSystem.UpdateAt<T>(phase);
            RegistrationValidator.Track(typeof(T), phase);
        }

        internal static void RegisterBefore<T>(this UpdateSystem updateSystem, SystemUpdatePhase phase)
            where T : ComponentSystemBase
        {
            updateSystem.UpdateBefore<T>(phase);
            RegistrationValidator.Track(typeof(T), phase);
        }

        /// <summary>
        /// Registers <typeparamref name="T"/> and orders it before
        /// <typeparamref name="TOther"/>. Only <typeparamref name="T"/> is the
        /// owned/registered system; <typeparamref name="TOther"/> is an ordering
        /// anchor and is NOT tracked here (it is owned by whoever registers it).
        /// </summary>
        internal static void RegisterBefore<T, TOther>(this UpdateSystem updateSystem, SystemUpdatePhase phase)
            where T : ComponentSystemBase
            where TOther : ComponentSystemBase
        {
            updateSystem.UpdateBefore<T, TOther>(phase);
            RegistrationValidator.Track(typeof(T), phase);
        }

        internal static void RegisterAfter<T>(this UpdateSystem updateSystem, SystemUpdatePhase phase)
            where T : ComponentSystemBase
        {
            updateSystem.UpdateAfter<T>(phase);
            RegistrationValidator.Track(typeof(T), phase);
        }

        /// <summary>
        /// Registers <typeparamref name="T"/> and orders it after
        /// <typeparamref name="TOther"/>. Only <typeparamref name="T"/> is the
        /// owned/registered system; <typeparamref name="TOther"/> is an ordering
        /// anchor and is NOT tracked here (it is owned by whoever registers it).
        /// </summary>
        internal static void RegisterAfter<T, TOther>(this UpdateSystem updateSystem, SystemUpdatePhase phase)
            where T : ComponentSystemBase
            where TOther : ComponentSystemBase
        {
            updateSystem.UpdateAfter<T, TOther>(phase);
            RegistrationValidator.Track(typeof(T), phase);
        }
    }
}
