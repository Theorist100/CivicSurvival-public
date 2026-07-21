using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Types;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

namespace CivicSurvival.Core.Interfaces.Domain.Population
{
    [OwnedByFeatureId(FeatureIds.PopulationName)]
    public interface IResidentPopulationEligibilityView
    {
        /// <summary>
        /// The eligibility selection (<see cref="EligibleHouseholds"/>) has been rebuilt
        /// and published by the producer. Consumers gate on this so an empty eligibility
        /// set is never misread as "no eligible households" — an empty set after the
        /// selection is ready is a valid empty city, an empty set before it is ready is
        /// cold data. Backed by the producer's monotonic readiness level
        /// (<c>Readiness &gt;= SelectionReady</c>).
        /// </summary>
        bool IsSelectionReady { get; }

        NativeParallelHashSet<Entity>.ReadOnly EligibleHouseholds { get; }

        JobHandle GetReadJobHandle();

        void AddEligibilityReader(JobHandle reader);
    }
}
