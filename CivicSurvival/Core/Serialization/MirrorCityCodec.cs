using System;
using System.Collections.Generic;
using CivicSurvival.Core.Components.Domain.GridWarfare;
using CivicSurvival.Core.Types;
using Colossal.Serialization.Entities;
using Unity.Mathematics;

namespace CivicSurvival.Core.Serialization
{
    /// <summary>
    /// Pure value codec for the mirror-city save block — Layer 3 (mirrors <see cref="EnemyStateCodec"/>
    /// and <c>DistrictModernizationCodec</c>). Persists the authoritative model: the
    /// <see cref="MirrorCityState"/> header plus the full <see cref="EnemyTarget"/> list. The three
    /// stability axes are NOT persisted here — they are a derived cache rebuilt from these targets.
    /// No World / EntityManager / RNG state; that glue stays in <c>MirrorCitySystem</c>.
    ///
    /// Keyed wire format tolerates missing/added keys (<c>default: Skip</c>), so a pre-mirror-city save
    /// simply carries no city and loads as un-generated (regenerated on the first tick).
    /// </summary>
    public static class MirrorCityCodec
    {
        /// <summary>Upper bound on persisted targets (guards corrupt saves; a generated city is far smaller).</summary>
        public const int MaxTargets = 512;

        // Header field count: variantId, genVersion, generated, tuningHash, capCutPhysical,
        // capCutDigital, capCutSocial, targets(buffer) = 8.
        private const int HeaderFieldCount = 8;

        // Per-target block field count: id, axis, tier, x, z, contrib, hp, maxHp, rebuild, rebuildAt,
        // aaRange, destroyed = 12.
        private const int TargetFieldCount = 12;

        public static void Write<TWriter>(in MirrorCityState state, IReadOnlyList<EnemyTarget> targets, TWriter writer)
            where TWriter : IWriter
        {
            KeyedSerializer.WriteBlockHeader(writer, HeaderFieldCount);
            KeyedSerializer.WriteField(writer, "variantId", state.VariantId);
            KeyedSerializer.WriteField(writer, "genVersion", state.GenVersion);
            KeyedSerializer.WriteField(writer, "generated", state.Generated);
            KeyedSerializer.WriteField(writer, "tuningHash", state.TuningHash);
            KeyedSerializer.WriteField(writer, "capCutPhysical", state.CapCutPhysical);
            KeyedSerializer.WriteField(writer, "capCutDigital", state.CapCutDigital);
            KeyedSerializer.WriteField(writer, "capCutSocial", state.CapCutSocial);
            WriteTargets(writer, targets);
        }

        public static void Read<TReader>(TReader reader, out MirrorCityState state, out EnemyTarget[] targets, out bool variantIdOutOfRange)
            where TReader : IReader
        {
            variantIdOutOfRange = false;
            int variantId = 0;
            int genVersion = 0;
            bool generated = false;
            int tuningHash = 0;
            float capCutPhysical = 0f;
            float capCutDigital = 0f;
            float capCutSocial = 0f;
            var readTargets = Array.Empty<EnemyTarget>();

            int fieldCount = KeyedSerializer.ReadBlockFieldCount(reader);
            for (int i = 0; i < fieldCount; i++)
            {
                var tag = KeyedSerializer.ReadFieldHeader(reader, out var key);
                switch (key)
                {
                    // Validation at the data boundary: a variant id is only meaningful inside the
                    // catalog, so anything outside [0, Count) (corruption, a rollback from a build
                    // with a larger catalog) resets to 0 HERE — every downstream Get (generation,
                    // UI, snapshot builder) then holds a valid id by construction, with no
                    // per-call-site range checks. The out-of-range fact is SIGNALLED to the caller
                    // (not swallowed): a generated city whose id cannot name a catalog variant is
                    // internally inconsistent — its targets were generated for a different map —
                    // and the deserializer must regenerate rather than pair variant-0's map with
                    // a foreign target buffer.
                    case "variantId":
                    {
                        int rawVariantId = KeyedSerializer.ReadInt(reader, tag, "variantId", 0);
                        variantIdOutOfRange = rawVariantId < 0 || rawVariantId >= Logic.MirrorCityVariantCatalog.Count;
                        variantId = variantIdOutOfRange ? 0 : rawVariantId;
                        break;
                    }
                    case "genVersion": genVersion = KeyedSerializer.ReadBoundedInt(reader, tag, "genVersion", 0, int.MaxValue, 0); break;
                    case "generated": generated = KeyedSerializer.ReadBool(reader, tag, "generated", false); break;
                    case "tuningHash": tuningHash = KeyedSerializer.ReadInt(reader, tag, "tuningHash", 0); break;
                    case "capCutPhysical": capCutPhysical = KeyedSerializer.ReadSafeFloat(reader, tag, "capCutPhysical", 0f, float.MaxValue, 0f); break;
                    case "capCutDigital": capCutDigital = KeyedSerializer.ReadSafeFloat(reader, tag, "capCutDigital", 0f, float.MaxValue, 0f); break;
                    case "capCutSocial": capCutSocial = KeyedSerializer.ReadSafeFloat(reader, tag, "capCutSocial", 0f, float.MaxValue, 0f); break;
                    case "targets": readTargets = ReadTargets(reader, tag); break;
                    default: KeyedSerializer.Skip(reader, tag); break;
                }
            }

            state = new MirrorCityState
            {
                VariantId = variantId,
                GenVersion = genVersion,
                Generated = generated,
                TuningHash = tuningHash,
                CapCutPhysical = capCutPhysical,
                CapCutDigital = capCutDigital,
                CapCutSocial = capCutSocial
            };
            targets = readTargets;
        }

