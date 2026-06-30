using System;

namespace CivicSurvival.Core.UI.DomainState.Attributes
{
    /// <summary>
    /// Marks a DTO eligibility field as backed by a C# predicate used by the processor path.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class DtoEligibilityAttribute : Attribute
    {
        public Type PredicateType { get; }
        public string PredicateMethod { get; }
        public string ReasonFieldName { get; }

        public DtoEligibilityAttribute(Type predicateType, string predicateMethod, string reasonFieldName = "")
        {
            PredicateType = predicateType;
            PredicateMethod = predicateMethod;
            ReasonFieldName = reasonFieldName ?? string.Empty;
        }
    }
}
