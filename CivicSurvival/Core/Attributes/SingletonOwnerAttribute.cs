using System;

namespace CivicSurvival.Core.Attributes
{
    /// <summary>
    /// Declares that a system is the sole owner (writer) of a singleton component.
    /// Enforced by CIVIC175 analyzer: non-owner writes become errors.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public sealed class SingletonOwnerAttribute : Attribute
    {
        /// <summary>The singleton component type this system owns.</summary>
        public Type SingletonType { get; }

        public SingletonOwnerAttribute(Type singletonType) => SingletonType = singletonType;
    }
}
