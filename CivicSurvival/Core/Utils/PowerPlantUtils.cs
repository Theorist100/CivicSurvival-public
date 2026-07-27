using System.Collections.Generic;
using Game.Prefabs;
using CivicSurvival.Core.Config;
using Unity.Entities;

namespace CivicSurvival.Core.Utils
{
    /// <summary>
    /// Utilities for power plant identification and naming.
    /// SMELL-E04 FIX: Consolidated duplicate plant name detection logic.
    /// </summary>
    public static class PowerPlantUtils
    {
        private static readonly LogContext Log = new("PowerPlantUtils");

        /// <summary>
        /// Power plant type enumeration.
        /// </summary>
        public enum PlantType
        {
            Unknown,
            Wind,
            Solar,
            Gas,
            Geothermal,
            Hydro,
            Coal,
            Nuclear,
            Generic
        }

        /// <summary>
        /// Weather-dependent generation whose output genuinely swings to ~0 on its own
        /// (wind calms, solar nights) — the only types that justify the diversity headroom
        /// bonus in the surplus formulas (degradation + strike axes): the bonus exists
        /// because intermittent sources REQUIRE backup reserve, not because owning many
        /// plant brands is a virtue. Stable types (coal/gas/nuclear/geothermal) grant no
        /// bonus — otherwise "diverse spam" farms the free threshold (7 types → ×3.4 of
        /// peak demand forgiven). Hydro deliberately excluded: its flow swing is seasonal
        /// and slow — the player can build against it, unlike tonight's calm.
        /// </summary>
        public static bool IsIntermittent(PlantType type)
            => type == PlantType.Wind || type == PlantType.Solar;

        // Prefab-entity → PlantType. Classification walks prefab.name with
        // ToUpperInvariant + Contains chains — fine on one-shot events (construction,
        // disaster), but the capacity resolver now classifies every grid producer in two
        // 500 ms hot loops. The prefab's type never changes within a session and the key
        // includes Entity.Version, so a recreated prefab entity can never serve a stale
        // hit. Main-thread only (PrefabSystem is managed and unreachable from jobs).
#pragma warning disable CIVIC148 // Version-keyed pure-function cache: a reused prefab Entity index gets a new Version → stale hit impossible; dead entries are inert and bounded by prefab count
        private static readonly Dictionary<Entity, PlantType> s_PlantTypeByPrefab = new();
#pragma warning restore CIVIC148

        /// <summary>
        /// Get plant type from prefab name.
        /// Used by ConstructionDelaySystem, PowerPlantDisasterSystem, etc.
        /// </summary>
        public static PlantType GetPlantType(string prefabName)
        {
            if (string.IsNullOrEmpty(prefabName))
                return PlantType.Unknown;

            string name = prefabName.ToUpperInvariant();

            // CA1307: Use StringComparison.Ordinal (string already uppercase)
            if (name.Contains("NUCLEAR", System.StringComparison.Ordinal) || name.Contains("ATOM", System.StringComparison.Ordinal))
                return PlantType.Nuclear;
            if (name.Contains("COAL", System.StringComparison.Ordinal))
                return PlantType.Coal;
            if (name.Contains("GAS", System.StringComparison.Ordinal))
                return PlantType.Gas;
            if (name.Contains("SOLAR", System.StringComparison.Ordinal))
                return PlantType.Solar;
            if (name.Contains("WIND", System.StringComparison.Ordinal))
                return PlantType.Wind;
            if (name.Contains("HYDRO", System.StringComparison.Ordinal) || name.Contains("DAM", System.StringComparison.Ordinal))
                return PlantType.Hydro;
            if (name.Contains("GEOTHERM", System.StringComparison.Ordinal))
                return PlantType.Geothermal;

            // Observability hook for new DLC plant prefabs that no existing substring
            // catches. Debug-only so production stays quiet; surfaces during
            // investigation when balance team wonders why a fresh prefab gets generic
            // construction days / display name. Add a substring branch above when it
            // fires for a real DLC plant.
            if (Log.IsDebugEnabled)
                Log.Debug($"GetPlantType: unknown prefab name '{prefabName}' → Generic. Add a substring match if this is a DLC plant.");
            return PlantType.Generic;
        }

        /// <summary>
        /// Get plant type from PrefabRef using PrefabSystem.
        /// </summary>
        public static PlantType GetPlantType(PrefabSystem prefabSystem, PrefabRef prefabRef)
        {
            // PERF-LOCK: per-prefab cache — the resolver calls this for every grid producer
            // every 500 ms tick; removing the cache reintroduces a string allocation
            // (prefab.name + ToUpperInvariant) per plant per tick in a [HotPathSystem].
            if (s_PlantTypeByPrefab.TryGetValue(prefabRef.m_Prefab, out var cached))
                return cached;

            // PrefabSystem.TryGetPrefab returns true on (m_Index >= 0) — it does NOT
            // verify the `as T` cast result. T=PrefabBase makes a null result
            // unreachable today, but the contract is fragile; explicit null-check
            // keeps callers honest if T ever tightens.
            if (!prefabSystem.TryGetPrefab(prefabRef.m_Prefab, out PrefabBase prefab) || prefab == null)
                return PlantType.Unknown;   // not cached: a not-yet-resolvable ref may resolve later

            var type = GetPlantType(prefab.name);
            s_PlantTypeByPrefab[prefabRef.m_Prefab] = type;
            return type;
        }

        /// <summary>
        /// Get human-readable display name for plant type.
        /// </summary>
        public static string GetDisplayName(PlantType type)
        {
            return type switch
            {
                PlantType.Nuclear => "Nuclear Plant",
                PlantType.Coal => "Coal Plant",
                PlantType.Gas => "Gas Plant",
                PlantType.Solar => "Solar Farm",
                PlantType.Wind => "Wind Farm",
                PlantType.Hydro => "Hydro Dam",
                PlantType.Geothermal => "Geothermal Plant",
                PlantType.Generic => "Power Plant",
                PlantType.Unknown => "Power Plant",
                _ => "Power Plant"
            };
        }

        /// <summary>
        /// Get construction time in days for plant type.
        /// Used by ConstructionDelaySystem.
        /// UTL-002 FIX: Extracted to Balance.Construction constants.
        /// </summary>
        public static int GetConstructionDays(PlantType type)
        {
            var cfg = BalanceConfig.Current.Construction;
            return type switch
            {
                PlantType.Wind => cfg.WindDays,
                PlantType.Solar => cfg.SolarDays,
                PlantType.Geothermal => cfg.GeothermalDays,
                PlantType.Gas => cfg.GasDays,
                PlantType.Hydro => cfg.HydroDays,
                PlantType.Coal => cfg.CoalDays,
                PlantType.Nuclear => cfg.NuclearDays,
                PlantType.Generic => cfg.GenericDays,
                PlantType.Unknown => cfg.GenericDays,
                _ => cfg.GenericDays
            };
        }
    }
}
