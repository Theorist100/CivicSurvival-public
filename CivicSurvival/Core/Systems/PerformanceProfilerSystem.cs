using Game;
using Unity.Entities;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Utils;
using CivicSurvival.Core.Attributes;

namespace CivicSurvival.Core.Systems
{
    /// <summary>
    /// Triggers performance profiler reports at end of simulation frame.
    /// Separated from other systems to avoid polluting their timing measurements.
    /// </summary>
    [ActIndependent]
    public partial class PerformanceProfilerSystem : CivicSystemBase
    {
        protected override void OnCreate()
        {
            base.OnCreate();
            // EntityCountProbe queries are owned by EntityCountProbeHost (per-World ECS lifecycle).
        }

        protected override void OnUpdateImpl()
        {
            PerformanceProfiler.OnSimTick();
        }
    }
}
