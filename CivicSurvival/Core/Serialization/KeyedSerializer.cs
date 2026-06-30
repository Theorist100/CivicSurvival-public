using Colossal.Serialization.Entities;
using Colossal.Logging;
using Unity.Entities;

namespace CivicSurvival.Core.Serialization
{
    /// <summary>
    /// Type tags for self-describing keyed serialization.
    /// Each field is written as: key (string) + tag (byte) + value.
    /// Tag enables skipping unknown fields without knowing their schema.
    /// </summary>
    public enum TypeTag : byte
    {
        None = 0,
        Bool = 1,
        U8 = 2,
        I32 = 3,
        I64 = 4,
        F32 = 5,
        F64 = 6,
        Str = 7,
        EnumByte = 8,
        EnumInt = 9,
        Buffer = 10,
        Entity = 11
    }

    /// <summary>
    /// Static helpers for keyed (self-describing) serialization.
    ///
    /// Format per block:
    ///   fieldCount (int)
    ///   [key (string) + tag (TypeTag byte) + value] × fieldCount
    ///
    /// Adding/removing/reordering fields is safe:
    ///   - Unknown key → Skip by tag
    ///   - Missing key → caller uses default
    /// </summary>
    public static class KeyedSerializer
    {
        private static readonly ILog Log = Mod.Log;

        /// <summary>Max fields per keyed block (guards against corrupted data while allowing large aggregate DTOs).</summary>
        private const int MAX_FIELDS_PER_BLOCK = 4096;

        /// <summary>
        /// Max elements in an unknown skipped buffer. Keep this aligned with the
        /// largest normal ReadBufferCount cap so skip paths are not looser than
        /// typed readers for corrupt identity-bearing fields.
        /// </summary>
        private const int MAX_BUFFER_ELEMENTS = 100_000;

        /// <summary>
        /// When true, a field tag mismatch throws instead of warn+skip+default.
        /// Production leaves this false (graceful forward-compat for added/removed
        /// fields). Serialization round-trip tests set it true so a write/read
        /// width drift (e.g. F64 written, F32 read) fails the test instead of
        /// being silently masked by the field default — the exact masking that
        /// hid the Cluster A defects (A-1/A-2/A-4) behind passing idempotence
        /// tests. Backed by AsyncLocal so a test enabling it flows only through
        /// its own execution context and cannot leak into xUnit's parallel test
        /// classes; production never sets it (defaults false → zero shipping
        /// behaviour change).
        /// </summary>
        private static readonly System.Threading.AsyncLocal<bool> s_strictTagMismatch = new();

        public static bool StrictTagMismatch
        {
            get => s_strictTagMismatch.Value;
            set => s_strictTagMismatch.Value = value;
        }

        // ════════════════════════════════════════════════════════════
        // WRITE — block header
        // ════════════════════════════════════════════════════════════

        public static void WriteBlockHeader<TWriter>(TWriter writer, int fieldCount)
            where TWriter : IWriter
        {
            writer.Write(fieldCount);
        }

        // ════════════════════════════════════════════════════════════
        // WRITE — scalar fields (key + tag + value)
        // ════════════════════════════════════════════════════════════

        public static void WriteField<TWriter>(TWriter writer, string key, bool value)
            where TWriter : IWriter
        {
            writer.Write(key);
            writer.Write((byte)TypeTag.Bool);
            writer.Write(value);
        }

        public static void WriteField<TWriter>(TWriter writer, string key, byte value)
            where TWriter : IWriter
        {
            writer.Write(key);
            writer.Write((byte)TypeTag.U8);
            writer.Write(value);
        }

        public static void WriteField<TWriter>(TWriter writer, string key, int value)
            where TWriter : IWriter
        {
            writer.Write(key);
            writer.Write((byte)TypeTag.I32);
            writer.Write(value);
        }

        public static void WriteField<TWriter>(TWriter writer, string key, long value)
            where TWriter : IWriter
        {
            writer.Write(key);
            writer.Write((byte)TypeTag.I64);
            writer.Write(value);
        }

        public static void WriteField<TWriter>(TWriter writer, string key, float value)
            where TWriter : IWriter
        {
            writer.Write(key);
            writer.Write((byte)TypeTag.F32);
            writer.Write(value);
        }

        public static void WriteField<TWriter>(TWriter writer, string key, double value)
            where TWriter : IWriter
        {
            writer.Write(key);
            writer.Write((byte)TypeTag.F64);
            writer.Write(value);
        }

        public static void WriteField<TWriter>(TWriter writer, string key, string value)
            where TWriter : IWriter
        {
            writer.Write(key);
            writer.Write((byte)TypeTag.Str);
            writer.Write(value);
        }

