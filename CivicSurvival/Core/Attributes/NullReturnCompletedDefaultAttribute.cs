using System;

namespace CivicSurvival.Core.Attributes
{
    /// <summary>
    /// Marks a Task-returning method as returning <c>Task.CompletedTask</c>
    /// for non-generic Task or <c>Task.FromResult(default(T))</c> for
    /// <c>Task&lt;T&gt;</c> in the generated null object.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class NullReturnCompletedDefaultAttribute : Attribute
    {
    }
}
