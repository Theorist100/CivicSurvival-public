using System.Collections.Generic;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Core.Services
{
    /// <summary>
    /// Process-wide map from <see cref="BuildingPlacementKind"/> to its
    /// <see cref="IBuildingPlacementHandler"/>. Populated by each owning domain in its
    /// <c>RegisterSystems</c> (AA today; Propaganda Center / gas station later) and read
    /// by the generic Core placement systems to dispatch building-specific work.
    ///
    /// Handlers are stateless strategy objects (they resolve the current World per call),
    /// so a static map that survives world reloads is safe — re-registration simply
    /// overwrites the previous instance with an equivalent one.
    /// </summary>
    public static class BuildingPlacementHandlerRegistry
    {
        private static readonly LogContext Log = new("BuildingPlacementHandlers");
        private static readonly object s_Lock = new();
        private static readonly Dictionary<BuildingPlacementKind, IBuildingPlacementHandler> s_Handlers = new();

        public static void Register(IBuildingPlacementHandler handler)
        {
            if (handler == null)
                return;

            lock (s_Lock)
            {
                s_Handlers[handler.Kind] = handler;
            }
            Log.Info($"Registered placement handler: {handler.Kind}");
        }

        public static bool TryGet(BuildingPlacementKind kind, out IBuildingPlacementHandler handler)
        {
            lock (s_Lock)
            {
                return s_Handlers.TryGetValue(kind, out handler!);
            }
        }

        /// <summary>
        /// Drop all registered handlers on mod unload. Each owning domain re-registers its
        /// handler on the next world load, so clearing here keeps a swapped-out domain's
        /// handler from lingering across a hot reload.
        /// </summary>
        public static void Clear()
        {
            lock (s_Lock)
            {
                s_Handlers.Clear();
            }
        }
    }
}
