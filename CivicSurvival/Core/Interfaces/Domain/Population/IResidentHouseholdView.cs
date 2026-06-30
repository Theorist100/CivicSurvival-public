using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Interfaces.Domain.Population
{
    /// <summary>
    /// Mandatory AlwaysOpen resident-household read model. Consumers that need
    /// resident household or alive resident counts must resolve this with
    /// ServiceRegistry.Require; there is intentionally no null object.
    /// </summary>
    [OwnedByFeatureId(FeatureIds.PopulationName)]
    public interface IResidentHouseholdView : IVersionedView<ResidentHouseholdSnapshot>
    {
        /// <summary>
        /// The selection result (EligibleHouseholds + LiveCitizensPerHousehold) has
        /// been rebuilt and published. Selection consumers gate on this so an empty
        /// eligibility set is never misread as "no eligible households". Backed by the
        /// producer's monotonic readiness level (<c>Readiness &gt;= SelectionReady</c>).
        /// </summary>
        bool IsSelectionReady { get; }

        void AckPendingDays(int dayCount);
    }
}
