using System;
using Unity.Collections;
using Unity.Entities;
using CivicSurvival.Core.Attributes;

namespace CivicSurvival.Core.Utils
{
    /// <summary>
    /// Result of classifying a power-plant building against a persistent identity registry.
    /// </summary>
    public enum PlantIdentityClass
    {
        /// <summary>Uninitialised / not classified. A classify call never returns this — it is the
        /// explicit <c>default(PlantIdentityClass)</c> so an uninitialised value does not silently
        /// read as a real classification.</summary>
        Unknown = 0,
        /// <summary>First time this plant slot is seen — a genuinely new plant.</summary>
        New = 1,
        /// <summary>Same stable Index AND same prefab (or same Version for sidecar-only callers) as a
        /// recorded plant — the SAME plant surviving structural churn (damage reaction, grid-node
        /// loss). Not new.</summary>
        Known = 2,
        /// <summary>Index matches a recorded plant but the prefab/Version differs — the slot was
        /// reused by a DIFFERENT building after the recorded one was demolished. A genuinely new
        /// plant.</summary>
        ReusedSlot = 3
    }

    /// <summary>
    /// One registry entry for a tracked power plant. Keyed by the building's stable
    /// <see cref="Entity.Index"/>; the prefab guards against Index reuse after demolition.
    /// </summary>
    public struct PlantIdentityRecord
    {
        /// <summary>Last-seen <see cref="Entity.Version"/> of the building. Tracked for
        /// migration/diagnostics only — identity does NOT depend on it (that is the bug fix).</summary>
        public int Version;

        /// <summary>Prefab entity of the building (from <see cref="Game.Prefabs.PrefabRef"/>).
        /// Used to distinguish "same plant, churned" from "a new building reusing the slot".</summary>
        public Entity Prefab;

        /// <summary>Stable plant id assigned to this plant, retained across sidecar churn so a
        /// re-created sidecar reuses the same id instead of allocating a new one. 0 = not yet
        /// assigned (the registry entry was created from a producer scan before an id was minted).</summary>
        public int StablePlantId;
    }

    /// <summary>
    /// Stable power-plant identity registry. A plant is identified by its building
    /// <see cref="Entity.Index"/> (which never changes for a live entity — the ECS slot is only
    /// freed and re-versioned on destroy) instead of the fragile <c>Index|Version</c> pack that
    /// the per-frame structural reaction to missile damage / grid-node loss desynchronises,
    /// causing existing plants to be re-flagged as new construction.
    ///
    /// Mirrors the immune <c>OperationalDamageSystem.m_DamageByBuilding</c> pattern (Index key +
    /// prefab/version guard payload). The single shared mechanism backs every consumer that used
    /// to dedup plants by the fragile key: <c>ConstructionDelaySystem</c>'s known-plant set and
    /// <c>EquipmentWearAssignSystem</c>'s existing-plant set; <c>ConstructionClassifiedState</c>
    /// (the resolver gate) keys its side-set on the same stable Index via
    /// <see cref="ClassificationKey"/>.
    ///
    /// Not Burst — used from managed system update bodies on the main thread.
    /// </summary>
    public struct StablePlantIdentityRegistry : IDisposable
    {
        // CIVIC097: the int key is the building's STABLE Entity.Index on purpose — that is the whole
        // point of this type. Slot reuse after demolition is caught by the prefab guard
        // (ClassifyAndRegister) or the Version guard (ClassifyByVersionAndRegister) at every access
        // site, so a recycled Index can never be mistaken for the demolished plant.
        [NonEntityIndex] private NativeHashMap<int, PlantIdentityRecord> m_ByIndex;

        public readonly bool IsCreated => m_ByIndex.IsCreated;

        public StablePlantIdentityRegistry(int capacity, Allocator allocator)
        {
            m_ByIndex = new NativeHashMap<int, PlantIdentityRecord>(capacity, allocator);
        }

        /// <summary>
        /// The classification side-set key for a plant. Stable across structural churn because it
        /// is the building Index alone — the same value the registry and the resolver gate agree on.
        /// </summary>
        public static long ClassificationKey(Entity building) => building.Index;

