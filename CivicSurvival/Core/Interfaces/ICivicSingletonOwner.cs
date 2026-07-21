using Unity.Entities;
using System;

namespace CivicSurvival.Core.Interfaces
{
    [AttributeUsage(AttributeTargets.Struct)]
    public sealed class CivicSingletonAttribute : Attribute
    {
    }

    /// <summary>
    /// Non-generic marker used by the post-load bridge to restore owned singletons.
    /// </summary>
    public interface ICivicSingletonOwner
    {
        void OnLoadRestore(EntityManager entityManager);
    }

    /// <summary>
    /// Implemented by systems that own and recreate a singleton component after load/default reset.
    /// </summary>
#pragma warning disable S2326 // T binds the owner contract to a specific singleton type for analyzers and reviews.
    public interface ICivicSingletonOwner<T> : ICivicSingletonOwner
        where T : unmanaged, IComponentData
    {
    }
#pragma warning restore S2326
}
