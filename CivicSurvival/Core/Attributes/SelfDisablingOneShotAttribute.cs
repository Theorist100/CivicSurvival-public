using System;

namespace CivicSurvival.Core.Attributes
{
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class SelfDisablingOneShotAttribute : Attribute
    {
        public string Reason { get; }

        public SelfDisablingOneShotAttribute(string reason)
        {
            Reason = reason ?? throw new ArgumentNullException(nameof(reason));
        }
    }
}
