using Unity.Entities;

namespace CivicSurvival.Core.Components.Lifecycle
{
    /// <summary>
    /// Cached entity handle for an ECS singleton component.
    /// Resolution and creation are centralized in CivicSystemBase so systems do
    /// not keep raw Entity fields that survive save/load as stale references.
    /// </summary>
#pragma warning disable S2326 // T anchors the handle/query contract enforced by CivicSystemBase.
    public struct CivicSingletonHandle<T>
        where T : unmanaged, IComponentData
    {
        private readonly EntityQuery m_Query;
        private readonly bool m_IsCreated;
        private Entity m_Entity;

        internal CivicSingletonHandle(EntityQuery query)
        {
            m_Query = query;
            m_IsCreated = true;
            m_Entity = Entity.Null;
        }

        public Entity Entity => m_Entity;
        internal EntityQuery Query => m_Query;
        internal bool IsCreated => m_IsCreated;

        public void Invalidate()
        {
            m_Entity = Entity.Null;
        }

        internal void Set(Entity entity)
        {
            m_Entity = entity;
        }
    }
#pragma warning restore S2326
}
