using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Types;
using Unity.Jobs;

namespace CivicSurvival.Core.Interfaces.Domain.Population
{
    [OwnedByFeatureId(FeatureIds.PopulationName)]
    public interface IResidentPopulationReader : IVersionedView<ResidentPopulationSnapshot>
    {
        /// <summary>
        /// Current monotonic readiness level of the producer, read at the view layer
        /// (mirrors vanilla <c>CountHouseholdDataSystem.IsCountDataNotReady()</c> —
        /// readiness is a property of the producer/view, never a field in the snapshot
        /// struct). Scalar consumers gate on <c>Readiness &gt;= ScalarReady</c>.
        /// </summary>
        ResidentPopulationReadiness Readiness { get; }

        /// <summary>
        /// Convenience gate: the scalar snapshot (AliveResidentCitizens + counts) is
        /// published and safe to read. Equivalent to
        /// <c>Readiness &gt;= ResidentPopulationReadiness.ScalarReady</c>.
        /// </summary>
        bool IsScalarReady { get; }

        void AddPopulationDataReader(JobHandle reader);
    }
}