        // ════════════════════════════════════════════════════════════
        // WRITE — enum fields
        // ════════════════════════════════════════════════════════════

        public static void WriteEnumByteField<TWriter>(TWriter writer, string key, byte value)
            where TWriter : IWriter
        {
            writer.Write(key);
            writer.Write((byte)TypeTag.EnumByte);
            writer.Write(value);
        }

        public static void WriteEnumIntField<TWriter>(TWriter writer, string key, int value)
            where TWriter : IWriter
        {
            writer.Write(key);
            writer.Write((byte)TypeTag.EnumInt);
            writer.Write(value);
        }

        // ════════════════════════════════════════════════════════════
        // WRITE — buffer header (for collections)
        // ════════════════════════════════════════════════════════════

        /// <summary>
        /// Write buffer field header: key + Buffer tag + element count.
        /// Caller then writes each element as a keyed block (WriteBlockHeader + WriteField per element field).
        /// </summary>
        public static void WriteBufferHeader<TWriter>(TWriter writer, string key, int elementCount)
            where TWriter : IWriter
        {
            writer.Write(key);
            writer.Write((byte)TypeTag.Buffer);
            writer.Write(elementCount);
        }

        // ════════════════════════════════════════════════════════════
        // WRITE — entity reference (engine remaps via m_EntityTable on load)
        // ════════════════════════════════════════════════════════════

        /// <summary>
        /// Write an entity reference as a keyed field. The Colossal serializer
        /// compacts the entity to its serialized-table index on write and maps it
        /// back to the post-load entity on read (same remap path as vanilla Owner).
        /// This is the only save-stable way to persist a cross-entity reference —
        /// raw int Index/Version do NOT survive load (slot reassigned on deserialize).
        /// </summary>
        public static void WriteEntityField<TWriter>(TWriter writer, string key, Entity value)
            where TWriter : IWriter
        {
            writer.Write(key);
            writer.Write((byte)TypeTag.Entity);
            writer.Write(value);
        }

        // ════════════════════════════════════════════════════════════
        // READ — keyed block helpers
        // ════════════════════════════════════════════════════════════

        /// <summary>
        /// Read the field count at the start of a keyed block. Bounded to prevent OOM on corrupted data.
        /// </summary>
        public static int ReadBlockFieldCount<TReader>(TReader reader)
            where TReader : IReader
        {
            reader.Read(out int count);
            if (count < 0 || count > MAX_FIELDS_PER_BLOCK)
                throw new System.IO.InvalidDataException(
                    $"[KeyedSerializer] Block field count {count} exceeds MAX_FIELDS_PER_BLOCK ({MAX_FIELDS_PER_BLOCK}) — verify writer side or bump the limit");
            return count;
        }

        /// <summary>
        /// Read field header (key + tag) from the stream. Returns the tag as TypeTag.
        /// </summary>
        public static TypeTag ReadFieldHeader<TReader>(TReader reader, out string key)
            where TReader : IReader
        {
#pragma warning disable CIVIC144 // String read from IReader — length is bounded by CS2's binary format, not a collection size
            reader.Read(out key);
#pragma warning restore CIVIC144
            reader.Read(out byte rawTag);
            if (rawTag == 0 || rawTag > (byte)TypeTag.Entity)
            {
                throw new System.IO.InvalidDataException(
                    $"[KeyedSerializer] Invalid TypeTag {rawTag} for field '{key}' — stream is corrupted");
            }
            return (TypeTag)rawTag;
        }

        /// <summary>
        /// Verify that the actual tag matches the expected type for a known field.
        /// If mismatch (type drift under same key): skip the value, log warning, return false.
        /// Caller should use the field's default value when this returns false.
        /// </summary>
        public static bool ExpectTag<TReader>(TReader reader, TypeTag actual, TypeTag expected, string key)
            where TReader : IReader
        {
            if (actual == expected)
                return true;

            if (StrictTagMismatch)
                throw new System.IO.InvalidDataException(
                    $"[KeyedSerializer] Type mismatch for '{key}': expected {expected}, got {actual} — write/read width drift (strict mode)");

            Log.Warn($"[KeyedSerializer] Type mismatch for '{key}': expected {expected}, got {actual}. Skipping field.");
            Skip(reader, actual);
            return false;
        }

        // ════════════════════════════════════════════════════════════
        // READ — typed field readers (tag-checked)
        // ════════════════════════════════════════════════════════════

