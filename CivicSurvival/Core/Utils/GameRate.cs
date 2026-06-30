namespace CivicSurvival.Core.Utils
{
    /// <summary>
    /// Standardized rate-over-time math for gameplay systems.
    /// All methods are pure (no state), safe for Burst jobs.
    ///
    /// Usage in ThrottledSystemBase:
    ///   float decay = GameRate.ScalePerDay(config.DecayPerDay, ThrottledDeltaSeconds);
    ///
    /// Usage in per-frame systems:
    ///   float regen = GameRate.ScalePerHour(config.RegenPerHour, SystemAPI.Time.DeltaTime);
    ///
    /// Chance rolls:
    ///   float p = GameRate.ChancePerDay(config.DisasterChancePerDay, deltaSeconds);
    ///   if (random.NextFloat() &lt; p) { ... }
    /// </summary>
    public static class GameRate
    {
        /// <summary>Seconds in one game hour (3600).</summary>
        public const float SECONDS_PER_HOUR = 3600f;

        /// <summary>Seconds in one game day (86400).</summary>
        public const float SECONDS_PER_DAY = 86400f;

        /// <summary>Hours in one game day (24).</summary>
        public const float HOURS_PER_DAY = 24f;

        /// <summary>
        /// Convert a per-day rate into an amount for the given delta.
        /// Example: ScalePerDay(5f, ThrottledDeltaSeconds) for "5 points decay per day".
        /// </summary>
        public static float ScalePerDay(float ratePerDay, float deltaSeconds)
        {
            return ratePerDay * deltaSeconds / SECONDS_PER_DAY;
        }

        /// <summary>
        /// Convert a per-hour rate into an amount for the given delta.
        /// Example: ScalePerHour(0.1f, deltaTime) for "0.1 per hour regen".
        /// </summary>
        public static float ScalePerHour(float ratePerHour, float deltaSeconds)
        {
            return ratePerHour * deltaSeconds / SECONDS_PER_HOUR;
        }

        /// <summary>
        /// Fraction of a game day that deltaSeconds represents.
        /// Example: DayFraction(ThrottledDeltaSeconds) replaces deltaSeconds / 86400f.
        /// </summary>
        public static float DayFraction(float deltaSeconds)
        {
            return deltaSeconds / SECONDS_PER_DAY;
        }

        /// <summary>
        /// Convert seconds to hours.
        /// Example: HoursDelta(ThrottledDeltaSeconds) replaces ThrottledDeltaSeconds / 3600f.
        /// </summary>
        public static float HoursDelta(float deltaSeconds)
        {
            return deltaSeconds / SECONDS_PER_HOUR;
        }

        /// <summary>Double-precision seconds to game hours conversion.</summary>
        public static double HoursDelta(double deltaSeconds)
        {
            return deltaSeconds / SECONDS_PER_HOUR;
        }

        /// <summary>
        /// Scale a daily probability for a sub-day time window (in seconds).
        /// Converts chancePerDay → chancePerHour → chancePerUpdate.
        /// Example: ChancePerDay(0.03f, ThrottledDeltaSeconds) for "3% chance per day".
        /// </summary>
        public static float ChancePerDay(float chancePerDay, float deltaSeconds)
        {
            return chancePerDay * deltaSeconds / SECONDS_PER_DAY;
        }

        /// <summary>
        /// Scale an hourly probability for a sub-hour time window (in seconds).
        /// Example: ChancePerHour(0.02f, ThrottledDeltaSeconds) for "2% chance per hour".
        /// </summary>
        public static float ChancePerHour(float chancePerHour, float deltaSeconds)
        {
            return chancePerHour * deltaSeconds / SECONDS_PER_HOUR;
        }

        /// <summary>
        /// Fraction of a game day from a delta in game hours.
        /// For systems that track game-time intervals (TotalGameHours deltas).
        /// Example: DayFractionFromHours(currentHour - prevHour) replaces deltaHours / 24f.
        /// </summary>
        public static float DayFractionFromHours(float deltaGameHours)
        {
            return deltaGameHours / HOURS_PER_DAY;
        }

        /// <summary>Double-precision fraction of a game day from game hours.</summary>
        public static double DayFractionFromHours(double deltaGameHours)
        {
            return deltaGameHours / HOURS_PER_DAY;
        }

        /// <summary>Convert game hours to game seconds.</summary>
        public static float HoursToSeconds(float gameHours)
        {
            return gameHours * SECONDS_PER_HOUR;
        }

        /// <summary>Double-precision conversion from game hours to game seconds.</summary>
        public static double HoursToSeconds(double gameHours)
        {
            return gameHours * SECONDS_PER_HOUR;
        }

        /// <summary>
        /// Accumulate a per-second rate with fractional remainder tracking.
        /// Prevents precision loss when rates produce sub-integer amounts per update.
        ///
        /// Example:
        ///   double ratePerSec = dailyIncome / GameRate.SECONDS_PER_DAY;
        ///   long whole = GameRate.AccumulateWithRemainder(ratePerSec, deltaSeconds, ref remainder);
        ///   if (whole > 0) AddIncome(whole);
        /// </summary>
        /// <returns>Whole units to apply this update.</returns>
        public static long AccumulateWithRemainder(
            double ratePerSecond,
            double deltaSeconds,
            ref double remainder)
        {
            if (!double.IsFinite(ratePerSecond) || !double.IsFinite(deltaSeconds) || !double.IsFinite(remainder))
            {
                remainder = 0.0;
                return 0;
            }

            double raw = ratePerSecond * deltaSeconds + remainder;
            if (!double.IsFinite(raw))
            {
                remainder = 0.0;
                return 0;
            }

            if (raw >= long.MaxValue)
            {
                remainder = 0.0;
                return long.MaxValue;
            }

            if (raw <= long.MinValue)
            {
                remainder = 0.0;
                return long.MinValue;
            }

            long whole = (long)System.Math.Floor(raw);
            remainder = raw - whole;
            return whole;
        }
    }
}
