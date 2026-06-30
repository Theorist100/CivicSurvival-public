using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Interfaces.Domain.Mobilization
{
    /// <summary>
    /// Read-side manpower contract for cross-domain placement validation.
    /// Implemented by MobilizationSystem so consumers do not read the
    /// Mobilization-owned singleton directly.
    /// </summary>
    [OwnedByFeatureId(FeatureIds.MobilizationName)]
    public interface IMobilizationManpowerReader
    {
        int AvailableManpower { get; }

        bool CanRecruit(int amount);
    }
}
