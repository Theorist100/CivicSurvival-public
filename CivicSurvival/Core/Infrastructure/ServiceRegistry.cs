using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Core.Infrastructure
{
    /// <summary>
    /// Centralized service container for infrastructure services.
    /// Replaces scattered static accessors in Mod.cs.
    ///
    /// Layer 2 of Hybrid ECS Architecture:
    /// - ModSettings, AudioManager, SocialFeedService, etc.
    /// - NOT for gameplay state (use Singleton Components instead)
    ///
    /// Usage:
    ///   ServiceRegistry.Instance.Register&lt;IMyService&gt;(impl);
    ///   var svc = ServiceRegistry.Instance.Require&lt;IMyService&gt;();
    ///
    /// DEBUG MODE: Tracks registration source to detect memory leaks (missing Unregister).
    /// </summary>
    public sealed class ServiceRegistry : IDisposable
    {
        private static readonly LogContext Log = new("ServiceRegistry");

        private static ServiceRegistry s_Instance = null!;
        private static readonly object s_Lock = new();
        [ThreadStatic] private static string? s_ActiveFeatureRegistrationId;

        /// <summary>
        /// Global service registry instance.
        /// Throws if not initialized.
        /// </summary>
        public static ServiceRegistry Instance
        {
            get
            {
                lock (s_Lock)
                {
                    return s_Instance
                        ?? throw new InvalidOperationException("ServiceRegistry not initialized. Call Initialize() in Mod.OnLoad().");
                }
            }
        }

        /// <summary>
        /// Check if registry is initialized (for null-safe access).
        /// </summary>
        public static bool IsInitialized
        {
            get
            {
                lock (s_Lock)
                {
                    return s_Instance != null;
                }
            }
        }

        private readonly Dictionary<Type, object> m_Services = new();
        private bool m_Disposed;

#if DEBUG
        // Lifecycle leak detector: tracks which system registered which service
        private readonly Dictionary<Type, string> m_RegistrationSource = new();
#endif

        private ServiceRegistry() { }

        public static IDisposable BeginFeatureRegistration(string featureId)
        {
            if (string.IsNullOrEmpty(featureId))
                throw new ArgumentException("Feature id is required.", nameof(featureId));

            return new FeatureRegistrationScope(featureId);
        }

        /// <summary>
        /// Initialize the global service registry. Call once in Mod.OnLoad().
        /// </summary>
        /// <exception cref="InvalidOperationException">If already initialized (previous instance not disposed).</exception>
        public static void Initialize()
        {
            ServiceRegistry stale;
            List<string> leakedNames = null!;
            int leakedCount = 0;
            lock (s_Lock)
            {
                stale = s_Instance;
                if (stale != null)
                {
                    leakedCount = stale.m_Services.Count;
                    leakedNames = new List<string>();
                    foreach (var kvp in stale.m_Services)
                    {
#if DEBUG
                        var source = stale.m_RegistrationSource.TryGetValue(kvp.Key, out var registeredBy)
                            ? $" (registered by {registeredBy})"
                            : "";
#else
                        var source = "";
#endif
                        leakedNames.Add($"Leaked service: {kvp.Key.Name} -> {kvp.Value.GetType().Name}{source}");
                    }
                }
            }

            if (stale != null)
            {
                // Inv 2: a previous Mod.OnLoad ran without a matching Dispose
                // (asymmetric lifecycle). The stale instance's services reference
                // the destroyed previous world. The old behaviour kept the stale
                // instance and threw — but CS2's mod loader swallows the throw, so
                // every later Require<> served dead-world references. The earlier
                // M18 "replace before throw" left an empty registry for the same
                // swallowed-throw reason. A singleton carried across a world
                // boundary must be re-resolved, not preserved: tear the stale
                // registry down (runs each service's Dispose — also closes the
                // latent native/EventBus leak the throw-and-keep path never did)
                // and rebuild fresh. Recovery is non-fatal so Mod.OnLoad continues
                // into a clean registry; the leak is still logged loudly as the
                // lifecycle-bug signal.
                Log.Error($"ALREADY EXISTS with {leakedCount} services! Previous instance not disposed (asymmetric Mod lifecycle) — rebuilding fresh registry.");
                foreach (var msg in leakedNames)
                    Log.Error(msg);
                try
                {
                    stale.Dispose(); // disposes stale services and nulls s_Instance
                }
                catch (Exception ex)
                {
                    Log.Error($"Stale ServiceRegistry dispose threw during re-Initialize (continuing with fresh registry): {ex}");
                }
            }

            lock (s_Lock)
            {
                // stale.Dispose() nulled s_Instance under s_Lock before its
                // outside-lock service-dispose loop, so a throwing service still
                // leaves s_Instance null here. With no stale it was already null.
                // Either way the new world gets a clean registry.
                s_Instance ??= new ServiceRegistry();
            }
            Log.Info("Initialized");
        }

        /// <summary>
        /// Register a service implementation.
        /// </summary>
        /// <typeparam name="TInterface">Service interface or type</typeparam>
        /// <param name="service">Service implementation</param>
        public void Register<TInterface>(TInterface service) where TInterface : class
        {
            if (service == null)
                throw new ArgumentNullException(nameof(service));

            bool overwriting = false;
            string typeName;
            lock (s_Lock)
            {
                if (m_Disposed)
                    throw new ObjectDisposedException(nameof(ServiceRegistry));

                var type = typeof(TInterface);
                ValidateOwnerScope(type);
                typeName = type.Name;
                overwriting = m_Services.ContainsKey(type);
                m_Services[type] = service;

#if DEBUG
                // Track registration source for leak detection
                var stackTrace = new StackTrace(1, true);
                var frame = stackTrace.GetFrame(1); // frame 0 = Register itself, frame 1 = actual caller
                var callerType = frame?.GetMethod()?.DeclaringType?.Name ?? "Unknown";
                m_RegistrationSource[type] = callerType;
#endif
            }
            if (overwriting) Log.Warn($"Overwriting: {typeName}");
            if (Log.IsDebugEnabled) Log.Debug($"Registered: {typeName} -> {service.GetType().Name}");
        }

        private static void ValidateOwnerScope(Type serviceType)
        {
            if (!OwnerMetadata.TryGet(serviceType, out var declaredOwner))
                return;

            var activeOwner = s_ActiveFeatureRegistrationId;
            if (string.IsNullOrEmpty(activeOwner))
            {
                throw new InvalidOperationException(
                    $"Service {serviceType.Name} declares owner '{declaredOwner}' but was registered outside a feature registration scope.");
            }

            if (!string.Equals(declaredOwner, activeOwner, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Service {serviceType.Name} declares owner '{declaredOwner}' but was registered while feature '{activeOwner}' was active.");
            }
        }

        // S3010/S2696 are intentionally suppressed for this scope class: the static
        // s_ActiveFeatureRegistrationId is the scope-stack state, and the disposable
        // exists precisely to push/pop it across nested feature-registration calls
        // (the ambient-context pattern). Initialising the static elsewhere would
        // defeat the scope semantics.
#pragma warning disable S3010 // Remove this assignment or initialize statically
#pragma warning disable S2696 // Updates a static field from an instance method
        private sealed class FeatureRegistrationScope : IDisposable
        {
            private readonly string? m_Previous;
            private bool m_Disposed;

            public FeatureRegistrationScope(string featureId)
            {
                m_Previous = s_ActiveFeatureRegistrationId;
                s_ActiveFeatureRegistrationId = featureId;
            }

            public void Dispose()
            {
                if (m_Disposed) return;
                m_Disposed = true;
                s_ActiveFeatureRegistrationId = m_Previous;
            }
        }
#pragma warning restore S2696
#pragma warning restore S3010

        /// <summary>
        /// Get a service. Returns null if not registered or type mismatch.
        /// </summary>
        /// <typeparam name="TInterface">Service interface or type</typeparam>
        /// <returns>Service instance or null</returns>
        public TInterface? Get<TInterface>() where TInterface : class
        {
            lock (s_Lock)
            {
                if (m_Disposed) return null;
                return m_Services.TryGetValue(typeof(TInterface), out var svc)
                    ? svc as TInterface  // Safe cast - returns null instead of throwing
                    : null;
            }
        }

        /// <summary>
        /// Get a service. Throws if not registered.
        /// Use in OnCreate() for required dependencies.
        /// </summary>
        /// <typeparam name="TInterface">Service interface or type</typeparam>
        /// <returns>Service instance</returns>
        /// <exception cref="InvalidOperationException">If service not registered</exception>
        public TInterface Require<TInterface>() where TInterface : class
        {
            var svc = Get<TInterface>();
            if (svc == null)
                throw new InvalidOperationException($"Required service not registered: {typeof(TInterface).Name}");
            return svc;
        }

        /// <summary>
        /// Null-safe static accessor. Returns null if registry is not initialized or service not registered.
        /// Use instead of <c>Instance?.Get&lt;T&gt;()</c> — Instance throws if not initialized, so ?. is a no-op.
        /// </summary>
        public static TInterface? TryGet<TInterface>() where TInterface : class
        {
            lock (s_Lock)
            {
                if (s_Instance == null || s_Instance.m_Disposed) return null;
                return s_Instance.m_Services.TryGetValue(typeof(TInterface), out var svc)
                    ? svc as TInterface
                    : null;
            }
        }

        /// <summary>
        /// Check if a service is registered.
        /// </summary>
        public bool Has<TInterface>() where TInterface : class
        {
            lock (s_Lock)
            {
                if (m_Disposed) return false;
                return m_Services.ContainsKey(typeof(TInterface));
            }
        }

        /// <summary>
        /// Unregister a service. Call in OnDestroy() to clean up.
        /// Composition-root shutdown only: per-system cleanup must use
        /// <see cref="Unregister{TInterface}(TInterface)"/> to avoid stale old-world
        /// instances deleting new-world registrations.
        /// </summary>
        /// <typeparam name="TInterface">Service interface or type</typeparam>
        public void Unregister<TInterface>() where TInterface : class
        {
            bool removed = false;
            var type = typeof(TInterface);
            lock (s_Lock)
            {
                if (m_Disposed) return;
                removed = m_Services.Remove(type);
#if DEBUG
                if (removed) m_RegistrationSource.Remove(type);
#endif
            }
            if (removed && Log.IsDebugEnabled) Log.Debug($"Unregistered: {type.Name}");
        }

        /// <summary>
        /// Unregister a service only if the current registration matches the given instance.
        /// Safe to call during world reload: skips silently if a new world already re-registered.
        /// </summary>
        /// <typeparam name="TInterface">Service interface or type</typeparam>
        /// <param name="instance">The instance that originally registered this service</param>
        public void Unregister<TInterface>(TInterface instance) where TInterface : class
        {
            var type = typeof(TInterface);
            lock (s_Lock)
            {
                if (m_Disposed) return;
                if (!m_Services.TryGetValue(type, out var current)) return;
                if (!ReferenceEquals(current, instance)) return; // stale unregister from old world — skip
                m_Services.Remove(type);
#if DEBUG
                m_RegistrationSource.Remove(type);
#endif
            }
            if (Log.IsDebugEnabled) Log.Debug($"Unregistered: {type.Name}");
        }

        /// <summary>
        /// Get service count (for diagnostics).
        /// </summary>
        public int Count
        {
            get
            {
                lock (s_Lock)
                {
                    return m_Disposed ? 0 : m_Services.Count;
                }
            }
        }

        /// <summary>
        /// Dispose all services and clear registry.
        /// Call in Mod.OnDispose().
        /// </summary>
        public void Dispose()
        {
            List<KeyValuePair<Type, object>> servicesToDispose;

            lock (s_Lock)
            {
                if (m_Disposed) return;
                m_Disposed = true;

                // Copy to list to avoid modification during iteration
                servicesToDispose = new List<KeyValuePair<Type, object>>(m_Services);

                m_Services.Clear();
#if DEBUG
                m_RegistrationSource.Clear();
#endif
#pragma warning disable S2696 // Instance members should not write to static fields - singleton disposal pattern
                s_Instance = null!;
#pragma warning restore S2696
            }

            // FIX #202: Log outside lock to avoid potential deadlock
            Log.Info($"Disposing {servicesToDispose.Count} services:");
            foreach (var kvp in servicesToDispose)
            {
                var isDisposable = kvp.Value is IDisposable ? " (IDisposable)" : "";
                Log.Info($"  - {kvp.Key.Name} -> {kvp.Value.GetType().Name}{isDisposable}");
            }

            var disposedServices = new HashSet<object>(ReferenceEqualityComparer.Shared);

            // Dispose outside lock to avoid deadlocks if Dispose() calls back into ServiceRegistry
            foreach (var kvp in servicesToDispose)
            {
                if (kvp.Value is IDisposable disposable)
                {
                    if (!disposedServices.Add(kvp.Value))
                    {
                        if (Log.IsDebugEnabled) Log.Debug($"Skipped duplicate dispose: {kvp.Key.Name}");
                        continue;
                    }

                    try
                    {
                        disposable.Dispose();
                        if (Log.IsDebugEnabled) Log.Debug($"Disposed: {kvp.Key.Name}");
                    }
                    catch (Exception ex)
                    {
                        Log.Warn($"Dispose error for {kvp.Key.Name}: {ex}");
                    }
                }
            }

            Log.Info("Disposed");
        }

        private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
        {
            public static readonly ReferenceEqualityComparer Shared = new();

            private ReferenceEqualityComparer() { }

            public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);

            public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
        }
    }
}
