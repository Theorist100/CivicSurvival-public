using Unity.Collections;
using Game.Buildings;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Components.Domain.Power;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Domains.NeighborEnvy.Logic
{
    /// <summary>
    /// Pure logic for power state calculations.
    /// No dependencies on System internals.
    /// </summary>
    internal static class EnvyPowerStateLogic
    {
        private static readonly LogContext Log = new("NeighborEnvyPowerState");
        public const int CATEGORY_KEY_STRIDE = 256;

        /// <summary>
        /// FIX P2-NE-001: Calculate composite key for district + category blackout lookup.
        /// Formula: districtIndex * 256 + category. The wide stride keeps future category values
        /// from colliding with neighboring district keys.
        /// </summary>
        public static int GetCategoryBlackoutKey(int districtIndex, BuildingCategory category)
        {
            return districtIndex * CATEGORY_KEY_STRIDE + (int)category;
        }

        public static int CountCategoryBlackoutEntries(DistrictStateSnapshot snapshot)
        {
            int count = 0;
            if (snapshot.DistrictBlackouts == null)
                return count;

            foreach (var kvp in snapshot.DistrictBlackouts)
                if (kvp.Value != null)
                    count += kvp.Value.Count;

            return count;
        }

        public static int GetCategoryBlackoutCapacity(DistrictStateSnapshot snapshot)
            => WithHeadroom(CountCategoryBlackoutEntries(snapshot), Engine.DataStructures.MEDIUM_CAPACITY);

        public static int GetDistrictScheduleCapacity(DistrictStateSnapshot snapshot)
            => WithHeadroom(snapshot.DistrictSchedules != null ? snapshot.DistrictSchedules.Count : 0, Engine.DataStructures.MEDIUM_CAPACITY);

        public static int GetVipDistrictCapacity(DistrictStateSnapshot snapshot)
            => WithHeadroom(snapshot.VIPDistricts != null ? snapshot.VIPDistricts.Count : 0, Engine.NeighborEnvy.VIP_DISTRICT_SET_CAPACITY);

        private static int WithHeadroom(int count, int minimum)
        {
            int withHeadroom = count <= 0 ? minimum : count + (count / 4) + 8;
            return withHeadroom > minimum ? withHeadroom : minimum;
        }

        /// <summary>
        /// Check if building is powered based on district blackout state.
        /// </summary>
        public static bool IsBuildingPowered(
            int districtIndex,
            float gameHour,
            NativeHashMap<int, byte> categoryBlackouts,
            NativeHashMap<int, int> districtSchedules,
            NativeHashSet<int> vipDistricts,
            int cityScheduleId)
        {
            // VIP districts are always powered
            if (vipDistricts.Contains(districtIndex))
                return true;

            // Check if residential category is blacked out
            int key = GetCategoryBlackoutKey(districtIndex, BuildingCategory.Residential);
            if (categoryBlackouts.ContainsKey(key))
                return false;

            // Check schedule (with phase offset)
            if (districtSchedules.TryGetValue(districtIndex, out int scheduleId) || (scheduleId = cityScheduleId) > 0)
            {
                if (ScheduleHelper.IsBlackoutActive(scheduleId, gameHour, districtIndex))
                    return false;
            }

            return true;
        }

        public static bool IsBuildingPowered(
            ElectricityConsumer consumer,
            bool hasBlackoutState,
            bool isBlackoutEnabled,
            BlackoutState blackoutState,
            float gridPowerThreshold)
        {
            if (hasBlackoutState && blackoutState.ServedByBackup)
                return true;

            if (hasBlackoutState && isBlackoutEnabled)
                return false;

            if (consumer.m_WantedConsumption <= 0)
                return true;

            return consumer.m_FulfilledConsumption >= consumer.m_WantedConsumption * gridPowerThreshold;
        }

        /// <summary>
        /// Sync blackout state from snapshot to native containers.
        /// </summary>
        public static void SyncBlackoutData(
            DistrictStateSnapshot snapshot,
            ref NativeHashMap<int, byte> categoryBlackouts,
            ref NativeHashMap<int, int> districtSchedules,
            ref NativeHashSet<int> vipDistricts)
        {
            if (snapshot.DistrictBlackouts != null)
            {
                foreach (var kvp in snapshot.DistrictBlackouts)
                {
                    int districtIndex = kvp.Key;
                    foreach (var category in kvp.Value)
                    {
                        // FIX P2-NE-001: Use extracted method instead of hardcoded formula
                        int key = GetCategoryBlackoutKey(districtIndex, category);
                        if (!categoryBlackouts.TryAdd(key, 1) && !categoryBlackouts.ContainsKey(key))
                            Log.Warn($"NeighborEnvy category blackout snapshot TryAdd failed at district {districtIndex}, category {category}");
                    }
                }
            }

            if (snapshot.DistrictSchedules != null)
            {
                foreach (var kvp in snapshot.DistrictSchedules)
                {
                    if (!districtSchedules.TryAdd(kvp.Key, (int)kvp.Value) && !districtSchedules.ContainsKey(kvp.Key))
                        Log.Warn($"NeighborEnvy district schedule snapshot TryAdd failed at district {kvp.Key}");
                }
            }

            if (snapshot.VIPDistricts != null)
            {
                foreach (var districtIndex in snapshot.VIPDistricts)
                {
                    if (!vipDistricts.Add(districtIndex) && !vipDistricts.Contains(districtIndex))
                        Log.Warn($"NeighborEnvy VIP snapshot TryAdd failed at district {districtIndex}");
                }
            }
        }
    }
}