        /// <summary>Read bool field with tag check. Returns defaultValue on type mismatch.</summary>
        public static bool ReadBool<TReader>(TReader reader, TypeTag tag, string key, bool defaultValue = false)
            where TReader : IReader
        {
            if (!ExpectTag(reader, tag, TypeTag.Bool, key)) return defaultValue;
            reader.Read(out bool v);
            return v;
        }

        /// <summary>Read int field with tag check. Returns defaultValue on type mismatch.</summary>
        public static int ReadInt<TReader>(TReader reader, TypeTag tag, string key, int defaultValue = 0)
            where TReader : IReader
        {
            if (!ExpectTag(reader, tag, TypeTag.I32, key)) return defaultValue;
            reader.Read(out int v);
            return v;
        }

        /// <summary>Read long field with tag check. Returns defaultValue on type mismatch.</summary>
        public static long ReadLong<TReader>(TReader reader, TypeTag tag, string key, long defaultValue = 0)
            where TReader : IReader
        {
            if (!ExpectTag(reader, tag, TypeTag.I64, key)) return defaultValue;
            reader.Read(out long v);
            return v;
        }

        /// <summary>Read float field with tag check. Returns defaultValue on type mismatch.</summary>
        public static float ReadFloat<TReader>(TReader reader, TypeTag tag, string key, float defaultValue = 0f)
            where TReader : IReader
        {
            if (!ExpectTag(reader, tag, TypeTag.F32, key)) return defaultValue;
            reader.Read(out float v);
            return v;
        }

        /// <summary>Read double field with tag check. Returns defaultValue on type mismatch.</summary>
        public static double ReadDouble<TReader>(TReader reader, TypeTag tag, string key, double defaultValue = 0.0)
            where TReader : IReader
        {
            if (!ExpectTag(reader, tag, TypeTag.F64, key)) return defaultValue;
            reader.Read(out double v);
            return v;
        }

        /// <summary>Read string field with tag check. Returns defaultValue on type mismatch.</summary>
        public static string ReadString<TReader>(TReader reader, TypeTag tag, string key, string defaultValue = "")
            where TReader : IReader
        {
            if (!ExpectTag(reader, tag, TypeTag.Str, key)) return defaultValue;
            reader.Read(out string v);
            return v;
        }

        /// <summary>
        /// Read an entity reference field with tag check. The Colossal reader maps
        /// the serialized-table index back to the post-load entity (Entity.Null if
        /// the referent did not survive). Returns Entity.Null on type mismatch.
        /// </summary>
        public static Entity ReadEntity<TReader>(TReader reader, TypeTag tag, string key)
            where TReader : IReader
        {
            if (!ExpectTag(reader, tag, TypeTag.Entity, key)) return Entity.Null;
            reader.Read(out Entity v);
            return v;
        }

        // ════════════════════════════════════════════════════════════
        // DISTRICT KEY — the one persisted-district-identity contract
        // (shared by every district-keyed codec AND the G6 runtime).
        // ════════════════════════════════════════════════════════════

        /// <summary>
        /// Write a persisted district key. District identity is the raw
        /// entity/logical index from one global ECS pool — Unzoned/No-District
        /// is Engine.Districts.NO_DISTRICT_INDEX, real districts are full entity
        /// indices, far above any small cap in a built city.
        /// There is deliberately NO upper-bound clamp on write or read: the
        /// ad-hoc 500/10000/499 caps silently coerced real ids to 0/-1 on every
        /// save/load (Cluster A). Aggregate size is bounded by the record-count
        /// cap at the buffer header, never by the key value.
        /// </summary>
        public static void WriteDistrictKey<TWriter>(TWriter writer, string key, int districtIndex)
            where TWriter : IWriter
            => WriteField(writer, key, districtIndex);

        /// <summary>
        /// Read a district key written by <see cref="WriteDistrictKey{TWriter}"/>.
        /// No upper-bound clamp; <paramref name="invalid"/> is returned only on a
        /// type-tag drift (caller drops keys &lt; 0 as before).
        /// </summary>
        public static int ReadDistrictKey<TReader>(TReader reader, TypeTag tag, string key, int invalid = -1)
            where TReader : IReader
            => ReadInt(reader, tag, key, invalid);

        // ════════════════════════════════════════════════════════════
        // READ — skip unknown fields
        // ════════════════════════════════════════════════════════════
        // READ — tag-checked wrappers for SerializationGuard methods
        // ════════════════════════════════════════════════════════════

        /// <summary>ReadBoundedInt with tag check. Returns def on type mismatch.</summary>
        public static int ReadBoundedInt<TReader>(TReader reader, TypeTag tag, string key, int min, int max, int def)
            where TReader : IReader
        {
            if (!ExpectTag(reader, tag, TypeTag.I32, key)) return def;
            return SerializationGuard.ReadBoundedInt(reader, min, max, def, key);
        }

