using System;

namespace CivicSurvival.Core.Attributes
{
    /// <summary>
    /// Marks a collection field keyed by int as intentionally NOT storing Entity.Index.
    /// Suppresses CIVIC014 for fields that store district indices, building indices, etc.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class NonEntityIndexAttribute : Attribute { }
}
