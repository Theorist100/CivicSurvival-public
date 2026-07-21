using System.Collections.Generic;
using Colossal.Logging;
using Colossal.Serialization.Entities;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;
using Unity.Collections;
using Unity.Entities;

namespace CivicSurvival.Core.Serialization
{
    /// <summary>
    /// Result of reading a persisted district key.
    /// <list type="bullet">
    /// <item><see cref="None"/> — no district (spotter global sentinel -1) or a stale key
    /// whose referent did not survive load (engine remap returned Entity.Null).</item>
    /// <item><see cref="Unzoned"/> — the logical unzoned bucket (index 0); not a real entity.</item>
    /// <item><see cref="Live"/> — a real district that survived load; the out entity is the
    /// post-remap live district entity in the current world.</item>
    /// </list>
    /// </summary>
    public enum DistrictKeyReadResult : byte
    {
        None = 0,
        Unzoned = 1,
        Live = 2
    }

    /// <summary>
    /// Immutable snapshot of live district entities keyed by their current index.
    /// Built once per Serialize on the owner's main thread (<see cref="DistrictKeyCodec.BuildLiveMap"/>);
    /// districts number in the single digits, so the cost is negligible.
    /// </summary>
    public readonly struct LiveDistrictMap
    {
        // CIVIC014: keyed by district index by design — the value Entity carries the version, and
        // the map is a single-session snapshot rebuilt every Serialize (never persisted).
#pragma warning disable CIVIC014
        private readonly Dictionary<int, Entity> m_Map;
#pragma warning restore CIVIC014

        internal LiveDistrictMap(Dictionary<int, Entity> map)
        {
            m_Map = map;
        }

        public readonly bool TryGet(int index, out Entity entity)
        {
            if (m_Map != null)
                return m_Map.TryGetValue(index, out entity);
            entity = Entity.Null;
            return false;
        }
    }

    /// <summary>
    /// The one persisted-district-identity contract. District identity is written as a
    /// live <see cref="Entity"/> that rides the engine's serialized entity-remap table
    /// (<see cref="KeyedSerializer.WriteEntityField{TWriter}"/>), so it survives the slot
    /// reassignment that every entity gets on deserialize.
    ///
    /// Raw int index and raw Index+Version pairs do NOT survive load — the free-list of the
    /// loading world reissues both fields, so a persisted raw key silently points at a
    /// different (or dead) district. This codec replaces every such raw-key persist site.
    ///
    /// Wire format per logical key <c>K</c> (always <see cref="FIELDS_PER_KEY"/> fields):
    /// <code>
    ///   K + "K" : U8      kind — 0 None, 1 Unzoned, 2 Entity
    ///   K       : Entity  Entity.Null unless kind = Entity (rides the remap table)
    /// </code>
    /// Unzoned is encoded by kind (not Entity.Null) so a legitimate unzoned bucket stays
    /// distinguishable from a stale/dropped key.
    /// </summary>
    public static class DistrictKeyCodec
    {
        private static readonly ILog Log = Mod.Log;

        /// <summary>Number of keyed fields emitted per district key. Callers add this to their block headers.</summary>
        public const int FIELDS_PER_KEY = 2;

        /// <summary>Suffix appended to the base key for the kind discriminator field.</summary>
        public const string KindKeySuffix = "K";

        private const byte KindNone = 0;
        private const byte KindUnzoned = 1;
        private const byte KindEntity = 2;

        // ════════════════════════════════════════════════════════════
        // Live-district resolution (Serialize side / player-action producers)
        // ════════════════════════════════════════════════════════════

        /// <summary>
        /// Snapshot the live district entities of the current world keyed by index.
        /// Call once at the start of a codec owner's Serialize (main thread, EntityManager valid).
        /// </summary>
        public static LiveDistrictMap BuildLiveMap(EntityManager entityManager)
        {
            var map = new Dictionary<int, Entity>();
            var query = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<Game.Areas.District>(),
                ComponentType.Exclude<Game.Tools.Temp>(),
                ComponentType.Exclude<Game.Common.Deleted>());
            var districts = query.ToEntityArray(Allocator.Temp);
            query.Dispose();
            for (int i = 0; i < districts.Length; i++)
                map[districts[i].Index] = districts[i];
            districts.Dispose();
            return new LiveDistrictMap(map);
        }

        /// <summary>
        /// Resolve a logical district index to a live <see cref="DistrictRef"/> (index+version) by
        /// scanning the live districts. For player-action producers that only hold the UI index and
        /// must capture the current version before persisting. Unzoned (index 0) resolves to
        /// <see cref="DistrictRef.Null"/>; a negative index or a dead district returns false.
        /// </summary>
        public static bool TryResolveLive(EntityManager entityManager, int index, out DistrictRef district)
        {
            district = DistrictRef.Null;
            if (index < 0)
                return false;
            if (index == DistrictUtils.UNZONED_AREA_INDEX)
            {
                district = DistrictRef.Null;
                return true;
            }

            var query = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<Game.Areas.District>(),
                ComponentType.Exclude<Game.Tools.Temp>(),
                ComponentType.Exclude<Game.Common.Deleted>());
            var districts = query.ToEntityArray(Allocator.Temp);
            query.Dispose();
            try
            {
                for (int i = 0; i < districts.Length; i++)
                {
                    if (districts[i].Index == index)
                    {
                        district = DistrictRef.FromEntity(districts[i]);
                        return true;
                    }
                }
            }
            finally
            {
                districts.Dispose();
            }

