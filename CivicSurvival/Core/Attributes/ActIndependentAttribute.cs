using System;

namespace CivicSurvival.Core.Attributes
{
    /// <summary>
    /// Declares that a system does not need act-awareness (ActChangedEvent handler).
    /// Enforced by CIVIC260 analyzer: concrete systems without this attribute
    /// and without an ActChangedEvent reference are flagged.
    ///
    /// Inherited = false: each concrete class must make its own declaration.
    /// An abstract base being act-independent does not mean its subclasses are.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class ActIndependentAttribute : Attribute { }
}
