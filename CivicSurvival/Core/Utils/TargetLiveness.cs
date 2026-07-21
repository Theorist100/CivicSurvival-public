using Game.Common;
using Unity.Entities;

namespace CivicSurvival.Core.Utils
{
    /// <summary>
    /// Shared gameplay target liveness contract: missing, Deleted, and Destroyed
    /// entities are not valid targets unless a system explicitly models rubble.
    /// Required component checks still belong to the caller's query or lookup.
    /// </summary>
    public static class TargetLiveness
    {
        public static bool IsLiveTarget(
            Entity target,
            EntityStorageInfoLookup storageInfoLookup,
            ComponentLookup<Deleted> deletedLookup,
            ComponentLookup<Destroyed> destroyedLookup)
        {
            return target != Entity.Null
                && storageInfoLookup.Exists(target)
                && !deletedLookup.HasComponent(target)
                && !destroyedLookup.HasComponent(target);
        }

        public static bool IsDeadTarget(
            Entity target,
            EntityStorageInfoLookup storageInfoLookup,
            ComponentLookup<Deleted> deletedLookup,
            ComponentLookup<Destroyed> destroyedLookup)
        {
            return !IsLiveTarget(target, storageInfoLookup, deletedLookup, destroyedLookup);
        }

        /// <summary>
        /// Target entity has ceased to exist: never set, storage slot gone, or Deleted.
        /// Unlike <see cref="IsDeadTarget"/> this does NOT treat vanilla <c>Destroyed</c>
        /// as gone — a destroyed building is a standing ruin that keeps its entity (and,
        /// for power plants, its ElectricityProducer; decompile: DestroySystem strips only
        /// consumer-side components) until bulldozed. Sidecars that model ruin state
        /// (wear, missile damage, construction, collapse, disaster) must be cleaned with
        /// this predicate so the ruin keeps its damage record.
        /// </summary>
        public static bool IsGoneTarget(
            Entity target,
            EntityStorageInfoLookup storageInfoLookup,
            ComponentLookup<Deleted> deletedLookup)
        {
            return target == Entity.Null
                || !storageInfoLookup.Exists(target)
                || deletedLookup.HasComponent(target);
        }
    }
}
