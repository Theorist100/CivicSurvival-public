using Unity.Collections;
using Unity.Entities;
using System;
using System.Collections.Generic;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Components.Domain.Power;

namespace CivicSurvival.Domains.Blackout.Data
{
    /// <summary>
    /// Thread-safe blackout configuration data using NativeContainers.
    /// Synced from managed Dictionary before each job execution.
    ///
    /// Note: original consumption is not stored here. Vanilla preserves WantedConsumption
    /// across save/load; BlackoutSystem re-captures its runtime baseline on the next frame.
    /// </summary>
    // IDISP007/008 suppressed: struct owns NativeContainers created in Create(), disposed in Dispose()
#pragma warning disable IDISP007, IDISP008
    public struct BlackoutData : IDisposable
    {
        private static readonly LogContext Log = new("BlackoutData");
        /// <summary>
        /// Per-district category blackout flags.
        /// Key = districtIndex * 10 + categoryId (BUG-BLK-005: Base-10 for readability, categoryId 0-5)
        /// Value = 1 if blackout active.
        /// </summary>
        [NonEntityIndex] public NativeHashMap<int, byte> CategoryBlackouts;

        /// <summary>Per-district schedule preset. Key = districtIndex, Value = SchedulePreset.</summary>
        [NonEntityIndex] public NativeHashMap<int, int> DistrictSchedules;

        /// <summary>VIP districts that never lose power.</summary>
        [NonEntityIndex] public NativeHashMap<int, byte> VIPDistricts;

        /// <summary>VIP Bypass districts - Wealthy households never lose power.</summary>
        [NonEntityIndex] public NativeHashMap<int, byte> VIPBypass;

        /// <summary>
        /// Current game hour (0-24).
        /// </summary>
        public float GameHour;

        // Minimum capacity to avoid frequent resizing
        private const int MIN_CAPACITY = 64;
        // Maximum reasonable capacity (prevent runaway allocation)
        private const int MAX_CAPACITY = 4096;

        public bool IsCreated => CategoryBlackouts.IsCreated;

        /// <summary>
        /// Create BlackoutData with specified initial capacity.
        /// </summary>
        public static BlackoutData Create(int capacity = Engine.DataStructures.MEDIUM_CAPACITY)
        {
            // Clamp capacity to reasonable range
            capacity = Math.Max(MIN_CAPACITY, Math.Min(MAX_CAPACITY, capacity));

            return new BlackoutData
            {
                CategoryBlackouts = new NativeHashMap<int, byte>(capacity, Allocator.Persistent),
                DistrictSchedules = new NativeHashMap<int, int>(capacity, Allocator.Persistent),
                VIPDistricts = new NativeHashMap<int, byte>(capacity, Allocator.Persistent),
                VIPBypass = new NativeHashMap<int, byte>(capacity, Allocator.Persistent),
                GameHour = 12f
            };
        }

        public void Dispose()
        {
            if (CategoryBlackouts.IsCreated) CategoryBlackouts.Dispose();
            if (DistrictSchedules.IsCreated) DistrictSchedules.Dispose();
            if (VIPDistricts.IsCreated) VIPDistricts.Dispose();
            if (VIPBypass.IsCreated) VIPBypass.Dispose();
        }

