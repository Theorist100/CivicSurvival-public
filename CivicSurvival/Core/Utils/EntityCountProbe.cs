using CivicSurvival.Core.Adapters;
using CivicSurvival.Core.Infrastructure;

namespace CivicSurvival.Core.Utils
{
    /// <summary>
    /// Static accessor for entity-count diagnostics. The 10× EntityQuery state
    /// lives in <c>EntityCountProbeHost</c>.
    /// </summary>
    internal static class EntityCountProbe
    {
        public struct Counts
        {
            public bool Valid;
            public int ThreatsAlive;
            public int DebrisFalling;
            public int VanillaOnFire;
            public int VanillaDestroyed;
            public int TotalModEntities;
            public int PsyStateEntities;
            public int SpotterEntities;
            public int BackupPowerEntities;
            public int EquipmentWearEntities;
            public int VanillaTotalEntities;
        }

        /// <summary>
        /// Take snapshot of entity counts. Returns <c>default</c> (Valid=false) if the
        /// host is not attached (boot window or between-worlds).
        /// </summary>
        public static Counts Snapshot()
        {
            // Explicit null check (not `?.`) — CIVIC109 bans silent null-conditional on
            // ServiceRegistry.Get*. Returning default on missing facade is intentional
            // (boot window / between-worlds), so no Mod.Log.Error here.
            var facade = ServiceRegistry.TryGet<EntityCountProbeFacade>();
            if (facade == null) return default;
            var host = facade.CurrentHost;
            return host == null ? default : host.Snapshot();
        }
    }
}
