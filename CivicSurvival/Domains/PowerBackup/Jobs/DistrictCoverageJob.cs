using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Game.Areas;
using CivicSurvival.Core.Components.Domain.Power;

namespace CivicSurvival.Domains.PowerBackup.Jobs
{
    /// <summary>
    /// Burst job for per-district battery coverage aggregation.
    /// IJobEntity — reads BackupPower and falls back to Private when older entities
    /// have not yet received BatteryLayerTag.
    /// Runs with .Schedule() (single-threaded Burst on worker) as part of async chain:
    /// BackupPowerJob → BackupPowerStatsJob → DistrictCoverageJob
    /// </summary>
#if ENABLE_BURST
    [BurstCompile]
#endif
    [WithNone(typeof(Game.Common.Deleted))]
    public partial struct DistrictCoverageJob : IJobEntity
    {
        [ReadOnly] public ComponentLookup<CurrentDistrict> DistrictLookup;
        [ReadOnly] public ComponentLookup<BatteryLayerTag> LayerTagLookup;

        [NativeDisableParallelForRestriction]
        public NativeHashMap<int, DistrictBatteryCoverage> CoverageMap;
        public NativeReference<int> HospitalsPowered;
        public NativeReference<int> HospitalsTotal;
        public NativeReference<int> SchoolsPowered;
        public NativeReference<int> SchoolsTotal;

        public void Execute(Entity entity, in BackupPower backup)
        {
            if (backup.Type == BackupPowerType.None)
                return;

            var layer = LayerTagLookup.TryGetComponent(entity, out var layerTag)
                ? layerTag.Layer
                : BatteryLayer.Private;
            bool hasPower = backup.Type == BackupPowerType.DieselGenerator
                ? backup.FuelHours > 0
                : backup.CurrentChargeWh > 0;

            // City-wide layer counts
            switch (layer)
            {
                case BatteryLayer.Hospital:
                    HospitalsTotal.Value++;
                    if (hasPower) HospitalsPowered.Value++;
                    break;
                case BatteryLayer.School:
                    SchoolsTotal.Value++;
                    if (hasPower) SchoolsPowered.Value++;
                    break;
                default:
                    break;
            }

            // Get district from vanilla building. No-District is a first-class
            // unzoned bucket keyed exactly like blackout/UI state.
            var buildingEntity = backup.GetBuildingEntity();
            int districtIndex = !DistrictLookup.TryGetComponent(buildingEntity, out var district)
                || district.m_District == Entity.Null
                    ? Engine.Districts.NO_DISTRICT_INDEX
                    : district.m_District.Index;

#pragma warning disable CIVIC097 // District index is a logical spatial ID, not entity tracking
            if (!CoverageMap.TryGetValue(districtIndex, out var cov))
                cov = new DistrictBatteryCoverage { DistrictIndex = districtIndex };

            switch (layer)
            {
                case BatteryLayer.Hospital:
                    cov.HospitalsTotal++;
                    if (hasPower) cov.HospitalsPowered++;
                    break;
                case BatteryLayer.School:
                    cov.SchoolsTotal++;
                    if (hasPower) cov.SchoolsPowered++;
                    break;
                case BatteryLayer.Private:
                    cov.PrivateTotal++;
                    if (hasPower) cov.PrivatePowered++;
                    break;
                default:
                    break;
            }

            CoverageMap[districtIndex] = cov;
#pragma warning restore CIVIC097
        }
    }
}