            return false;
        }

        // ════════════════════════════════════════════════════════════
        // WRITE — two entries converging on one wire format
        // ════════════════════════════════════════════════════════════

        /// <summary>
        /// Write a district key from a logical index using the live map to capture the live entity.
        /// Unzoned (index 0) is encoded by kind; a negative index is None; an index missing from the
        /// live map is written kind=Entity with a null entity (reader drops it) and increments
        /// <paramref name="staleCount"/>.
        /// </summary>
        public static void Write<TWriter>(TWriter writer, string key, int districtIndex, in LiveDistrictMap map, ref int staleCount)
            where TWriter : IWriter
        {
            if (districtIndex == DistrictUtils.UNZONED_AREA_INDEX)
            {
                WritePair(writer, key, KindUnzoned, Entity.Null);
                return;
            }
            if (districtIndex < 0)
            {
                WritePair(writer, key, KindNone, Entity.Null);
                return;
            }
            if (map.TryGet(districtIndex, out var entity))
            {
                WritePair(writer, key, KindEntity, entity);
                return;
            }

            WritePair(writer, key, KindEntity, Entity.Null);
            staleCount++;
        }

        /// <summary>
        /// Write a district key from a <see cref="DistrictRef"/> (index+version already captured).
        /// <see cref="DistrictRef.Null"/> is Unzoned; a negative index is None; otherwise the
        /// reconstructed entity rides the remap table (write emits -1 if it is not live in the save).
        /// </summary>
        public static void Write<TWriter>(TWriter writer, string key, DistrictRef district)
            where TWriter : IWriter
        {
            if (district.IsNull)
            {
                WritePair(writer, key, KindUnzoned, Entity.Null);
                return;
            }
            if (district.Index < 0)
            {
                WritePair(writer, key, KindNone, Entity.Null);
                return;
            }

            WritePair(writer, key, KindEntity, district.ToEntity());
        }

        private static void WritePair<TWriter>(TWriter writer, string key, byte kind, Entity entity)
            where TWriter : IWriter
        {
            KeyedSerializer.WriteField(writer, key + KindKeySuffix, kind);
            KeyedSerializer.WriteEntityField(writer, key, entity);
        }

        // ════════════════════════════════════════════════════════════
        // READ — the caller reads the two keyed fields into locals, then resolves.
        // (Same two-key idiom as the former "d"+"v" DistrictRef pair; no off-by-one
        //  against the field-count loop.)
        // ════════════════════════════════════════════════════════════

        /// <summary>Read the kind discriminator field (stream key = baseKey + "K").</summary>
        public static byte ReadKind<TReader>(TReader reader, TypeTag tag, string baseKey)
            where TReader : IReader
            => KeyedSerializer.ReadBoundedByte(reader, tag, baseKey + KindKeySuffix, KindNone, KindEntity, KindNone);

        /// <summary>Read the entity field (stream key = baseKey). The engine reader remaps it to the post-load entity.</summary>
        public static Entity ReadEntity<TReader>(TReader reader, TypeTag tag, string baseKey)
            where TReader : IReader
            => KeyedSerializer.ReadEntity(reader, tag, baseKey);

        /// <summary>
        /// Resolve the two read fields into a result. <paramref name="live"/> is the post-load live
        /// district entity for <see cref="DistrictKeyReadResult.Live"/>, else Entity.Null.
        /// </summary>
        public static DistrictKeyReadResult Resolve(byte kind, Entity entity, out Entity live)
        {
            switch (kind)
            {
                case KindUnzoned:
                    live = Entity.Null;
                    return DistrictKeyReadResult.Unzoned;
                case KindEntity:
                    if (entity == Entity.Null)
                    {
                        // kind=Entity but the engine remap dropped it → stale referent.
                        live = Entity.Null;
                        return DistrictKeyReadResult.None;
                    }
                    live = entity;
                    return DistrictKeyReadResult.Live;
                default: // KindNone
                    live = Entity.Null;
                    return DistrictKeyReadResult.None;
            }
        }

        /// <summary>
        /// Resolve the two read fields straight to a runtime (index, version):
        /// Live → live index+version, Unzoned → (0, 0), None/stale → (-1, 0).
        /// </summary>
        public static void ResolveIndexVersion(byte kind, Entity entity, out int index, out int version)
        {
            switch (Resolve(kind, entity, out var live))
            {
                case DistrictKeyReadResult.Live:
                    index = live.Index;
                    version = live.Version;
                    break;
                case DistrictKeyReadResult.Unzoned:
                    index = DistrictUtils.UNZONED_AREA_INDEX;
                    version = 0;
                    break;
                default:
                    index = -1;
                    version = 0;
                    break;
            }
        }

        // ════════════════════════════════════════════════════════════
        // Verification logging (Axiom 1: log = truth; §8.2 runtime check)
        // ════════════════════════════════════════════════════════════

        public static void LogWrite(string codec, int count, int stale)
        {
            if (count > 0 || stale > 0)
                Log.Info($"[DistrictKey] {codec}: wrote {count} district keys ({stale} stale dropped)");
        }

        public static void LogRead(string codec, int count, int stale)
        {
            if (count > 0 || stale > 0)
                Log.Info($"[DistrictKey] {codec}: restored {count} district keys ({stale} stale dropped)");
        }
    }
}
