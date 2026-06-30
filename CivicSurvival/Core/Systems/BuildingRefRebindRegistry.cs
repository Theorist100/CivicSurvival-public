using System;
using System.Collections.Generic;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Core.Systems
{
    /// <summary>
    /// Process-local post-load gate for mod entities that carry indexed building refs.
    /// Default gameplay is open; a load arms the gate until each typed rebind owner
    /// completes and marks its component type.
    /// </summary>
    public static class BuildingRefRebindRegistry
    {
        private static readonly LogContext Log = new("BuildingRefRebindRegistry");
        private static readonly HashSet<Type> s_CompletedTypes = new();
        private static bool s_PostLoadGateActive;

        public static void BeginPostLoadRebind()
        {
            bool wasActive = s_PostLoadGateActive;
            s_CompletedTypes.Clear();
            s_PostLoadGateActive = true;
            if (!wasActive)
                Log.Info("Post-load building-ref rebind gate armed");
        }

        public static void Reset()
        {
            s_CompletedTypes.Clear();
            s_PostLoadGateActive = false;
        }

        public static void MarkComplete(Type componentType, string ownerName)
        {
            if (componentType == null)
                return;

            s_CompletedTypes.Add(componentType);
            Log.Info($"{ownerName}: building-ref rebind complete for {componentType.Name}");
        }

        public static bool CanPurge<T>()
        {
            return !s_PostLoadGateActive || s_CompletedTypes.Contains(typeof(T));
        }
    }
}
