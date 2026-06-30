using System;

namespace CivicSurvival.Core.Utils
{
    /// <summary>
    /// Single formula for the covert export ceiling: shadow volume ≤ city
    /// dispatchable potential − active load − legal export flow. Legal flow =
    /// max(0, RawBalance − ExternalPower) clamped by the enforced export ceiling
    /// (per-interconnector cap × interconnector count): the flow-difference proxy
    /// carries ±a-few-MW rounding noise, and with cap 0 the city physically cannot
    /// export at all. The donor/import bonus sits inside Production and is not export.
    /// Known proxy allowance: during a partial blackout in a split network (one
    /// sub-grid under-served while another exports) the unserved load deflates
    /// RawBalance and with it the legal-flow estimate — the shadow ceiling is
    /// inflated by the same amount. Bounded by the unserved volume; the exact
    /// counter (ElectricityTradeSystem.m_Export) is deliberately not read to
    /// avoid a dependency on the vanilla system.
    /// Consumers: PowerGridDataSystem (ExportedMW ceiling), ShadowTradeDailySystem
    /// (ExportedMW calculation) and PowerGridUISystem (EXPORT row, via
    /// <see cref="ComputeLegalExportKW"/>). Change the formula only here.
    /// </summary>
    public static class PowerHeadroomMath
    {
        private const long KW_PER_MW = 1000L;

        /// <summary>
        /// Legal export flow estimate: the noisy flow-difference proxy clamped by the
        /// enforced ceiling. Arguments are kW; result is kW ≥ 0.
        /// </summary>
        public static int ComputeLegalExportKW(int rawBalanceKW, int externalPowerKW, int exportCapTotalKW)
        {
            long proxyKW = Math.Max(0, (long)rawBalanceKW - externalPowerKW);
            return (int)Math.Min(proxyKW, Math.Max(0, exportCapTotalKW));
        }

        /// <summary>Arguments: MW / kW / kW / kW / kW. Result is kW ≥ 0.</summary>
        public static int ComputeShadowExportCapKW(
            int cityDispatchableMW, int consumptionKW, int rawBalanceKW, int externalPowerKW,
            int exportCapTotalKW)
        {
            long headroomKW = (long)cityDispatchableMW * KW_PER_MW - consumptionKW;
            long legalExportKW = ComputeLegalExportKW(rawBalanceKW, externalPowerKW, exportCapTotalKW);
            long capKW = headroomKW - legalExportKW;
            return (int)Math.Max(0L, Math.Min(capKW, int.MaxValue));
        }
    }
}
