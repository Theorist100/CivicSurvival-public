using System;
using Colossal.Logging;
using Colossal.Serialization.Entities;
using CivicSurvival.Core.Interfaces.Services;

namespace CivicSurvival.Core.Serialization
{
    /// <summary>
    /// Universal serialization helper for versioned save/load.
    /// Works with ANY base class (GameSystemBase, UISystemBase, ThrottledSystemBase, plain classes).
    ///
    /// Length-prefixed block pattern (stream-safe on version mismatch):
    /// <code>
    /// private const byte SAVE_VERSION = 1;
    ///
    /// public void Serialize&lt;TWriter&gt;(TWriter writer) where TWriter : IWriter
    /// {
    ///     var block = SerializationGuard.BeginBlock(writer, SAVE_VERSION);
    ///     try
    ///     {
    ///         writer.Write(m_Field1);
    ///     }
    ///     finally
    ///     {
    ///         SerializationGuard.EndBlock(writer, block);
    ///     }
    /// }
    ///
    /// public void Deserialize&lt;TReader&gt;(TReader reader) where TReader : IReader
    /// {
    ///     if (!SerializationGuard.TryBeginBlock(reader, SAVE_VERSION, out var version, out var block, nameof(MySystem)))
    ///     {
    ///         ResetToBootDefaults(ResetReason.VersionMismatch);
    ///         return;
    ///     }
    ///     try
    ///     {
    ///         if (version >= 1) reader.Read(out m_Field1);
    ///     }
    ///     catch (Exception)
    ///     {
    ///         ResetToBootDefaults(ResetReason.DeserializeFailed);
    ///     }
    ///     finally
    ///     {
    ///         SerializationGuard.EndBlock(reader, block);
    ///     }
    /// }
    /// </code>
    /// </summary>
    public static class SerializationGuard
    {
        private static readonly ILog Log = Mod.Log;

        /// <summary>
        /// Upper bound for persisted power-plant capacity fields, in kW. Real plants
        /// (incl. modded) reach several thousand MW — e.g. 3500 MW / 7500 MW seen in
        /// saves — so the old 1,000,000 kW (1000 MW) ceiling rejected legitimate values
        /// and reset OriginalCapacity to 0, silently corrupting wear/repair/damage math
        /// for large plants. 100 GW leaves headroom for mods while still catching a
        /// stream-desync garbage int. Matches the 100,000,000 ceiling already used for
        /// BackupPower watt-hour fields.
        /// </summary>
        public const int MaxPlantCapacityKW = 100_000_000;

        // ====================================================================
        // LOAD SESSION REPORT — aggregate save-format mismatches into one log
        // line at OnGameLoaded instead of one Error/Warn per affected system.
        // ====================================================================

        private static readonly System.Collections.Generic.List<(string System, string Reason)> s_LoadMismatches = new();
        private static readonly object s_LoadMismatchesLock = new();

        /// <summary>
        /// Reset the accumulated mismatch buffer. Call from
        /// <c>OnGamePreload(Purpose.LoadGame)</c> before any system Deserialize runs.
        /// </summary>
        public static void BeginLoadSession()
        {
            lock (s_LoadMismatchesLock) s_LoadMismatches.Clear();
        }

        /// <summary>
        /// Emit a single aggregate Warn summarising every save-format mismatch
        /// recorded since <see cref="BeginLoadSession"/>. Call from
        /// <c>OnGameLoaded</c> once all Deserialize methods have completed.
        /// Resets the buffer.
        /// </summary>
        public static void FlushLoadReport()
        {
            int count;
            string systems;
            lock (s_LoadMismatchesLock)
            {
                count = s_LoadMismatches.Count;
                if (count == 0) return;
                systems = string.Join(", ", s_LoadMismatches.ConvertAll(m => $"{m.System}({m.Reason})"));
                s_LoadMismatches.Clear();
            }
            Log.Warn($"Save-format mismatch: {count} systems reset to defaults — {systems}");
        }

        private static void RecordLoadMismatch(string? systemName, string reason)
        {
            lock (s_LoadMismatchesLock) s_LoadMismatches.Add((systemName ?? "Serialization", reason));
        }

        // ====================================================================
        // LENGTH-PREFIXED BLOCK API
        // ====================================================================

