using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Interfaces.Domain.Scenario
{
    [OwnedByFeatureId(FeatureIds.ScenarioName)]
    public interface IScenarioModalReader
    {
        bool IsDefeatDismissed { get; }
    }

    [OwnedByFeatureId(FeatureIds.ScenarioName)]
    public interface IScenarioModalMutator
    {
        void MarkDefeatDismissed();
        void ClearDefeatDismissed();
    }
}
