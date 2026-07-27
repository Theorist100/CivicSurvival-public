using System;

namespace CivicSurvival.Core.Attributes
{
    /// <summary>
    /// Explicit opt-in to <c>null</c> return for a reference type or
    /// <c>Task</c> return in a <see cref="GenerateNullObjectAttribute"/>-decorated
    /// interface. Use when consumers explicitly handle null and a sentinel
    /// instance would be misleading.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Property)]
    public sealed class NullReturnNullAttribute : Attribute
    {
    }
}
