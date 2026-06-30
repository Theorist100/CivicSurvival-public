using System;

namespace CivicSurvival.Core.Attributes
{
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class ReentrantOneShotAttribute : Attribute
    {
        public string Reason { get; }

        public ReentrantOneShotAttribute(string reason)
        {
            Reason = reason ?? throw new ArgumentNullException(nameof(reason));
        }
    }
}
