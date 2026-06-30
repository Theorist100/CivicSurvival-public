using Unity.Entities;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Components.CrossDomain
{
    /// <summary>
    /// Current narrative act as ECS singleton.
    /// Split from ScenarioSingleton: 16 domains read this field while the rest of
    /// ScenarioSingleton has 0-1 cross-domain readers per field.
    ///
    /// Access: SystemAPI.GetSingleton&lt;CurrentActSingleton&gt;().CurrentAct
    /// Writer: ScenarioStateMachine (sole owner, same entity as ScenarioSingleton).
    /// </summary>
    public struct CurrentActSingleton : IComponentData
    {
        public Act CurrentAct;

        public static CurrentActSingleton Default => new() { CurrentAct = Act.PreWar };
    }
}