        /// <summary>
        /// Begin a length-prefixed write block and write version byte.
        /// Must be paired with <see cref="EndBlock{TWriter}(TWriter, WriterBlock)"/>.
        /// </summary>
        public static WriterBlock BeginBlock<TWriter>(TWriter writer, byte version)
            where TWriter : IWriter
        {
            WriterBlock block = writer.Begin();
            writer.Write(version);
            return block;
        }

        /// <summary>
        /// Begin a length-prefixed read block and validate version.
        /// On failure (version mismatch or corruption): skips the entire block automatically,
        /// returns false. Caller should invoke
        /// <see cref="IBootDefaultsReset.ResetToBootDefaults(ResetReason)"/> with
        /// <see cref="ResetReason.VersionMismatch"/> and return — no manual byte consumption needed.
        /// Must be paired with <see cref="EndBlock{TReader}(TReader, ReaderBlock)"/> on success.
        /// </summary>
        public static bool TryBeginBlock<TReader>(
            TReader reader,
            byte maxSupportedVersion,
            out byte readVersion,
            out ReaderBlock block,
            string? systemName = null)
            where TReader : IReader
        {
            block = reader.Begin(out _);
            reader.Read(out readVersion);

            if (readVersion > maxSupportedVersion)
            {
                RecordLoadMismatch(systemName, $"v{readVersion}>code v{maxSupportedVersion}");
                if (Log.isDebugEnabled)
                    Log.Debug($"[{systemName ?? "Serialization"}] Save version {readVersion} > code version {maxSupportedVersion}; reset to defaults.");
                reader.End(block);
                return false;
            }

            if (readVersion == 0)
            {
                RecordLoadMismatch(systemName, "v0/corrupt");
                if (Log.isDebugEnabled)
                    Log.Debug($"[{systemName ?? "Serialization"}] Invalid save version 0; reset to defaults.");
                reader.End(block);
                return false;
            }

            if (Log.isDebugEnabled) Log.Debug($"[{systemName ?? "Serialization"}] Deserializing v{readVersion}");
            return true;
        }

        /// <summary>End a write block (writes block size into the stream header).</summary>
        public static void EndBlock<TWriter>(TWriter writer, WriterBlock block)
            where TWriter : IWriter
        {
            writer.End(block);
        }

        /// <summary>End a read block (skips any remaining unread bytes in the block).</summary>
        public static void EndBlock<TReader>(TReader reader, ReaderBlock block)
            where TReader : IReader
        {
            reader.End(block);
        }

        /// <summary>Write a versioned block with no payload.</summary>
        public static void WriteEmptyBlock<TWriter>(TWriter writer, byte version)
            where TWriter : IWriter
        {
            var block = BeginBlock(writer, version);
            EndBlock(writer, block);
        }

        /// <summary>
        /// Log successful serialization (optional, for debugging).
        /// </summary>
        public static void LogSerialized(string systemName, byte version)
        {
            if (Log.isDebugEnabled) Log.Debug($"[{systemName}] Serialized v{version}");
        }

        // ====================================================================
        // DESERIALIZATION VALIDATION HELPERS
        // ====================================================================

        /// <summary>
        /// Read a byte from stream and validate it as enum value.
        /// Returns defaultValue if the raw byte is not a defined enum member.
        /// </summary>
        public static T ReadEnumByte<TReader, T>(TReader reader, T defaultValue, string fieldName)
            where TReader : IReader
            where T : struct, Enum
        {
            reader.Read(out byte raw);
            // Convert to enum's underlying type before IsDefined/ToObject (they require exact type match)
            var underlying = Enum.GetUnderlyingType(typeof(T));
            object converted;
            try
            {
                converted = underlying == typeof(byte) ? raw : Convert.ChangeType(raw, underlying);
            }
            catch (OverflowException)
            {
                Log.Warn($"[Deserialize] {fieldName}={raw} overflows {underlying.Name}, using {defaultValue}");
                return defaultValue;
            }
            if (!Enum.IsDefined(typeof(T), converted))
            {
                Log.Warn($"[Deserialize] Invalid {fieldName}={raw}, using {defaultValue}");
                return defaultValue;
            }
            return (T)Enum.ToObject(typeof(T), converted);
        }