        /// <summary>ReadClampedInt with tag check. Returns min on type mismatch.</summary>
        public static int ReadClampedInt<TReader>(TReader reader, TypeTag tag, string key, int min, int max)
            where TReader : IReader
        {
            if (!ExpectTag(reader, tag, TypeTag.I32, key)) return min;
            return SerializationGuard.ReadClampedInt(reader, min, max, key);
        }

        /// <summary>ReadClampedInt with tag check and explicit type-mismatch fallback.</summary>
        public static int ReadClampedInt<TReader>(TReader reader, TypeTag tag, string key, int min, int max, int defaultOnTypeMismatch)
            where TReader : IReader
        {
            if (!ExpectTag(reader, tag, TypeTag.I32, key)) return defaultOnTypeMismatch;
            return SerializationGuard.ReadClampedInt(reader, min, max, key);
        }

        /// <summary>ReadMonotonicCounter with tag check. Returns min on type mismatch.</summary>
        public static int ReadMonotonicCounter<TReader>(TReader reader, TypeTag tag, string key, int min, int max)
            where TReader : IReader
        {
            if (!ExpectTag(reader, tag, TypeTag.I32, key)) return min;
            return SerializationGuard.ReadMonotonicCounter(reader, min, max, key);
        }

        /// <summary>Read monotonic counter with one preserved sentinel. Returns sentinel on type mismatch.</summary>
        public static int ReadMonotonicCounterWithSentinel<TReader>(TReader reader, TypeTag tag, string key, int sentinel, int min, int max)
            where TReader : IReader
        {
            if (!ExpectTag(reader, tag, TypeTag.I32, key)) return sentinel;
            return SerializationGuard.ReadMonotonicCounterWithSentinel(reader, sentinel, min, max, key);
        }

        /// <summary>Read bounded byte with tag check. Returns def on type mismatch.</summary>
        public static byte ReadBoundedByte<TReader>(TReader reader, TypeTag tag, string key, byte min, byte max, byte def)
            where TReader : IReader
        {
            if (!ExpectTag(reader, tag, TypeTag.U8, key)) return def;

            reader.Read(out byte value);
            if (value < min || value > max)
            {
                Log.Warn($"[KeyedSerializer] Byte '{key}'={value} out of [{min},{max}], using {def}");
                return def;
            }
            return value;
        }

        /// <summary>ReadBoundedLong with tag check. Returns 0 on type mismatch.</summary>
        public static long ReadBoundedLong<TReader>(TReader reader, TypeTag tag, string key, long min, long max)
            where TReader : IReader
        {
            if (!ExpectTag(reader, tag, TypeTag.I64, key)) return 0;
            return SerializationGuard.ReadBoundedLong(reader, min, max, key);
        }

        /// <summary>ReadSafeFloat (bounded) with tag check.</summary>
        public static float ReadSafeFloat<TReader>(TReader reader, TypeTag tag, string key, float min, float max, float def)
            where TReader : IReader
        {
            if (!ExpectTag(reader, tag, TypeTag.F32, key)) return def;
            return SerializationGuard.ReadSafeFloat(reader, min, max, def, key);
        }

        /// <summary>ReadSafeFloat (NaN/Inf only) with tag check.</summary>
        public static float ReadSafeFloat<TReader>(TReader reader, TypeTag tag, string key, float def)
            where TReader : IReader
        {
            if (!ExpectTag(reader, tag, TypeTag.F32, key)) return def;
            return SerializationGuard.ReadSafeFloat(reader, def, key);
        }

        /// <summary>ReadSafeFloatUnclamped with tag check.</summary>
        public static float ReadSafeFloatUnclamped<TReader>(TReader reader, TypeTag tag, string key, float def)
            where TReader : IReader
        {
            if (!ExpectTag(reader, tag, TypeTag.F32, key)) return def;
            return SerializationGuard.ReadSafeFloatUnclamped(reader, def, key);
        }

        /// <summary>ReadSafeDouble (NaN/Inf only) with tag check.</summary>
        public static double ReadSafeDouble<TReader>(TReader reader, TypeTag tag, string key, double def)
            where TReader : IReader
        {
            if (!ExpectTag(reader, tag, TypeTag.F64, key)) return def;
            return SerializationGuard.ReadSafeDouble(reader, def, key);
        }

        /// <summary>ReadSafeDouble (bounded) with tag check.</summary>
        public static double ReadSafeDouble<TReader>(TReader reader, TypeTag tag, string key, double min, double max, double def)
            where TReader : IReader
        {
            if (!ExpectTag(reader, tag, TypeTag.F64, key)) return def;
            return SerializationGuard.ReadSafeDouble(reader, min, max, def, key);
        }

