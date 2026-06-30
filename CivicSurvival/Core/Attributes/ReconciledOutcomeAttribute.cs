using System;

namespace CivicSurvival.Core.Attributes
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
    public sealed class ReconciledOutcomeAttribute : Attribute
    {
        public ReconciledOutcomeAttribute(string logicalName, Type durableSidecar)
        {
            LogicalName = logicalName;
            DurableSidecar = durableSidecar;
        }

        public string LogicalName { get; }

        public Type DurableSidecar { get; }
    }
}
