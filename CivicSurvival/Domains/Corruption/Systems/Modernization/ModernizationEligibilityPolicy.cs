using Game;
using Game.Areas;
using Game.Buildings;
using Game.Common;
using Game.Simulation;
using Unity.Collections;
using Unity.Entities;
using CivicSurvival.Core.Interfaces.Domain.Power;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.UI.DomainState;

namespace CivicSurvival.Domains.Corruption.Systems.Modernization
{
    /// <summary>
    /// Pure-function eligibility/cost policy for District Modernization.
    /// Does not mutate cleanup scratch; the caller composes the "replacing corrupt"
    /// add-on count from <see cref="CounterfeitCleanupService"/>.
    /// </summary>
    internal sealed class ModernizationEligibilityPolicy
    {
        private readonly ModernizationProgramStore m_Store;

        public ModernizationEligibilityPolicy(ModernizationProgramStore store)
        {
            m_Store = store;
        }

        /// <summary>Stateless — no-op. Present so the system's ResetState path can
        /// reference every sub-service uniformly (CIVIC314 coverage).</summary>
        public void Reset()
        {
            // No mutable state — intentionally empty.
        }

        /// <summary>
        /// Returns the unprotected building count in the district plus whether the
        /// district currently runs a corrupt program (caller decides whether to
        /// add the replaced-corrupt count via cleanup PreScan/Count).
        /// </summary>
        public (int UnprotectedCount, bool ReplacingCorrupt) Count(
            int districtIndex,
            EntityQuery buildingsWithDistrictQuery,
            ComponentLookup<CurrentDistrict> currentDistrictLookup,
            IBackupPowerLinkReader backupLinks)
        {
            bool replacingCorrupt = m_Store.TryGetProgram(districtIndex, out var oldProgram)
                && oldProgram.HasProgram
                && oldProgram.Contractor == ContractorType.YourGuy;
            int unprotected = CountUnprotectedBuildings(
                districtIndex, buildingsWithDistrictQuery, currentDistrictLookup, backupLinks);
            return (unprotected, replacingCorrupt);
        }

        /// <summary>
        /// Builds the eligibility verdict from a precomputed target count + cost.
        /// </summary>
        public EligibilityFlag GetEligibility(
            ContractorType contractor,
            bool hasPendingProcurement,
            int daysUntilNextProcurement,
            int targetBuildingCount,
            long totalCost,
            World world)
        {
            return ModernizationEligibility.ForModernization(
                hasPendingProcurement,
                daysUntilNextProcurement,
                contractor,
                targetBuildingCount,
                totalCost,
                world);
        }

        /// <summary>
        /// Count buildings in the district that currently lack a backup power link
        /// (no BackupPowerRef component, or the linked mod entity is Entity.Null).
        /// </summary>
        public int CountUnprotectedBuildings(
            int districtIndex,
            EntityQuery buildingsWithDistrictQuery,
            ComponentLookup<CurrentDistrict> currentDistrictLookup,
            IBackupPowerLinkReader backupLinks)
        {
            int count = 0;
            var entities = buildingsWithDistrictQuery.ToEntityArray(Allocator.Temp);
            try
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    Entity entity = entities[i];
                    if (!currentDistrictLookup.TryGetComponent(entity, out var district))
                        continue;
                    if (district.m_District.Index != districtIndex)
                        continue;
                    if (!backupLinks.TryGet(BuildingRef.FromEntity(entity), out _))
                        count++;
                }
            }
            finally
            {
                if (entities.IsCreated) entities.Dispose();
            }
            return count;
        }
    }
}
