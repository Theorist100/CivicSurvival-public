using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;

namespace CivicSurvival.Core.Components.Lifecycle
{
#pragma warning disable S2326 // Generic argument anchors the singleton component contract.
    public struct EnsureSingletonPolicy<T>
        where T : unmanaged, IComponentData
    {
        public Entity Preferred;
        public Action<EntityManager, Entity, Entity>? MergeDuplicate;
        public Action<EntityManager, Entity>? EnsureShape;
        public bool? DestroyDuplicates;
    }

    public struct EnsurePairedPolicy<TPrimary, TSecondary>
        where TPrimary : unmanaged, IComponentData
        where TSecondary : unmanaged, IComponentData
    {
        public Entity Preferred;
        public Action<EntityManager, Entity, Entity>? MergeDuplicate;
        public Action<EntityManager, Entity>? EnsureShape;
        public bool? RemoveComponentsNotEntity;
    }
#pragma warning restore S2326

    public static class CivicSingleton
    {
        public static Entity Ensure<T>(EntityManager em, in T defaultValue)
            where T : unmanaged, IComponentData
        {
            var policy = default(EnsureSingletonPolicy<T>);
            using var query = em.CreateEntityQuery(ComponentType.ReadWrite<T>());
            return Ensure(em, query, defaultValue, policy);
        }

        public static Entity Ensure<T>(
            EntityManager em,
            in T defaultValue,
            in EnsureSingletonPolicy<T> policy)
            where T : unmanaged, IComponentData
        {
            using var query = em.CreateEntityQuery(ComponentType.ReadWrite<T>());
            return Ensure(em, query, defaultValue, policy);
        }

        public static Entity Ensure<T>(EntityManager em, EntityQuery query, in T defaultValue)
            where T : unmanaged, IComponentData
        {
            var policy = default(EnsureSingletonPolicy<T>);
            return Ensure(em, query, defaultValue, policy);
        }

        public static Entity Ensure<T>(
            EntityManager em,
            EntityQuery query,
            in T defaultValue,
            in EnsureSingletonPolicy<T> policy)
            where T : unmanaged, IComponentData
        {
            using var entities = query.ToEntityArray(Allocator.Temp);

            var canonical = PickCanonical<T>(em, entities, policy.Preferred);
            if (canonical == Entity.Null)
            {
                canonical = em.CreateEntity();
                em.AddComponentData(canonical, defaultValue);
            }

            var destroyDuplicates = policy.DestroyDuplicates.GetValueOrDefault(true);
            var comparer = EqualityComparer<T>.Default;
            for (int i = 0; i < entities.Length; i++)
            {
                var duplicate = entities[i];
                if (duplicate == canonical || !em.Exists(duplicate))
                    continue;

                if (policy.MergeDuplicate != null)
                {
                    policy.MergeDuplicate(em, canonical, duplicate);
                }
                else if (em.HasComponent<T>(canonical) && em.HasComponent<T>(duplicate))
                {
                    // F-17 (LIF-05): non-lossy dedup. With no MergeDuplicate
                    // policy, do not silently discard a data-bearing duplicate.
                    // If the canonical is still at default and the duplicate
                    // holds a non-default (restored) value, migrate the value
                    // onto the canonical before destroying — otherwise an
                    // EnsureExists-inside-Deserialize repair can pick the empty
                    // entity as canonical and drop the one that carried the
                    // saved state.
                    var canonValue = em.GetComponentData<T>(canonical);
                    var dupValue = em.GetComponentData<T>(duplicate);
                    if (comparer.Equals(canonValue, defaultValue)
                        && !comparer.Equals(dupValue, defaultValue))
                    {
                        em.SetComponentData(canonical, dupValue);
                    }
                }

                if (destroyDuplicates)
                    em.DestroyEntity(duplicate);
            }

            policy.EnsureShape?.Invoke(em, canonical);
            em.SetName(canonical, typeof(T).Name);
            return canonical;
        }

