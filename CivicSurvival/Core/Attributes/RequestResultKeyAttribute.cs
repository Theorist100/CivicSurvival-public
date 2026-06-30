using System;

namespace CivicSurvival.Core.Attributes
{
    /// <summary>
    /// Declares the UI result key generated for a RequestKind value.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class RequestResultKeyAttribute : Attribute
    {
        public string Key { get; }

        public RequestResultKeyAttribute(string key) => Key = key;
    }
}
