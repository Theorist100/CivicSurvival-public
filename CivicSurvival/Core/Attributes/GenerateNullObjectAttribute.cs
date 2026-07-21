using System;

namespace CivicSurvival.Core.Attributes
{
    /// <summary>
    /// Marks a service interface for automatic null-object class generation.
    /// The generator (CivicSurvival.Analyzers/NullObjectGenerator) emits an
    /// <c>internal sealed class Null{Name}</c> implementation alongside the
    /// interface, with a static <c>Instance</c> field for canonical access.
    ///
    /// Generator rules:
    /// - void methods / property setters / event add/remove / Dispose → empty body
    /// - value types → default(T) returns
    /// - string returns → require [NullReturn("...")] or [NullReturnNull] (CIVIC401)
    /// - collection returns → require [NullReturnEmpty] or [NullReturnNull] (CIVIC402)
    /// - reference type returns → require [NullReturn(...)] (CIVIC403)
    /// - Task returns → require [NullReturnCompletedDefault] or [NullReturnNull] (CIVIC404)
    /// - out/ref parameters → not supported (CIVIC405)
    /// - interface name must follow I{Name} convention (CIVIC407)
    ///
    /// Consumer access: <c>Null{Name}.Instance</c>.
    /// Use via <see cref="CivicSurvival.Core.Infrastructure.ServiceRegistryFeatureExtensions.TryGetOrNullObject{T}"/>.
    /// </summary>
    [AttributeUsage(AttributeTargets.Interface)]
    public sealed class GenerateNullObjectAttribute : Attribute
    {
    }
}