        private static void WriteTargets<TWriter>(TWriter writer, IReadOnlyList<EnemyTarget> targets)
            where TWriter : IWriter
        {
            // Callers always hand a concrete list (Array.Empty for "no city"); a null here is a
            // programming error and fails loud rather than silently persisting an empty buffer.
            if (targets == null)
                throw new ArgumentNullException(nameof(targets));

            int count = targets.Count;
            if (count > MaxTargets)
                count = MaxTargets;

            KeyedSerializer.WriteBufferHeader(writer, "targets", count);
            for (int i = 0; i < count; i++)
            {
                var t = targets[i];
                KeyedSerializer.WriteBlockHeader(writer, TargetFieldCount);
                KeyedSerializer.WriteField(writer, "id", (int)t.Id);
                KeyedSerializer.WriteEnumByteField(writer, "axis", (byte)t.Axis);
                KeyedSerializer.WriteEnumByteField(writer, "tier", (byte)t.Tier);
                KeyedSerializer.WriteField(writer, "x", t.X);
                KeyedSerializer.WriteField(writer, "z", t.Z);
                KeyedSerializer.WriteField(writer, "contrib", t.Contrib);
                KeyedSerializer.WriteField(writer, "hp", t.Hp);
                KeyedSerializer.WriteField(writer, "maxHp", t.MaxHp);
                KeyedSerializer.WriteField(writer, "rebuild", t.RebuildProgress);
                KeyedSerializer.WriteField(writer, "rebuildAt", t.RebuildAtHours);
                KeyedSerializer.WriteField(writer, "aaRange", t.AaRange);
                KeyedSerializer.WriteField(writer, "destroyed", t.DestroyedForever);
            }
        }

        private static EnemyTarget[] ReadTargets<TReader>(TReader reader, TypeTag tag)
            where TReader : IReader
        {
            int count = KeyedSerializer.ReadBufferCount(reader, tag, "targets", MaxTargets);
            var entries = new EnemyTarget[count];

            for (int i = 0; i < count; i++)
            {
                int id = 0;
                var axis = AttackCategory.Kinetic;
                var tier = MirrorTargetTier.Regular;
                float x = 0f;
                float z = 0f;
                float contrib = 0f;
                float hp = 0f;
                float maxHp = 0f;
                float rebuild = 1f;
                float rebuildAt = 0f;
                float aaRange = 0f;
                bool destroyed = false;

                int blockFields = KeyedSerializer.ReadBlockFieldCount(reader);
                for (int f = 0; f < blockFields; f++)
                {
                    var fieldTag = KeyedSerializer.ReadFieldHeader(reader, out var fieldKey);
                    switch (fieldKey)
                    {
                        case "id": id = KeyedSerializer.ReadBoundedInt(reader, fieldTag, "id", 0, ushort.MaxValue, 0); break;
                        case "axis": axis = KeyedSerializer.ReadEnumByte<TReader, AttackCategory>(reader, fieldTag, "axis", AttackCategory.Kinetic); break;
                        case "tier": tier = KeyedSerializer.ReadEnumByte<TReader, MirrorTargetTier>(reader, fieldTag, "tier", MirrorTargetTier.Regular); break;
                        case "x": x = KeyedSerializer.ReadSafeFloatUnclamped(reader, fieldTag, "x", 0f); break;
                        case "z": z = KeyedSerializer.ReadSafeFloatUnclamped(reader, fieldTag, "z", 0f); break;
                        case "contrib": contrib = KeyedSerializer.ReadSafeFloat(reader, fieldTag, "contrib", 0f, float.MaxValue, 0f); break;
                        case "hp": hp = KeyedSerializer.ReadSafeFloat(reader, fieldTag, "hp", 0f, float.MaxValue, 0f); break;
                        case "maxHp": maxHp = KeyedSerializer.ReadSafeFloat(reader, fieldTag, "maxHp", 0f, float.MaxValue, 0f); break;
                        case "rebuild": rebuild = KeyedSerializer.ReadSafeFloat(reader, fieldTag, "rebuild", 0f, 1f, 1f); break;
                        case "rebuildAt": rebuildAt = KeyedSerializer.ReadSafeFloat(reader, fieldTag, "rebuildAt", 0f, float.MaxValue, 0f); break;
                        case "aaRange": aaRange = KeyedSerializer.ReadSafeFloat(reader, fieldTag, "aaRange", 0f, float.MaxValue, 0f); break;
                        case "destroyed": destroyed = KeyedSerializer.ReadBool(reader, fieldTag, "destroyed", false); break;
                        default: KeyedSerializer.Skip(reader, fieldTag); break;
                    }
                }

                entries[i] = new EnemyTarget
                {
                    Id = (ushort)math.clamp(id, 0, ushort.MaxValue),
                    Axis = axis,
                    Tier = tier,
                    X = x,
                    Z = z,
                    Contrib = contrib,
                    Hp = hp,
                    MaxHp = maxHp,
                    RebuildProgress = math.saturate(rebuild),
                    RebuildAtHours = rebuildAt,
                    AaRange = aaRange,
                    DestroyedForever = destroyed
                };
            }

            return entries;
        }
    }
}
