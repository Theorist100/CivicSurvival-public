using System;

namespace CivicSurvival.Core.Infrastructure
{
    /// <summary>
    /// Marks a class as a façade over a world-owned ECS host-system. Façade lives
    /// process-lifetime in <see cref="ServiceRegistry"/> and holds exactly one
    /// <c>internal nullable</c> field referring to a
    /// <see cref="Unity.Entities.ComponentSystemBase"/> subtype (the host). The
    /// host writes itself to that field in OnCreate and clears it in OnDestroy.
    /// Multi-host façades require <c>// facade: multi-host</c> escape comment on
    /// extra host fields.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class FacadeAttribute : Attribute
    {
    }
}
