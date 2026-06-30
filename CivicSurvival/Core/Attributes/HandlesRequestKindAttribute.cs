using System;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Attributes
{
    /// <summary>
    /// Declares which RequestKind value(s) a processor handles.
    /// CIVIC4ZJ enforces bidirectional binding between this attribute and the RequestKind enum.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public sealed class HandlesRequestKindAttribute : Attribute
    {
        public RequestKind Kind { get; }

        public HandlesRequestKindAttribute(RequestKind kind) => Kind = kind;
    }
}
