using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Interfaces.Domain.Population
{
    /// <summary>
    /// Mandatory AlwaysOpen resident-household read model — the mod's only view over
    /// which households hold living residents and how many. Consumers that need it must
    /// resolve it with ServiceRegistry.Require; there is intentionally no null object.
    /// A consumer that wants the CITY's population instead wants the vanilla record, via
    /// <c>ICityPopulationReader</c>.
    /// </summary>
    [OwnedByFeatureId(FeatureIds.PopulationName)]
    public interface IResidentHouseholdView : IVersionedView<ResidentHouseholdSnapshot>
    {
        /// <summary>
        /// The selection result (EligibleHouseholds + LiveCitizensPerHousehold) has
        /// been rebuilt and published. Selection consumers gate on this so an empty
        /// eligibility set is never misread as "no eligible households". Backed by the
        /// producer's readiness latch, which reaches SelectionReady when the rebuild job
        /// completes and never before.
        /// </summary>
        bool IsSelectionReady { get; }

        void AckPendingDays(int dayCount);
    }
}