        /// <summary>
        /// Read an int from stream and validate it as enum value.
        /// Returns defaultValue if the raw int is not a defined enum member.
        /// </summary>
        public static T ReadEnumInt<TReader, T>(TReader reader, T defaultValue, string fieldName)
            where TReader : IReader
            where T : struct, Enum
        {
            reader.Read(out int raw);
            // Convert to enum's underlying type before IsDefined/ToObject (they require exact type match)
            var underlying = Enum.GetUnderlyingType(typeof(T));
            object converted;
            try
            {
                converted = underlying == typeof(int) ? raw : Convert.ChangeType(raw, underlying);
            }
            catch (OverflowException)
            {
                Log.Warn($"[Deserialize] {fieldName}={raw} overflows {underlying.Name}, using {defaultValue}");
                return defaultValue;
            }
            if (!Enum.IsDefined(typeof(T), converted))
            {
                Log.Warn($"[Deserialize] Invalid {fieldName}={raw}, using {defaultValue}");
                return defaultValue;
            }
            return (T)Enum.ToObject(typeof(T), converted);
        }

        /// <summary>
        /// Read an int from stream and validate it is within [min, max].
        /// Returns defaultValue when out of range; does not clamp.
        /// </summary>
        public static int ReadBoundedInt<TReader>(TReader reader, int min, int max, int defaultValue, string fieldName)
            where TReader : IReader
        {
            reader.Read(out int value);
            if (value < min || value > max)
            {
                Log.Warn($"[Deserialize] {fieldName}={value} out of [{min},{max}], using {defaultValue}");
                return defaultValue;
            }
            return value;
        }

        /// <summary>
        /// Read an int from stream and clamp out-of-range values to [min, max].
        /// Use when reset-to-default would erase a recoverable resource count.
        /// </summary>
        public static int ReadClampedInt<TReader>(TReader reader, int min, int max, string fieldName)
            where TReader : IReader
        {
            reader.Read(out int value);
            if (value < min)
            {
                Log.Warn($"[Deserialize] {fieldName}={value} below min {min}, clamping");
                return min;
            }
            if (value > max)
            {
                Log.Warn($"[Deserialize] {fieldName}={value} above max {max}, clamping");
                return max;
            }
            return value;
        }

        /// <summary>
        /// Read a monotonic progress counter or day/wave watermark.
        /// Out-of-range values clamp to the nearest bound instead of resetting.
        /// </summary>
        public static int ReadMonotonicCounter<TReader>(TReader reader, int min, int max, string fieldName)
            where TReader : IReader
            => ReadClampedInt(reader, min, max, fieldName);

        /// <summary>
        /// Read a monotonic counter that also has one valid sentinel state.
        /// The sentinel is preserved exactly; other out-of-range values clamp to
        /// the nearest non-sentinel bound instead of resetting progress.
        /// </summary>
        public static int ReadMonotonicCounterWithSentinel<TReader>(TReader reader, int sentinel, int min, int max, string fieldName)
            where TReader : IReader
        {
            reader.Read(out int value);
            if (value == sentinel)
                return sentinel;
            if (value < min)
            {
                Log.Warn($"[Deserialize] {fieldName}={value} below min {min}, clamping");
                return min;
            }
            if (value > max)
            {
                Log.Warn($"[Deserialize] {fieldName}={value} above max {max}, clamping");
                return max;
            }
            return value;
        }

        /// <summary>
        /// Read an int from stream intended as collection size.
        /// Throws when out of range; callers must not clamp because unread elements
        /// would desynchronize the rest of the block.
        /// </summary>
        public static int ReadCollectionSize<TReader>(TReader reader, int maxSize, string fieldName)
            where TReader : IReader
        {
            reader.Read(out int count);
            if (count < 0)
            {
                throw new System.IO.InvalidDataException(
                    $"[Deserialize] {fieldName}={count} negative — stream is corrupted");
            }
            if (count > maxSize)
            {
                throw new System.IO.InvalidDataException(
                    $"[Deserialize] {fieldName}={count} exceeds max {maxSize} — cannot clamp without stream desync");
            }
            return count;
        }

