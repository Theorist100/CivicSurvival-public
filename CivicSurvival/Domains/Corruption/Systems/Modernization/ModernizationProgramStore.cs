using System.Collections.Generic;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Components.Lifecycle;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Serialization;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Domains.Corruption.Systems.Modernization
{
    /// <summary>
    /// Single owner of the per-district modernization program dictionary and its
    /// published snapshot. Mutations (Set/Remove/RecordFire) self-publish through
    /// <see cref="VersionedView{T}"/> — callers do not need to remember to publish.
    /// Cleanup state lives in <see cref="CounterfeitCleanupService"/>; this store
    /// reads its counts to assemble the cross-domain snapshot.
    /// </summary>
    internal sealed class ModernizationProgramStore
    {
        private const int SNAPSHOT_HASH_SEED = 17;
        private const int SNAPSHOT_HASH_MULTIPLIER = 31;

        [NonEntityIndex] private readonly Dictionary<int, DistrictModernizationData> m_Programs = new();
        private readonly CounterfeitCleanupService m_Cleanup;
        private readonly VersionedView<ModernizationProgramsSnapshot> m_View = new(ModernizationProgramsSnapshot.Empty);

        [System.NonSerialized] private int[] m_ActiveDistrictsCache = System.Array.Empty<int>();
        [System.NonSerialized] private int m_ActiveDistrictsObserverCursor = -1;

        public ModernizationProgramStore(CounterfeitCleanupService cleanup)
        {
            m_Cleanup = cleanup;
        }

        public int Count => m_Programs.Count;
        public IVersionedView<ModernizationProgramsSnapshot> View => m_View;

        public DistrictModernizationData? GetProgram(int districtIndex)
            => m_Programs.TryGetValue(districtIndex, out var program) ? program : null;

        public bool TryGetProgram(int districtIndex, out DistrictModernizationData program)
            => m_Programs.TryGetValue(districtIndex, out program);

        public IEnumerable<KeyValuePair<int, DistrictModernizationData>> Enumerate() => m_Programs;

        public IReadOnlyCollection<int> ActiveProgramDistricts
        {
            get
            {
                if (m_View.Observe(ref m_ActiveDistrictsObserverCursor).Changed)
                {
                    m_ActiveDistrictsCache = m_Programs.Count == 0
                        ? System.Array.Empty<int>()
                        : new List<int>(m_Programs.Keys).ToArray();
                }
                return m_ActiveDistrictsCache;
            }
        }

        public void SetProgram(int districtIndex, in DistrictModernizationData data)
        {
            m_Programs[districtIndex] = data;
            Publish();
        }

        public bool RemoveProgram(int districtIndex)
        {
            if (!m_Programs.Remove(districtIndex))
                return false;
            Publish();
            return true;
        }

        public bool RecordFire(int districtIndex, int day)
        {
            if (!m_Programs.TryGetValue(districtIndex, out var program))
                return false;
            if (day <= program.LastFireDay)
                return false;

            program.LastFireDay = day;
            program.FireCount++;
            m_Programs[districtIndex] = program;
            Publish();
            return true;
        }

        public void Publish()
        {
            m_View.Publish(new ModernizationProgramsSnapshot(
                m_Programs.Count,
                m_Cleanup.PendingDistrictCount,
                m_Cleanup.PendingBuildingKeyCount,
                CalculateHash()));
        }

        public void Reset()
        {
            m_Programs.Clear();
            m_ActiveDistrictsCache = System.Array.Empty<int>();
            m_ActiveDistrictsObserverCursor = -1;
        }

        public DistrictModernizationProgramPersistEntry[] SnapshotForSave()
        {
            if (m_Programs.Count == 0)
                return System.Array.Empty<DistrictModernizationProgramPersistEntry>();

            var entries = new DistrictModernizationProgramPersistEntry[m_Programs.Count];
            int i = 0;
            foreach (var kvp in m_Programs)
            {
                var program = kvp.Value;
                entries[i++] = new DistrictModernizationProgramPersistEntry(
                    kvp.Key,
                    program.HasProgram,
                    program.Contractor,
                    program.ActivationDay,
                    program.BuildingCount,
                    program.TotalCost,
                    program.KickbackEarned,
                    program.ExpectedKickback,
                    program.LastFireDay,
                    program.FireCount);
            }
            return entries;
        }

        public void RestoreFromSave(IReadOnlyList<DistrictModernizationProgramPersistEntry> entries)
        {
            m_Programs.Clear();
            for (int i = 0; i < entries.Count; i++)
            {
                var p = entries[i];
                m_Programs[p.DistrictIndex] = new DistrictModernizationData
                {
                    HasProgram = p.HasProgram,
                    Contractor = p.Contractor,
                    ActivationDay = p.ActivationDay,
                    BuildingCount = p.BuildingCount,
                    TotalCost = p.TotalCost,
                    KickbackEarned = p.KickbackEarned,
                    ExpectedKickback = p.ExpectedKickback,
                    LastFireDay = p.LastFireDay,
                    FireCount = p.FireCount
                };
            }
        }

        private int CalculateHash()
        {
            unchecked
            {
                int hash = SNAPSHOT_HASH_SEED;
                foreach (var kvp in m_Programs)
                {
                    hash = (hash * SNAPSHOT_HASH_MULTIPLIER) + kvp.Key;
                    hash = (hash * SNAPSHOT_HASH_MULTIPLIER) + (kvp.Value.HasProgram ? 1 : 0);
                    hash = (hash * SNAPSHOT_HASH_MULTIPLIER) + (int)kvp.Value.Contractor;
                    hash = (hash * SNAPSHOT_HASH_MULTIPLIER) + kvp.Value.ActivationDay;
                    hash = (hash * SNAPSHOT_HASH_MULTIPLIER) + kvp.Value.BuildingCount;
                    hash = (hash * SNAPSHOT_HASH_MULTIPLIER) + kvp.Value.TotalCost;
                    hash = (hash * SNAPSHOT_HASH_MULTIPLIER) + kvp.Value.KickbackEarned;
                    hash = (hash * SNAPSHOT_HASH_MULTIPLIER) + kvp.Value.ExpectedKickback;
                    hash = (hash * SNAPSHOT_HASH_MULTIPLIER) + kvp.Value.LastFireDay;
                    hash = (hash * SNAPSHOT_HASH_MULTIPLIER) + kvp.Value.FireCount;
                }

                foreach (int district in m_Cleanup.EnumeratePendingDistricts())
                    hash = (hash * SNAPSHOT_HASH_MULTIPLIER) + district;
                foreach (long buildingKey in m_Cleanup.EnumeratePendingBuildingKeys())
                    hash = (hash * SNAPSHOT_HASH_MULTIPLIER) + buildingKey.GetHashCode();
                return hash;
            }
        }
    }
}
