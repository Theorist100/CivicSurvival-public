using System;

namespace CivicSurvival.Domains.Engineering.Pipelines.PowerCapacity
{
    public enum PowerCapacityPipelinePhase
    {
        None = 0,
        ClassifyAndEnsureModifiers = 1,
        ApplyConstructionDelay = 2,
        ApplyDisasterModifier = 3,
        ApplyWearAndRepair = 4,
        ResolveAndPublish = 5,
        ReconcileAfterLoad = 6
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public sealed class PowerCapacityPipelinePhaseAttribute : Attribute
    {
        public PowerCapacityPipelinePhaseAttribute(PowerCapacityPipelinePhase phase)
        {
            Phase = phase;
        }

        public PowerCapacityPipelinePhase Phase { get; }
    }
}