        /// <summary>
        /// Sync from managed dictionaries (call on main thread before job).
        /// Dynamically resizes if needed.
        /// </summary>
        public void SyncFromManaged(
            IReadOnlyDictionary<int, IReadOnlyCollection<BuildingCategory>> blackouts,
            IReadOnlyDictionary<int, SchedulePreset> schedules,
            IReadOnlyCollection<int> vipDistricts,
            IReadOnlyCollection<int> vipBypass,
            float gameHour)
        {
            // Safety check - don't operate on disposed containers
            if (!IsCreated)
            {
                Log.Warn("SyncFromManaged called on disposed data");
                return;
            }

            // Calculate required capacity
            int categoryCount = 0;
            foreach (var kvp in blackouts)
            {
                categoryCount += kvp.Value.Count;
            }

            int requiredCategoryCapacity = categoryCount;
            int requiredScheduleCapacity = schedules.Count;
            int requiredVipCapacity = vipDistricts.Count;
            int requiredVipBypassCapacity = vipBypass.Count;

            // Resize only containers whose own logical source outgrew capacity.
            bool needsResize = requiredCategoryCapacity > CategoryBlackouts.Capacity
                            || requiredScheduleCapacity > DistrictSchedules.Capacity
                            || requiredVipCapacity > VIPDistricts.Capacity
                            || requiredVipBypassCapacity > VIPBypass.Capacity;
            if (needsResize)
            {
                int categoryCapacity = GrowthCapacity(requiredCategoryCapacity, nameof(CategoryBlackouts));
                int scheduleCapacity = GrowthCapacity(requiredScheduleCapacity, nameof(DistrictSchedules));
                int vipCapacity = GrowthCapacity(requiredVipCapacity, nameof(VIPDistricts));
                int vipBypassCapacity = GrowthCapacity(requiredVipBypassCapacity, nameof(VIPBypass));

                ResizeContainers(categoryCapacity, scheduleCapacity, vipCapacity, vipBypassCapacity);
                if (Log.IsDebugEnabled)
                    Log.Debug($"Resized containers: categories={categoryCapacity}, schedules={scheduleCapacity}, vip={vipCapacity}, vipBypass={vipBypassCapacity}");
            }

            CategoryBlackouts.Clear();
            DistrictSchedules.Clear();
            VIPDistricts.Clear();
            VIPBypass.Clear();
            GameHour = gameHour;

            // Sync category blackouts
            foreach (var kvp in blackouts)
            {
                int districtIndex = kvp.Key;
                foreach (var category in kvp.Value)
                {
                    int categoryId = (int)category;

                    // BUG-BLK-005: Base-10 formula for district+category composite key
                    // INVARIANT: categoryId must be 0-9 to avoid key collision
                    if (categoryId < 0 || categoryId > 9)
                    {
                        Log.Error($"Invalid categoryId {categoryId} - must be 0-9");
                        continue;
                    }

                    int key = districtIndex * Engine.PowerGrid.CATEGORY_MULTIPLIER + categoryId;
                    CategoryBlackouts[key] = 1;
                }
            }

            // Sync schedules
            foreach (var kvp in schedules)
            {
                DistrictSchedules[kvp.Key] = (int)kvp.Value;
            }

            // Sync VIP districts
            foreach (var districtIndex in vipDistricts)
            {
                VIPDistricts[districtIndex] = 1;
            }

            // Sync VIP Bypass districts
            foreach (var districtIndex in vipBypass)
            {
                VIPBypass[districtIndex] = 1;
            }

        }

        /// <summary>
        /// Resize all containers to new capacity.
        /// Caller (SyncFromManaged) always Clear()s and repopulates after resize,
        /// so no need to copy old data — just dispose and recreate.
        /// </summary>
        private static int GrowthCapacity(int requiredCapacity, string name)
        {
            long requested = Math.Max((long)MIN_CAPACITY, (long)requiredCapacity * 2L);
            if (requested > MAX_CAPACITY)
            {
                Log.Warn($"{name} capacity exceeded MAX_CAPACITY ({requested} > {MAX_CAPACITY}); clamping");
                return MAX_CAPACITY;
            }

            return checked((int)requested);
        }

        private void ResizeContainers(int categoryCapacity, int scheduleCapacity, int vipCapacity, int vipBypassCapacity)
        {
            if (CategoryBlackouts.IsCreated) CategoryBlackouts.Dispose();
            if (DistrictSchedules.IsCreated) DistrictSchedules.Dispose();
            if (VIPDistricts.IsCreated) VIPDistricts.Dispose();
            if (VIPBypass.IsCreated) VIPBypass.Dispose();

            CategoryBlackouts = new NativeHashMap<int, byte>(categoryCapacity, Allocator.Persistent);
            DistrictSchedules = new NativeHashMap<int, int>(scheduleCapacity, Allocator.Persistent);
            VIPDistricts = new NativeHashMap<int, byte>(vipCapacity, Allocator.Persistent);
            VIPBypass = new NativeHashMap<int, byte>(vipBypassCapacity, Allocator.Persistent);
        }
    }
#pragma warning restore IDISP007, IDISP008
}
