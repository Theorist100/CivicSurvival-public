using System.Collections.Generic;
using Game;
using Game.Areas;
using Game.Common;
using Unity.Collections;
using Unity.Entities;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Components.Domain.Economy;
using CivicSurvival.Core.Components.Domain.Power;
using CivicSurvival.Core.Interfaces.Domain.Power;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Domains.Corruption.Systems.Modernization
{
    /// <summary>
    /// Owns the counterfeit equipment cleanup state for District Modernization:
    /// pending cleanup queues (serialized — published to cross-domain consumers via
    /// <see cref="ModernizationProgramsSnapshot"/>) and the per-procurement scratch
    /// (non-serialized — populated by PreScan, drained by QueueExactCleanup, read
    /// by the installer to avoid colliding with replacement installs).
    /// </summary>
    internal sealed class CounterfeitCleanupService
    {
        [NonEntityIndex] private readonly HashSet<int> m_PendingDistricts = new();
        [NonEntityIndex] private readonly HashSet<long> m_PendingBuildingKeys = new();

        // Per-procurement scratch — keys of buildings currently being cleaned up,
        // and the exact mod-entity lists to mark Deleted on confirm. Never serialized:
        // these belong to a single in-flight ActivateProcurement → ConfirmProcurement
        // sequence and are cleared at both ends.
        [NonEntityIndex] private readonly HashSet<long> m_CleanedUpBuildingKeys = new();
        [NonEntityIndex] private readonly List<Entity> m_CounterfeitEntitiesToDelete = new();
        [NonEntityIndex] private readonly List<Entity> m_BackupEntitiesToDelete = new();

        public int PendingDistrictCount => m_PendingDistricts.Count;
        public int PendingBuildingKeyCount => m_PendingBuildingKeys.Count;
        public int ScratchKeyCount => m_CleanedUpBuildingKeys.Count;

        public IReadOnlyCollection<int> PendingDistricts
            => m_PendingDistricts.Count == 0
                ? System.Array.Empty<int>()
                : new List<int>(m_PendingDistricts);

        /// <summary>Live HashSet enumeration of pending district indices — for the
        /// store's snapshot hash. Stable within a session; order may shuffle across
        /// sessions, which is acceptable for change-detection hashing.</summary>
        internal IEnumerable<int> EnumeratePendingDistricts() => m_PendingDistricts;

        /// <summary>Live HashSet enumeration of pending building keys — for the
        /// store's snapshot hash. See <see cref="EnumeratePendingDistricts"/>.</summary>
        internal IEnumerable<long> EnumeratePendingBuildingKeys() => m_PendingBuildingKeys;

        public void CopyPendingDistricts(List<int> target)
        {
            target.Clear();
            foreach (int districtIndex in m_PendingDistricts)
                target.Add(districtIndex);
        }

        public void CopyPendingBuildingKeys(List<long> target)
        {
            target.Clear();
            foreach (long buildingKey in m_PendingBuildingKeys)
                target.Add(buildingKey);
        }

        /// <summary>Drain the pending cleanup sets after MCS has processed them.
        /// Returns true if anything was cleared — caller publishes the snapshot.</summary>
        public bool ClearPending()
        {
            if (m_PendingDistricts.Count == 0 && m_PendingBuildingKeys.Count == 0)
                return false;
            m_PendingDistricts.Clear();
            m_PendingBuildingKeys.Clear();
            return true;
        }

        /// <summary>Mark a district for counterfeit cleanup: enqueues every counterfeit
        /// building's identity key from that district. Returns true if the district
        /// was newly added — caller publishes the snapshot.
        /// Buildings already tagged Deleted are skipped: their CounterfeitBattery is
        /// picked up by ModEntityCleanupSystem off the Deleted building, so persisting
        /// their key would only bloat the queue with entries that can never match.</summary>
        public bool MarkDistrictPending(int districtIndex, EntityQuery counterfeitQuery, ComponentLookup<Deleted> deletedLookup)
        {
            EnqueueLiveBuildingKeysForDistrict(districtIndex, counterfeitQuery, deletedLookup);
            return m_PendingDistricts.Add(districtIndex);
        }

        /// <summary>Rebuild <c>m_PendingBuildingKeys</c> from live components after load.
        /// Building identity keys are derived state (pending districts × live counterfeits)
        /// and are not persisted — the packed Index+Version is not remapped by the engine,
        /// so it must be re-derived from the now-remapped <see cref="CounterfeitBattery"/>
        /// components once <c>m_PendingDistricts</c> has been restored. Must run after the
        /// CounterfeitBattery entity refs are deserialized+remapped AND after the pending
        /// districts are restored.</summary>
        public void RebuildPendingBuildingKeysFromLive(EntityQuery counterfeitQuery, ComponentLookup<Deleted> deletedLookup)
        {
            m_PendingBuildingKeys.Clear();
            if (m_PendingDistricts.Count == 0)
                return;

            foreach (int districtIndex in m_PendingDistricts)
                EnqueueLiveBuildingKeysForDistrict(districtIndex, counterfeitQuery, deletedLookup);
        }

        /// <summary>Shared body of <see cref="MarkDistrictPending"/> and
        /// <see cref="RebuildPendingBuildingKeysFromLive"/>: enqueue the identity key of
        /// every live (non-Deleted) counterfeit battery whose installation district matches.
        /// Buildings already tagged Deleted are skipped — their CounterfeitBattery is reaped
        /// by ModEntityCleanupSystem off the Deleted building, so their key would only bloat
        /// the queue with entries that can never match.</summary>
        private void EnqueueLiveBuildingKeysForDistrict(int districtIndex, EntityQuery counterfeitQuery, ComponentLookup<Deleted> deletedLookup)
        {
            var counterfeits = counterfeitQuery.ToComponentDataArray<CounterfeitBattery>(Allocator.Temp);
            try
            {
                for (int i = 0; i < counterfeits.Length; i++)
                {
                    var cb = counterfeits[i];
                    if (cb.InstallationDistrictId != districtIndex)
                        continue;
                    if (deletedLookup.HasComponent(cb.Building.ToEntity()))
                        continue;
                    m_PendingBuildingKeys.Add(BuildingIdentityKey.Pack(cb.Building.Index, cb.Building.Version));
                }
            }
            finally
            {
                if (counterfeits.IsCreated) counterfeits.Dispose();
            }
        }

        public void ClearScratch()
        {
            m_CleanedUpBuildingKeys.Clear();
            m_CounterfeitEntitiesToDelete.Clear();
            m_BackupEntitiesToDelete.Clear();
        }

        public bool IsBeingCleaned(long buildingKey) => m_CleanedUpBuildingKeys.Contains(buildingKey);

        /// <summary>Count counterfeit batteries currently installed in a district —
        /// pure read for synchronous eligibility/cost queries from the UI path.
        /// Does not mutate scratch.</summary>
        public int CountCounterfeitInDistrict(
            int districtIndex,
            EntityQuery counterfeitQuery,
            ComponentLookup<CurrentDistrict> currentDistrictLookup)
        {
            int count = 0;
            var counterfeits = counterfeitQuery.ToComponentDataArray<CounterfeitBattery>(Allocator.Temp);
            try
            {
                for (int i = 0; i < counterfeits.Length; i++)
                {
                    var buildingEntity = counterfeits[i].Building.ToEntity();
                    if (!currentDistrictLookup.TryGetComponent(buildingEntity, out var district))
                        continue;
                    if (district.m_District.Index != districtIndex)
                        continue;
                    count++;
                }
            }
            finally
            {
                if (counterfeits.IsCreated) counterfeits.Dispose();
            }
            return count;
        }

        /// <summary>Pre-scan counterfeit batteries in the district, populating scratch
        /// with the building identity keys (always) and the exact mod-entity lists
        /// (when <paramref name="collectEntities"/> is true, used by confirm path).</summary>
        public void PreScan(
            int districtIndex,
            EntityQuery counterfeitQuery,
            ComponentLookup<CurrentDistrict> currentDistrictLookup,
            IBackupPowerLinkReader backupLinks,
            bool collectEntities)
        {
            if (collectEntities)
            {
                m_CounterfeitEntitiesToDelete.Clear();
                m_BackupEntitiesToDelete.Clear();
            }

            var entities = counterfeitQuery.ToEntityArray(Allocator.Temp);
            var counterfeits = counterfeitQuery.ToComponentDataArray<CounterfeitBattery>(Allocator.Temp);
            try
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    Entity cbEntity = entities[i];
                    var buildingEntity = counterfeits[i].Building.ToEntity();
                    if (!currentDistrictLookup.TryGetComponent(buildingEntity, out var district))
                        continue;
                    if (district.m_District.Index != districtIndex)
                        continue;

                    m_CleanedUpBuildingKeys.Add(BuildingIdentityKey.Pack(buildingEntity));
                    if (!collectEntities)
                        continue;

                    AddUniqueEntity(m_CounterfeitEntitiesToDelete, cbEntity);
                    if (backupLinks.TryGet(BuildingRef.FromEntity(buildingEntity), out var modEntity))
                    {
                        AddUniqueEntity(m_BackupEntitiesToDelete, modEntity);
                    }
                }
            }
            finally
            {
                if (entities.IsCreated) entities.Dispose();
                if (counterfeits.IsCreated) counterfeits.Dispose();
            }
        }

        /// <summary>Emit Deleted markers for every counterfeit/backup mod-entity collected
        /// during the most recent PreScan(collectEntities:true) call.</summary>
        public void QueueExactCleanup(
            EntityCommandBuffer ecb,
            ComponentLookup<CounterfeitBattery> counterfeitBatteryLookup,
            ComponentLookup<BackupPower> backupPowerLookup,
            ComponentLookup<Deleted> deletedLookup)
        {
            for (int i = 0; i < m_CounterfeitEntitiesToDelete.Count; i++)
            {
                var entity = m_CounterfeitEntitiesToDelete[i];
                if (counterfeitBatteryLookup.HasComponent(entity) && !deletedLookup.HasComponent(entity))
                    ecb.AddComponent<Deleted>(entity);
            }

            for (int i = 0; i < m_BackupEntitiesToDelete.Count; i++)
            {
                var entity = m_BackupEntitiesToDelete[i];
                if (backupPowerLookup.HasComponent(entity) && !deletedLookup.HasComponent(entity))
                    ecb.AddComponent<Deleted>(entity);
            }
        }

        public void Reset()
        {
            m_PendingDistricts.Clear();
            m_PendingBuildingKeys.Clear();
            ClearScratch();
        }

        public int[] SnapshotPendingDistricts()
        {
            if (m_PendingDistricts.Count == 0)
                return System.Array.Empty<int>();
            var array = new int[m_PendingDistricts.Count];
            m_PendingDistricts.CopyTo(array);
            return array;
        }

        /// <summary>Restore the pending cleanup districts from save. Building keys are not
        /// restored here: they are derived state rebuilt from live components via
        /// <see cref="RebuildPendingBuildingKeysFromLive"/> after entity remap completes.</summary>
        public void RestoreFromSave(IReadOnlyList<int> districts)
        {
            m_PendingDistricts.Clear();
            m_PendingBuildingKeys.Clear();
            for (int i = 0; i < districts.Count; i++)
                m_PendingDistricts.Add(districts[i]);
        }

        private static void AddUniqueEntity(List<Entity> target, Entity entity)
        {
            if (entity == Entity.Null || target.Contains(entity))
                return;
            target.Add(entity);
        }
    }
}
