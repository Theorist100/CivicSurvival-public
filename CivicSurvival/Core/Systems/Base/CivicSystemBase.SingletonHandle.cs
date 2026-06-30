using System;
using CivicSurvival.Core.Components.Lifecycle;
using Unity.Entities;

namespace CivicSurvival.Core.Systems.Base
{
    public abstract partial class CivicSystemBase
    {
        protected CivicSingletonHandle<T> CreateSingletonHandle<T>()
            where T : unmanaged, IComponentData
        {
            return CreateSingletonHandle<T>(GetEntityQuery(ComponentType.ReadWrite<T>()));
        }

        protected CivicSingletonHandle<T> CreateSingletonHandle<T>(EntityQuery query)
            where T : unmanaged, IComponentData
        {
            return new CivicSingletonHandle<T>(query);
        }

        protected Entity EnsureSingleton<T>(
            ref CivicSingletonHandle<T> handle,
            T defaultValue,
            Action<EntityManager, Entity>? ensureShape = null)
            where T : unmanaged, IComponentData
        {
            if (!handle.IsCreated)
                handle = CreateSingletonHandle<T>();

            var policy = new EnsureSingletonPolicy<T>
            {
                Preferred = handle.Entity,
                EnsureShape = ensureShape
            };

            var entity = CivicSingleton.Ensure(EntityManager, handle.Query, defaultValue, policy);
            handle.Set(entity);
            return entity;
        }

        /// <summary>
        /// EntityManager-based overload for callers that run OUTSIDE a valid
        /// SystemAPI context — ICivicSingletonOwner.OnLoadRestore, EventBus
        /// handlers, Deserialize. Same Inv-2 contract (liveness-validate →
        /// query-first → dedup → create-if-absent + shape), driven entirely by
        /// the passed <paramref name="em"/> so it never touches SystemAPI
        /// (CIVIC292/185-safe).
        /// </summary>
        protected Entity EnsureSingleton<T>(
            ref CivicSingletonHandle<T> handle,
            EntityManager em,
            T defaultValue,
            Action<EntityManager, Entity>? ensureShape = null)
            where T : unmanaged, IComponentData
        {
            if (!handle.IsCreated)
                handle = CreateSingletonHandle<T>();

            var policy = new EnsureSingletonPolicy<T>
            {
                Preferred = handle.Entity,
                EnsureShape = ensureShape
            };

            var entity = CivicSingleton.Ensure(em, handle.Query, defaultValue, policy);
            handle.Set(entity);
            return entity;
        }

        protected Entity EnsureSingletonFast<T>(
            ref CivicSingletonHandle<T> handle,
            T defaultValue,
            Action<EntityManager, Entity>? ensureShape = null)
            where T : unmanaged, IComponentData
        {
            if (!handle.IsCreated)
                handle = CreateSingletonHandle<T>();

            var cached = handle.Entity;
            if (cached != Entity.Null
                && EntityManager.Exists(cached)
                && EntityManager.HasComponent<T>(cached))
                return cached;

            return EnsureSingleton(ref handle, defaultValue, ensureShape);
        }

        protected Entity ResolveSingletonReadOnly<T>(ref CivicSingletonHandle<T> handle)
            where T : unmanaged, IComponentData
        {
            if (!handle.IsCreated)
                handle = CreateSingletonHandle<T>(GetEntityQuery(ComponentType.ReadOnly<T>()));

            var cached = handle.Entity;
            if (cached != Entity.Null
                && EntityManager.Exists(cached)
                && EntityManager.HasComponent<T>(cached))
                return cached;

            if (handle.Query.TryGetSingletonEntity<T>(out var entity))
            {
                handle.Set(entity);
                return entity;
            }

            handle.Invalidate();
            return Entity.Null;
        }
    }
}
