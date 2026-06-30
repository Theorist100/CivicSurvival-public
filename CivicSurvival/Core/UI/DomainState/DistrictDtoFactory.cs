using System.Collections.Generic;
using System.Text;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;
using Unity.Mathematics;

namespace CivicSurvival.Core.UI.DomainState
{
    /// <summary>
    /// Factory for building <see cref="DistrictDto"/> wire payloads from
    /// the district state snapshot, and helper for serializing a list of
    /// them as one JSON array via the generated WriteTo pipeline.
    /// Moved out of <c>Core.Utils.DistrictJsonSerializer</c> in C4b2.
    /// </summary>
    public static class DistrictDtoFactory
    {
        private const int KW_TO_MW_HALF = 500;
        private const int KW_PER_MW = 1000;
        // Rough upper bound for serialized 28-field DistrictDto list across ~10 districts.
        private const int SERIALIZE_BUFFER_INITIAL_CAPACITY = 4096;

        private static int ComputeDeliveredMW(int districtActiveKW, int districtThresholdCutKW, float cityDeliveryRatio)
        {
            // Base is the district's ACTIVE load (schedule / category blackout already
            // removed), not its raw wanted load — a blacked-out district draws 0 and must
            // show DEL = 0, matching the city power-balance panel.
            if (districtActiveKW <= 0) return 0;
            int vanillaDeliveredKW = (int)System.Math.Round(districtActiveKW * math.saturate(cityDeliveryRatio));
            int actualDeliveredKW = vanillaDeliveredKW - districtThresholdCutKW;
            if (actualDeliveredKW < 0) actualDeliveredKW = 0;
            return (actualDeliveredKW + KW_TO_MW_HALF) / KW_PER_MW;
        }

        /// <summary>
        /// Serialize the district list to a JSON array string for the Districts
        /// value binding. Each entry goes through the generated DistrictDto.WriteTo.
        /// </summary>
        public static string SerializeList(List<DistrictDto> districts)
        {
            if (districts == null || districts.Count == 0) return "[]";
            var sb = new StringBuilder(SERIALIZE_BUFFER_INITIAL_CAPACITY);
            sb.Append('[');
            for (int i = 0; i < districts.Count; i++)
            {
                if (i > 0) sb.Append(',');
                districts[i].WriteTo(sb);
            }
            sb.Append(']');
            return sb.ToString();
        }

        /// <summary>
        /// Build a DistrictDto from the immutable district state snapshot.
        /// All blackout / schedule / penalty reads come from the snapshot so
        /// the per-frame view is internally consistent; threshold/internet
        /// state is passed in as lookups because they live in other systems.
        /// </summary>
        public static DistrictDto CreateFromSnapshot(
            in DistrictStateSnapshot snapshot,
            int entityIndex,
            int entityVersion,
            string name,
            int priority,
            DistrictPowerData? powerData = null,
            HashSet<int>? internetDisabledDistricts = null,
            System.Func<int, int, int>? getThresholdCut = null,
            System.Func<int, int, int>? getThresholdCutKW = null,
            float cityDeliveryRatio = 1f)
        {
            var data = powerData ?? new DistrictPowerData();

            bool residentialOff = false;
            bool commercialOff = false;
            bool industrialOff = false;
            bool officeOff = false;
            bool servicesOff = false;

            if (snapshot.DistrictBlackouts.TryGetValue(entityIndex, out var categories))
            {
                residentialOff = categories.Contains(BuildingCategory.Residential);
                commercialOff = categories.Contains(BuildingCategory.Commercial);
                industrialOff = categories.Contains(BuildingCategory.Industrial);
                officeOff = categories.Contains(BuildingCategory.Office);
                servicesOff = categories.Contains(BuildingCategory.Services);
            }

            SchedulePreset schedule = SchedulePreset.Manual;
            if (snapshot.DistrictSchedules.TryGetValue(entityIndex, out var districtSchedule))
            {
                schedule = districtSchedule;
            }
            else if (snapshot.CitySchedule != SchedulePreset.Manual)
            {
                schedule = snapshot.CitySchedule;
            }

            var penalties = snapshot.GetPenalties(entityIndex);

            int thresholdCutKW = getThresholdCutKW != null ? getThresholdCutKW.Invoke(entityIndex, entityVersion) : 0;

            // Active load after schedule / category blackout gating — single source of
            // truth shared with PowerGridDataSystem.CalculateActiveConsumption.
            int activeKW = DistrictActiveLoad.ComputeActiveKW(in snapshot, entityIndex, in data);

            return new DistrictDto
            {
                EntityIndex = entityIndex,
                EntityVersion = entityVersion,
                Name = name ?? $"District {entityIndex}",
                IsUnzoned = entityIndex == 0,
                ResidentialOff = residentialOff,
                CommercialOff = commercialOff,
                IndustrialOff = industrialOff,
                OfficeOff = officeOff,
                ServicesOff = servicesOff,
                Schedule = (int)schedule,
                ScheduleName = SchedulePresets.GetDisplayName(schedule),
                ScheduleActive = snapshot.IsBlackoutActiveForSchedule(entityIndex),
                TotalMW = data.TotalMW,
                ResidentialMW = data.ResidentialMW,
                CommercialMW = data.CommercialMW,
                IndustrialMW = data.IndustrialMW,
                OfficeMW = data.OfficeMW,
                ServicesMW = data.ServicesMW,
                Priority = priority,
                DeliveredMW = ComputeDeliveredMW(activeKW, thresholdCutKW, cityDeliveryRatio),
                ThresholdCutMW = thresholdCutKW / KW_PER_MW,
                IsVIP = snapshot.IsVIP(entityIndex),
                IsVIPBypass = snapshot.HasVIPBypass(entityIndex),
                IsAutoShedded = snapshot.IsAutoShedded(entityIndex),
                InternetDisabled = internetDisabledDistricts != null && internetDisabledDistricts.Contains(entityIndex),
                ThresholdCutBuildings = getThresholdCut != null ? getThresholdCut.Invoke(entityIndex, entityVersion) : 0,
                TotalHappinessPenalty = penalties.TotalHappinessPenalty,
                TotalCommercePenalty = penalties.TotalCommercePenalty,
                BlackoutSource = snapshot.GetBlackoutSource(entityIndex)
            };
        }
    }
}
