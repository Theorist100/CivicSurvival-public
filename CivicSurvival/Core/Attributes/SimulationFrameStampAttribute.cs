using System;

namespace CivicSurvival.Core.Attributes
{
    /// <summary>
    /// Marks an integer field as a simulation frame stamp stored in data.
    /// Consumers may compare it as frame metadata, but it is not a snapshot
    /// payload version or cache invalidation counter.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class SimulationFrameStampAttribute : Attribute
    {
        public SimulationFrameStampAttribute(string reason)
        {
            Reason = reason ?? string.Empty;
        }

        public string Reason { get; }
    }
}