        public static Entity EnsurePaired<TPrimary, TSecondary>(
            EntityManager em,
            in TPrimary primaryDefault,
            in TSecondary secondaryDefault,
            in EnsurePairedPolicy<TPrimary, TSecondary> policy)
            where TPrimary : unmanaged, IComponentData
            where TSecondary : unmanaged, IComponentData
        {
            using var primaryQuery = em.CreateEntityQuery(ComponentType.ReadWrite<TPrimary>());
            using var secondaryQuery = em.CreateEntityQuery(ComponentType.ReadWrite<TSecondary>());
            using var primaryEntities = primaryQuery.ToEntityArray(Allocator.Temp);
            using var secondaryEntities = secondaryQuery.ToEntityArray(Allocator.Temp);

            var candidates = BuildCandidateUnion(primaryEntities, secondaryEntities);
            var canonical = PickPairedCanonical<TPrimary, TSecondary>(em, candidates, policy.Preferred);
            if (canonical == Entity.Null)
                canonical = em.CreateEntity();

            EnsurePairedPayload(em, canonical, primaryEntities, primaryDefault);
            EnsurePairedPayload(em, canonical, secondaryEntities, secondaryDefault);

            var removeComponentsNotEntity = policy.RemoveComponentsNotEntity.GetValueOrDefault(true);
            foreach (var duplicate in candidates)
            {
                if (duplicate == canonical || !em.Exists(duplicate))
                    continue;

                policy.MergeDuplicate?.Invoke(em, canonical, duplicate);
                if (removeComponentsNotEntity)
                {
                    if (em.HasComponent<TPrimary>(duplicate))
                        em.RemoveComponent<TPrimary>(duplicate);
                    if (em.HasComponent<TSecondary>(duplicate))
                        em.RemoveComponent<TSecondary>(duplicate);
                }
                else
                {
                    em.DestroyEntity(duplicate);
                }
            }

            policy.EnsureShape?.Invoke(em, canonical);
            return canonical;
        }

        private static Entity PickCanonical<T>(
            EntityManager em,
            NativeArray<Entity> entities,
            Entity preferred)
            where T : unmanaged, IComponentData
        {
            if (preferred != Entity.Null && em.Exists(preferred) && em.HasComponent<T>(preferred))
                return preferred;

            for (int i = 0; i < entities.Length; i++)
            {
                if (em.Exists(entities[i]))
                    return entities[i];
            }

            return Entity.Null;
        }

        private static Entity PickPairedCanonical<TPrimary, TSecondary>(
            EntityManager em,
            IReadOnlyList<Entity> candidates,
            Entity preferred)
            where TPrimary : unmanaged, IComponentData
            where TSecondary : unmanaged, IComponentData
        {
            if (preferred != Entity.Null
                && em.Exists(preferred)
                && (em.HasComponent<TPrimary>(preferred) || em.HasComponent<TSecondary>(preferred)))
            {
                return preferred;
            }

            for (int i = 0; i < candidates.Count; i++)
            {
                if (em.Exists(candidates[i]))
                    return candidates[i];
            }

            return Entity.Null;
        }

        private static List<Entity> BuildCandidateUnion(
            NativeArray<Entity> primaryEntities,
            NativeArray<Entity> secondaryEntities)
        {
            var candidates = new List<Entity>(primaryEntities.Length + secondaryEntities.Length);
            AddUnique(candidates, primaryEntities);
            AddUnique(candidates, secondaryEntities);
            return candidates;
        }

        private static void AddUnique(List<Entity> candidates, NativeArray<Entity> entities)
        {
            for (int i = 0; i < entities.Length; i++)
            {
                if (!candidates.Contains(entities[i]))
                    candidates.Add(entities[i]);
            }
        }

        private static void EnsurePairedPayload<T>(
            EntityManager em,
            Entity canonical,
            NativeArray<Entity> sourceEntities,
            in T defaultValue)
            where T : unmanaged, IComponentData
        {
            if (em.HasComponent<T>(canonical))
                return;

            var value = defaultValue;
            for (int i = 0; i < sourceEntities.Length; i++)
            {
                var source = sourceEntities[i];
                if (source != canonical && em.Exists(source) && em.HasComponent<T>(source))
                {
                    value = em.GetComponentData<T>(source);
                    break;
                }
            }

            em.AddComponentData(canonical, value);
        }
    }
}
