using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Systems.Base;

namespace CivicSurvival.Domains.Scenario.Systems
{
    [ActIndependent]
    public partial class IntroLoadRestoreUISystem : CivicSystemBase
    {
        protected override void OnUpdateImpl()
        {
            World.GetExistingSystemManaged<IntroScenarioSystem>()?.ApplyPauseSafeLoadSideEffects();
        }
    }
}
