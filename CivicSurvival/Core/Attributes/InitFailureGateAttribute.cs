using System;

namespace CivicSurvival.Core.Attributes
{
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class InitFailureGateAttribute : Attribute
    {
        public string Reason { get; }

        public InitFailureGateAttribute(string reason)
        {
            Reason = reason ?? throw new ArgumentNullException(nameof(reason));
        }
    }
}
