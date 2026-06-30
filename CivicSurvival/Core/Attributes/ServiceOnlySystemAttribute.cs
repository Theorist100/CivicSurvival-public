using System;

namespace CivicSurvival.Core.Attributes
{
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class ServiceOnlySystemAttribute : Attribute
    {
        public string Reason { get; }

        public ServiceOnlySystemAttribute(string reason)
        {
            Reason = reason ?? throw new ArgumentNullException(nameof(reason));
        }
    }
}
