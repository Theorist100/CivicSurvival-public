using CivicSurvival.Core.Config;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Core.Services
{
    /// <summary>
    /// Repair calculation parameters (cost, kickback, duration).
    /// </summary>
    public readonly struct RepairParams
    {
        public int Cost { get; init; }
        public int Kickback { get; init; }
        public float DurationHours { get; init; }
        public bool UsesShadowCash { get; init; }
    }

    /// <summary>
    /// Static helper for repair payment calculations.
    /// </summary>
    public static class RepairPaymentHelper
    {
        /// <summary>
        /// Calculate repair cost, kickback, and duration for given wear percent.
        /// </summary>
        /// <param name="wearPercent">Plant wear (0-100 integer percent).</param>
        /// <param name="repairType">Type of repair operation.</param>
        /// <returns>Calculated repair parameters.</returns>
        public static RepairParams CalculateRepairParams(int wearPercent, RepairType repairType)
        {
            var cfg = BalanceConfig.Current.InfrastructureRepair;

            return repairType switch
            {
                RepairType.Municipal => new RepairParams
                {
                    Cost = wearPercent * cfg.MunicipalBaseCostPerPercent,
                    Kickback = 0,
                    DurationHours = cfg.MunicipalRepairHours,
                    UsesShadowCash = false
                },

                RepairType.MunicipalWithKickback => new RepairParams
                {
                    Cost = (int)System.Math.Round(wearPercent * cfg.MunicipalBaseCostPerPercent * cfg.MunicipalCostMultiplierWithKickback),
                    Kickback = (int)System.Math.Round(wearPercent * cfg.MunicipalBaseCostPerPercent * cfg.MunicipalCostMultiplierWithKickback * cfg.MunicipalKickbackPercent),
                    DurationHours = cfg.MunicipalRepairHours,
                    UsesShadowCash = false
                },

                RepairType.ShadowOps => new RepairParams
                {
                    Cost = wearPercent * cfg.ShadowOpsBaseCostPerPercent,
                    Kickback = 0,
                    DurationHours = cfg.ShadowOpsRepairHours,
                    UsesShadowCash = true
                },

                _ => default
            };
        }

        // ============================================================================
        // CIVILIAN BUILDING REPAIR (per-hit pricing)
        // ============================================================================

        /// <summary>
        /// Calculate civilian building repair params (per-hit pricing).
        /// Kickback multiplier and percent reused from PP config.
        /// </summary>
        public static RepairParams CalculateCivilianRepairParams(int hitCount, RepairType repairType)
        {
            var cfg = BalanceConfig.Current.InfrastructureRepair;

            return repairType switch
            {
                RepairType.Municipal => new RepairParams
                {
                    Cost = hitCount * cfg.CivilianMunicipalCostPerHit,
                    Kickback = 0,
                    DurationHours = cfg.CivilianMunicipalRepairHours,
                    UsesShadowCash = false
                },

                RepairType.MunicipalWithKickback => new RepairParams
                {
                    Cost = (int)System.Math.Round(hitCount * cfg.CivilianMunicipalCostPerHit
                           * cfg.MunicipalCostMultiplierWithKickback),
                    Kickback = (int)System.Math.Round(hitCount * cfg.CivilianMunicipalCostPerHit
                               * cfg.MunicipalCostMultiplierWithKickback
                               * cfg.MunicipalKickbackPercent),
                    DurationHours = cfg.CivilianMunicipalRepairHours,
                    UsesShadowCash = false
                },

                RepairType.ShadowOps => new RepairParams
                {
                    Cost = hitCount * cfg.CivilianShadowOpsCostPerHit,
                    Kickback = 0,
                    DurationHours = cfg.CivilianShadowOpsRepairHours,
                    UsesShadowCash = true
                },

                _ => LogUnknownRepairType(repairType)
            };
        }

        private static RepairParams LogUnknownRepairType(RepairType repairType)
        {
            Mod.Log.Warn($"CalculateRepairParams: unknown RepairType {(int)repairType}");
            return default;
        }

        // ============================================================================
        // BILLABLE REPAIR PERCENT
        // ============================================================================

        /// <summary>
        /// Billable repair percent: the worst of the four plant-damage sources, billed
        /// as an integer percent. The player pays for a full return to 100%, so the
        /// charge is driven by whichever damage class is largest (a 40%-worn plant that
        /// also took 70% operational damage bills at 70%).
        ///
        /// Single source for the repair-transaction layer: <see cref="PlantRepairService"/>
        /// (cost preview + complete-repair) and the ModificationEnd intake both call this
        /// so the percent the player is billed and the percent the repair restores can
        /// never drift.
        /// </summary>
        /// <param name="wearPercent">Accumulated equipment wear (0..1 fraction).</param>
        /// <param name="explosionPercent">Explosion damage fraction (saved or snapshot), or 0.</param>
        /// <param name="operationalPercent">Operational damage fraction from the capacity snapshot, or 0.</param>
        /// <param name="disasterPercent">Disaster damage fraction from the capacity snapshot, or 0.</param>
        /// <returns>Billable percent clamped to [1,100], or 0 when there is no damage.</returns>
        public static int BillableRepairPercent(
            float wearPercent,
            float explosionPercent,
            float operationalPercent,
            float disasterPercent)
        {
            float damagePercent = wearPercent;
            damagePercent = System.Math.Max(damagePercent, explosionPercent);
            damagePercent = System.Math.Max(damagePercent, operationalPercent);
            damagePercent = System.Math.Max(damagePercent, disasterPercent);

            if (damagePercent <= 0f)
                return 0;

            return System.Math.Clamp((int)System.Math.Ceiling(damagePercent * 100), 1, 100);
        }

    }
}
