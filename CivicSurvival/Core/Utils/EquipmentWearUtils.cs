using CivicSurvival.Core.Config;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Utils
{
    /// <summary>
    /// Utility methods for EquipmentWear calculations.
    /// Extracted from component to maintain ECS data-only principle and Burst compatibility.
    /// </summary>
    public static class EquipmentWearUtils
    {
        /// <summary>
        /// Wear fraction (0..1) at or below which a plant is still "Operational" — no
        /// worn indicator, and nothing to repair on the wear axis. Single source for the
        /// boundary used by both <see cref="GetPlantState"/> (Worn vs Operational) and the
        /// UI repair affordance, so the REPAIR button never appears on a plant that shows
        /// no wear. Combat/disaster damage is repairable independently of this floor.
        /// </summary>
        public const float WornThreshold = 0.01f;

        /// <summary>
        /// Get explosion threshold from the active balance config.
        /// </summary>
        public static float GetExplosionThreshold()
        {
            return BalanceConfig.Current.EquipmentWear.ExplosionThreshold;
        }

        /// <summary>
        /// Check if wear is in danger zone (explosion risk).
        /// </summary>
        /// <param name="wearPercent">Current wear level (0-1).</param>
        /// <returns>True if wear exceeds explosion threshold.</returns>
        public static bool IsInDangerZone(float wearPercent)
        {
            return wearPercent >= GetExplosionThreshold();
        }

        /// <summary>
        /// Compute plant state from component fields.
        /// Priority: Repairing > DisabledByDisaster > Exploded > Critical > Worn > Operational
        /// </summary>
        /// <param name="wearPercent">Current wear level (0-1).</param>
        /// <param name="hasExploded">True if plant has suffered explosion damage.</param>
        /// <param name="isUnderRepair">True if plant is currently being repaired.</param>
        /// <param name="isUnderConstruction">True if plant is under construction.</param>
        /// <param name="hasDisasterDamage">True if plant is disabled by disaster.</param>
        /// <returns>Computed plant state.</returns>
        public static PlantState GetPlantState(float wearPercent, bool hasExploded, bool isUnderRepair, bool isUnderConstruction = false, bool hasDisasterDamage = false)
        {
            if (isUnderConstruction) return PlantState.UnderConstruction;
            if (isUnderRepair) return PlantState.Repairing;
            if (hasDisasterDamage) return PlantState.DisabledByDisaster;
            if (hasExploded) return PlantState.Exploded;
            if (IsInDangerZone(wearPercent)) return PlantState.Critical;
            if (wearPercent > WornThreshold) return PlantState.Worn;
            return PlantState.Operational;
        }
    }
}
