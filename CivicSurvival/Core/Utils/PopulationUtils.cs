using Unity.Entities;
using CivicSurvival.Core.Adapters;
using CivicSurvival.Core.Infrastructure;

namespace CivicSurvival.Core.Utils
{
    /// <summary>
    /// Static accessor for citizen-count queries. The per-World EntityQuery lives
    /// in <c>PopulationCountHost</c>. Returns 0 if host is not attached.
    /// </summary>
    public static class PopulationUtils
    {
        /// <summary>
        /// Get current citizen count from the active world. Returns 0 if host not
        /// attached. Targets <c>PopulationCountHost.World</c> regardless of caller —
        /// for non-ECS contexts (telemetry sidecars) and ECS systems alike.
        /// </summary>
        public static int GetCitizenCount()
        {
            // Explicit null check (not `?.`) — CIVIC109 bans silent null-conditional on
            // ServiceRegistry.Get*. Returning 0 on missing facade is intentional (boot
            // window / between-worlds), so no Mod.Log.Error here.
            var facade = ServiceRegistry.TryGet<PopulationCountFacade>();
            if (facade == null) return 0;
            var host = facade.CurrentHost;
            return host == null ? 0 : host.GetCitizenCount();
        }

        /// <summary>Extension form for SystemBase callers — same semantics as parameterless overload.</summary>
        public static int GetCitizenCount(this SystemBase system) => GetCitizenCount();
    }
}
