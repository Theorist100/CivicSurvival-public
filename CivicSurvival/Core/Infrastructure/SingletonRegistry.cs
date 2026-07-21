using System;
using System.Collections.Generic;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Core.Infrastructure
{
    /// <summary>
    /// Centralized singleton tracking for lifecycle management and debugging.
    ///
    /// Tracks three types of singletons:
    /// - ECS Singletons (PowerGridSingleton, etc.) - via Unity.Entities
    /// - Static singletons (ServiceRegistry.Instance, etc.) - via Register()
    /// - MonoBehaviour singletons (AudioManager, etc.) - via Register()
    ///
    /// Benefits:
    /// - Single source of truth for all singletons
    /// - Lifecycle debugging (which system owns what)
    /// - Dependency tracking
    /// - Memory leak detection in DEBUG mode
    ///
    /// Usage:
    ///   SingletonRegistry.Instance.Register&lt;AudioManager&gt;(audioManager, "Mod.OnLoad");
    /// </summary>
    public sealed class SingletonRegistry : IDisposable
    {
        private static readonly LogContext Log = new("SingletonRegistry");

        private static SingletonRegistry s_Instance = null!;
        private static readonly object s_Lock = new();

        /// <summary>
        /// Global singleton registry instance.
        /// </summary>
        public static SingletonRegistry Instance
        {
            get
            {
                lock (s_Lock)
                {
                    return s_Instance
                        ?? throw new InvalidOperationException("SingletonRegistry not initialized. Call Initialize() in Mod.OnLoad().");
                }
            }
        }

        /// <summary>
        /// Check if registry is initialized.
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

        private readonly Dictionary<Type, object> m_Singletons = new();
        private readonly Dictionary<Type, string> m_OwnerSystems = new();
        private bool m_Disposed;

        private SingletonRegistry() { }

        /// <summary>
        /// Initialize the global singleton registry.
        /// Call once in Mod.OnLoad() after FeatureRegistry.
        /// </summary>
        public static void Initialize()
        {
            SingletonRegistry stale;
            List<string> leakedNames = null!;
            int leakedCount = 0;

            lock (s_Lock)
            {
                stale = s_Instance;
                if (stale != null)
                {
                    leakedCount = stale.m_Singletons.Count;
                    leakedNames = new List<string>(leakedCount);
                    foreach (var kvp in stale.m_Singletons)
                    {
                        var owner = stale.m_OwnerSystems.TryGetValue(kvp.Key, out var o) ? o : "unknown";
                        leakedNames.Add($"Leaked singleton: {kvp.Key.Name} -> {kvp.Value.GetType().Name} (owner: {owner})");
                    }
                }
            }

            if (stale != null)
            {
                Log.Error($"ALREADY EXISTS with {leakedCount} singletons! Previous instance not disposed (asymmetric Mod lifecycle) — rebuilding fresh registry.");
                foreach (var msg in leakedNames)
                    Log.Error(msg);

                try
                {
                    stale.Dispose();
                }
                catch (Exception ex)
                {
                    Log.Error($"Stale SingletonRegistry dispose threw during re-Initialize (continuing with fresh registry): {ex}");
                }
            }

            lock (s_Lock)
            {
                s_Instance ??= new SingletonRegistry();
            }
            Log.Info(" Initialized");
        }

        /// <summary>
        /// Register a singleton with owner tracking.
        /// </summary>
        /// <typeparam name="T">Singleton type</typeparam>
        /// <param name="singleton">Singleton instance</param>
        /// <param name="ownerSystem">Name of system that created this singleton</param>
        public void Register<T>(T singleton, string ownerSystem) where T : class
        {
            if (singleton == null)
                throw new ArgumentNullException(nameof(singleton));
            if (string.IsNullOrEmpty(ownerSystem))
                throw new ArgumentNullException(nameof(ownerSystem));

            bool overwriting = false;
            string prevOwner = null!;
            string typeName;
            lock (s_Lock)
            {
                if (m_Disposed)
                    throw new ObjectDisposedException(nameof(SingletonRegistry));

                var type = typeof(T);
                typeName = type.Name;
                if (m_Singletons.ContainsKey(type))
                {
                    overwriting = true;
                    prevOwner = m_OwnerSystems[type];
                }

                m_Singletons[type] = singleton;
                m_OwnerSystems[type] = ownerSystem;
            }
            if (overwriting) Log.Warn($" Overwriting {typeName} (was owned by: {prevOwner})");
            if (Log.IsDebugEnabled) Log.Debug($" Registered: {typeName} (owner: {ownerSystem})");
        }

        /// <summary>
        /// Unregister a singleton.
        /// </summary>
        public void Unregister<T>() where T : class
        {
            bool removed = false;
            var type = typeof(T);
            lock (s_Lock)
            {
                if (m_Disposed)
                    return;

                removed = m_Singletons.Remove(type);
                if (removed)
                {
                    m_OwnerSystems.Remove(type);
                }
            }
            if (removed && Log.IsDebugEnabled) Log.Debug($" Unregistered: {type.Name}");
        }

        /// <summary>
        /// Dispose and clear all singletons.
        /// Call in Mod.OnDispose().
        /// </summary>
        public void Dispose()
        {
            List<KeyValuePair<Type, object>> singletonsToDispose;

            var logLines = new List<string>();
            lock (s_Lock)
            {
                if (m_Disposed)
                    return;

                m_Disposed = true;

                logLines.Add($" Disposing {m_Singletons.Count} singletons:");
                foreach (var kvp in m_Singletons)
                {
                    var isDisposable = kvp.Value is IDisposable ? " (IDisposable)" : "";
                    var owner = m_OwnerSystems.TryGetValue(kvp.Key, out var o) ? o : "unknown";
                    logLines.Add($"   - {kvp.Key.Name} (owner: {owner}){isDisposable}");
                }

                // Copy to list to avoid modification during iteration
                singletonsToDispose = new List<KeyValuePair<Type, object>>(m_Singletons);

                m_Singletons.Clear();
                m_OwnerSystems.Clear();

#pragma warning disable S2696 // Instance members should not write to static fields - singleton disposal pattern
                s_Instance = null!;
#pragma warning restore S2696
            }
            foreach (var line in logLines) Log.Info(line);

            // Dispose outside lock to avoid deadlocks
            foreach (var kvp in singletonsToDispose)
            {
                if (kvp.Value is IDisposable disposable)
                {
                    try
                    {
                        disposable.Dispose();
                        if (Log.IsDebugEnabled) Log.Debug($" Disposed: {kvp.Key.Name}");
                    }
                    catch (Exception ex)
                    {
                        Log.Warn($" Dispose error for {kvp.Key.Name}: {ex}");
                    }
                }
            }

            Log.Info(" Disposed");
        }
    }
}
