using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Interfaces.Domain.Cognitive
{
    /// <summary>
    /// Read-side contract for buckwheat aid eligibility consumed outside the Cognitive domain.
    /// </summary>
    [OwnedByFeatureId(FeatureIds.CognitiveName)]
    public interface IBuckwheatAidReader
    {
        bool CanDistributeToDistrict(int districtIndex, out string reasonId);
    }
}