        /// <summary>
        /// Classify a live building and register/update it as the current occupant of its Index slot,
        /// guarding against slot reuse with the building's <b>prefab</b> (the consumer holds a live
        /// producer entity, so its <see cref="Game.Prefabs.PrefabRef"/> is available).
        /// <list type="bullet">
        /// <item><see cref="PlantIdentityClass.New"/> — Index not tracked; recorded as new.</item>
        /// <item><see cref="PlantIdentityClass.Known"/> — same Index+prefab; the stored Version is
        /// migrated to the live one (churn), identity preserved.</item>
        /// <item><see cref="PlantIdentityClass.ReusedSlot"/> — Index reused by a different prefab;
        /// the record is overwritten and the slot's new occupant is treated as new.</item>
        /// </list>
        /// </summary>
#pragma warning disable CIVIC097 // Stable Entity.Index key by design — slot reuse caught by prefab guard below.
        public PlantIdentityClass ClassifyAndRegister(Entity building, Entity prefab)
        {
            if (!m_ByIndex.TryGetValue(building.Index, out var record))
            {
                m_ByIndex[building.Index] = new PlantIdentityRecord { Version = building.Version, Prefab = prefab };
                return PlantIdentityClass.New;
            }

            if (record.Prefab == prefab)
            {
                // Same plant surviving churn — keep identity, migrate the tracked Version.
                if (record.Version != building.Version)
                {
                    record.Version = building.Version;
                    m_ByIndex[building.Index] = record;
                }
                return PlantIdentityClass.Known;
            }

            // Slot reused by a different building after the recorded plant was demolished.
            m_ByIndex[building.Index] = new PlantIdentityRecord { Version = building.Version, Prefab = prefab };
            return PlantIdentityClass.ReusedSlot;
        }
#pragma warning restore CIVIC097

#pragma warning disable CIVIC097 // Stable Entity.Index key by design — slot reuse caught by prefab guard.
        public readonly bool Contains(Entity building) => m_ByIndex.ContainsKey(building.Index);

        /// <summary>DIAGNOSTIC: read the current record for a building's Index slot without mutating.
        /// Returns false (record = default) when the slot is untracked.</summary>
        public readonly bool TryGetRecord(Entity building, out PlantIdentityRecord record)
            => m_ByIndex.TryGetValue(building.Index, out record);

        /// <summary>
        /// Read the stable plant id retained for a building's slot. Returns false when the slot is
        /// untracked or its id has not been minted yet (StablePlantId == 0). Used by
        /// <c>EquipmentWearAssignSystem</c> to reuse an id when a sidecar was destroyed (e.g. by the
        /// orphan cleanup on a grid-rebuild liveness blip) and must be re-created for the same plant.
        /// </summary>
        public readonly bool TryGetStablePlantId(Entity building, out int stablePlantId)
        {
            if (m_ByIndex.TryGetValue(building.Index, out var record) && record.StablePlantId != 0)
            {
                stablePlantId = record.StablePlantId;
                return true;
            }
            stablePlantId = 0;
            return false;
        }

        /// <summary>
        /// Record the stable plant id for a building's slot. The slot must already be tracked
        /// (via a preceding Classify call). Retained across sidecar churn so the next re-creation
        /// reuses this id instead of allocating a new one.
        /// </summary>
        public void SetStablePlantId(Entity building, int stablePlantId)
        {
            if (m_ByIndex.TryGetValue(building.Index, out var record))
            {
                record.StablePlantId = stablePlantId;
                m_ByIndex[building.Index] = record;
            }
        }

        /// <summary>
        /// Seed a slot directly from an existing sidecar (building + minted id), guarding slot reuse
        /// by prefab. Used on load to re-populate the transient registry from the persisted
        /// EquipmentWear sidecars BEFORE the assign pass, so post-load ids are reused not re-minted.
        /// </summary>
        public void SeedFromSidecar(Entity building, Entity prefab, int stablePlantId)
        {
            m_ByIndex[building.Index] = new PlantIdentityRecord
            {
                Version = building.Version,
                Prefab = prefab,
                StablePlantId = stablePlantId
            };
        }
#pragma warning restore CIVIC097

        public void Remove(int buildingIndex) => m_ByIndex.Remove(buildingIndex);

        public void Clear() => m_ByIndex.Clear();

        /// <summary>Enumerate tracked building Indices (for prune of demolished plants).</summary>
        public readonly NativeArray<int> GetTrackedIndices(Allocator allocator) => m_ByIndex.GetKeyArray(allocator);

        public void Dispose()
        {
            if (m_ByIndex.IsCreated)
                m_ByIndex.Dispose();
        }
    }
}