        /// <summary>
        /// Read a long from stream and clamp to [min, max].
        /// Out-of-range → clamps silently (long corruption is rare, logging per-field is excessive).
        /// Use for amounts (Balance, Debt, Income).
        /// </summary>
        public static long ReadBoundedLong<TReader>(TReader reader, long min, long max, string fieldName)
            where TReader : IReader
        {
            reader.Read(out long value);
            if (value < min)
            {
                Log.Warn($"[Deserialize] {fieldName}={value} below min {min}, clamping");
                return min;
            }
            if (value > max)
            {
                Log.Warn($"[Deserialize] {fieldName}={value} above max {max}, clamping");
                return max;
            }
            return value;
        }

        /// <summary>
        /// Read a float from stream and validate it is finite and within [min, max].
        /// NaN/Infinity or out-of-range → returns <paramref name="defaultValue"/> with a warning log.
        /// </summary>
        public static float ReadSafeFloat<TReader>(TReader reader, float min, float max, float defaultValue, string fieldName)
            where TReader : IReader
        {
            reader.Read(out float value);
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                Log.Warn($"[Deserialize] {fieldName}={value} is NaN/Inf, using default {defaultValue}");
                return defaultValue;
            }
            if (value < min || value > max)
            {
                Log.Warn($"[Deserialize] {fieldName}={value} out of [{min},{max}], reset to default {defaultValue}");
                return defaultValue;
            }
            return value;
        }

        /// <summary>
        /// Read a float from stream and validate it is finite (not NaN or Infinity).
        /// NaN/Infinity → returns <paramref name="defaultValue"/> with a warning log.
        /// No range check — use the overload with min/max for bounded fields.
        /// </summary>
        public static float ReadSafeFloat<TReader>(TReader reader, float defaultValue, string fieldName)
            where TReader : IReader
        {
            reader.Read(out float value);
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                Log.Warn($"[Deserialize] {fieldName}={value} is NaN/Inf, using default {defaultValue}");
                return defaultValue;
            }
            return value;
        }

        /// <summary>
        /// Read a float from stream and validate it is finite (not NaN or Infinity).
        /// For values that are intentionally unbounded: world-space coordinates, game-time timestamps,
        /// physics velocities, fuel/hour accumulators, etc.
        /// NaN/Infinity → returns <paramref name="defaultValue"/> with a warning log.
        /// Use <see cref="ReadSafeFloat{TReader}(TReader,float,float,float,string)"/> for bounded gameplay values.
        /// </summary>
        public static float ReadSafeFloatUnclamped<TReader>(TReader reader, float defaultValue, string fieldName)
            where TReader : IReader
            => ReadSafeFloat(reader, defaultValue, fieldName);

        /// <summary>
        /// Read a double from stream and validate it is finite and within [min, max].
        /// NaN/Infinity/out-of-range → returns <paramref name="defaultValue"/> with a warning log.
        /// </summary>
        public static double ReadSafeDouble<TReader>(TReader reader, double min, double max, double defaultValue, string fieldName)
            where TReader : IReader
        {
            reader.Read(out double value);
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                Log.Warn($"[Deserialize] {fieldName}={value} is NaN/Inf, using default {defaultValue}");
                return defaultValue;
            }
            if (value < min || value > max)
            {
                Log.Warn($"[Deserialize] {fieldName}={value} out of [{min},{max}], reset to default {defaultValue}");
                return defaultValue;
            }
            return value;
        }

        /// <summary>
        /// Read a double from stream and validate it is finite (not NaN or Infinity).
        /// NaN/Infinity → returns <paramref name="defaultValue"/> with a warning log.
        /// No range check — use the overload with min/max for bounded fields.
        /// </summary>
        public static double ReadSafeDouble<TReader>(TReader reader, double defaultValue, string fieldName)
            where TReader : IReader
        {
            reader.Read(out double value);
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                Log.Warn($"[Deserialize] {fieldName}={value} is NaN/Inf, using default {defaultValue}");
                return defaultValue;
            }
            return value;
        }

        /// <summary>Advance stream past an int field without using the value.</summary>
        public static void SkipInt<TReader>(TReader reader) where TReader : IReader
            => reader.Read(out int _);

        /// <summary>Advance stream past a bool field without using the value.</summary>
        public static void SkipBool<TReader>(TReader reader) where TReader : IReader
            => reader.Read(out bool _);
    }

}
