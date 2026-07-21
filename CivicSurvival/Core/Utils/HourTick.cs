using CivicSurvival.Core.Events;

namespace CivicSurvival.Core.Utils
{
    /// <summary>
    /// Helpers for hourly-sliced income (schemes that accrue per day but pay per
    /// game hour). Kept in one place so the global-hour key and the slice divisor
    /// cannot drift between the systems that use them.
    /// </summary>
    public static class HourTick
    {
        /// <summary>
        /// Strictly increasing hour counter across day boundaries — the dedup key
        /// for hourly payouts (a plain 0-23 hour would repeat every day and the
        /// monotonic dedup would swallow every hour after the first).
        /// </summary>
        public static int GlobalHour(HourChangedEvent evt)
            => evt.DayNumber * (int)GameRate.HOURS_PER_DAY + evt.Hour;

        /// <summary>
        /// One hour's share of a FIXED daily amount (a flat payout, a share of the
        /// day's throughput). The 24 slices sum to the daily amount, so switching a
        /// scheme from daily to hourly changes cadence, not balance.
        ///
        /// NOT for rates applied to a shrinking base — those compound; use
        /// <see cref="GameRate.CompoundingRatePerHour"/> instead.
        /// </summary>
        public static double SliceOfDaily(double dailyAmount)
            => dailyAmount / GameRate.HOURS_PER_DAY;
    }
}