        /// <summary>ReadEnumByte with tag check.</summary>
        public static T ReadEnumByte<TReader, T>(TReader reader, TypeTag tag, string key, T def)
            where TReader : IReader
            where T : struct, System.Enum
        {
            if (!ExpectTag(reader, tag, TypeTag.EnumByte, key)) return def;
            return SerializationGuard.ReadEnumByte<TReader, T>(reader, def, key);
        }

        /// <summary>ReadEnumInt with tag check.</summary>
        public static T ReadEnumInt<TReader, T>(TReader reader, TypeTag tag, string key, T def)
            where TReader : IReader
            where T : struct, System.Enum
        {
            if (!ExpectTag(reader, tag, TypeTag.EnumInt, key)) return def;
            return SerializationGuard.ReadEnumInt<TReader, T>(reader, def, key);
        }

        /// <summary>ReadCollectionSize with tag check (expects Buffer tag).</summary>
        public static int ReadBufferCount<TReader>(TReader reader, TypeTag tag, string key, int max)
            where TReader : IReader
        {
            if (!ExpectTag(reader, tag, TypeTag.Buffer, key)) return 0;
            reader.Read(out int count);
            if (count < 0 || count > max)
                throw new System.IO.InvalidDataException(
                    $"[KeyedSerializer] Buffer '{key}' has {count} elements (max {max}) — stream is corrupted");
            return count;
        }

        // ════════════════════════════════════════════════════════════
        // READ — skip unknown fields
        // ════════════════════════════════════════════════════════════

        /// <summary>
        /// Skip a single field value by its type tag. Handles Buffer recursively.
        /// </summary>
        public static void Skip<TReader>(TReader reader, TypeTag tag)
            where TReader : IReader
        {
            switch (tag)
            {
                case TypeTag.Bool:     reader.Read(out bool _);   break;
                case TypeTag.U8:       reader.Read(out byte _);   break;
                case TypeTag.I32:      reader.Read(out int _);    break;
                case TypeTag.I64:      reader.Read(out long _);   break;
                case TypeTag.F32:      reader.Read(out float _);  break;
                case TypeTag.F64:      reader.Read(out double _); break;
                case TypeTag.Str:      reader.Read(out string _); break;
                case TypeTag.EnumByte: reader.Read(out byte _);   break;
                case TypeTag.EnumInt:  reader.Read(out int _);    break;
                case TypeTag.Buffer:   SkipBuffer(reader);        break;
                case TypeTag.Entity:   reader.Read(out Entity _); break;
                case TypeTag.None:
                default:
                    throw new System.IO.InvalidDataException(
                        $"[KeyedSerializer] Cannot skip TypeTag {(byte)tag} — stream is corrupted");
            }
        }

        /// <summary>
        /// Skip an entire buffer: read element count, then skip each element's keyed block.
        /// </summary>
        public static void SkipBuffer<TReader>(TReader reader)
            where TReader : IReader
        {
            reader.Read(out int elementCount);
            if (elementCount < 0 || elementCount > MAX_BUFFER_ELEMENTS)
                throw new System.IO.InvalidDataException(
                    $"[KeyedSerializer] SkipBuffer element count {elementCount} out of [0,{MAX_BUFFER_ELEMENTS}] — stream is corrupted");
            for (int i = 0; i < elementCount; i++)
                SkipKeyedBlock(reader);
        }

        /// <summary>
        /// Skip one keyed block: read field count, then skip each key+tag+value triple.
        /// Works recursively for nested buffers.
        /// </summary>
        public static void SkipKeyedBlock<TReader>(TReader reader)
            where TReader : IReader
        {
            reader.Read(out int fieldCount);
            if (fieldCount < 0 || fieldCount > MAX_FIELDS_PER_BLOCK)
                throw new System.IO.InvalidDataException(
                    $"[KeyedSerializer] SkipKeyedBlock field count {fieldCount} exceeds MAX_FIELDS_PER_BLOCK ({MAX_FIELDS_PER_BLOCK}) — verify writer side or bump the limit");
            for (int f = 0; f < fieldCount; f++)
            {
                reader.Read(out string _);
                reader.Read(out byte rawTag);
                if (rawTag == 0 || rawTag > (byte)TypeTag.Entity)
                {
                    throw new System.IO.InvalidDataException(
                        $"[KeyedSerializer] Invalid TypeTag {rawTag} in keyed block — stream is corrupted");
                }
                Skip(reader, (TypeTag)rawTag);
            }
        }
    }
}
