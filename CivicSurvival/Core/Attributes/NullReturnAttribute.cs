using System;

namespace CivicSurvival.Core.Attributes
{
    /// <summary>
    /// Overrides the null-object return value for a method or property getter
    /// in a <see cref="GenerateNullObjectAttribute"/>-decorated interface.
    ///
    /// Value must be a compile-time constant assignable to the return type.
    /// For collections prefer <see cref="NullReturnEmptyAttribute"/>.
    /// For nullable reference returns prefer <see cref="NullReturnNullAttribute"/>.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Property)]
    public sealed class NullReturnAttribute : Attribute
    {
        public NullReturnAttribute(object value) => Value = value;
        public object Value { get; }
    }
}
