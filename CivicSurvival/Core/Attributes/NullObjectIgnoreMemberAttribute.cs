using System;

namespace CivicSurvival.Core.Attributes
{
    /// <summary>
    /// Excludes a single member from null-object generation. The generator
    /// emits an abstract method declaration instead of a body, so the
    /// resulting class becomes abstract and must be subclassed manually.
    ///
    /// Use only for members the generator cannot handle (e.g. an out/ref
    /// parameter on one method while the rest of the interface is clean).
    /// Most interfaces should refactor the offending signature instead.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Property)]
    public sealed class NullObjectIgnoreMemberAttribute : Attribute
    {
    }
}
