using System;
using System.Threading;

namespace CivicSurvival.Core.Utils
{
    /// <summary>
    /// Thread-safe random number generator.
    /// Uses ThreadLocal to avoid same-seed issues when called rapidly from multiple threads.
    ///
    /// Why static ThreadLocal:
    /// - Lives for entire AppDomain lifetime (mod lifetime)
    /// - No need to Dispose - cleared when AppDomain unloads
    /// - Each thread gets its own Random instance with unique seed
    ///
    /// Usage:
    ///   int value = ThreadSafeRandom.Next(10);
    ///   double d = ThreadSafeRandom.NextDouble();
    /// </summary>
    public static class ThreadSafeRandom
    {
        private static int s_Seed = Environment.TickCount;

        private static readonly ThreadLocal<Random> s_Local = new(() =>
            new Random(Interlocked.Increment(ref s_Seed)));

        // M1 FIX: Consolidate null-forgiving operator to single property
        // ThreadLocal<T> with valueFactory is guaranteed non-null after first access
        private static Random Local => s_Local.Value!;

        /// <summary>
        /// Returns a non-negative random integer less than maxValue.
        /// </summary>
        public static int Next(int maxValue) => Local.Next(maxValue);

        /// <summary>
        /// Returns a random integer within a specified range [minValue, maxValue).
        /// </summary>
        public static int Next(int minValue, int maxValue) => Local.Next(minValue, maxValue);

        /// <summary>
        /// Returns a random floating-point number between 0.0 and 1.0.
        /// </summary>
        public static double NextDouble() => Local.NextDouble();

        /// <summary>
        /// Returns a random floating-point number between 0.0f and 1.0f.
        /// </summary>
        public static float NextFloat() => (float)Local.NextDouble();
    }
}
