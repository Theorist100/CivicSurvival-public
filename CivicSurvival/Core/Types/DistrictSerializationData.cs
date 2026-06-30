using System.Collections.Generic;
using CivicSurvival.Core.Systems;

using CivicSurvival.Core.Features.Wellbeing;
namespace CivicSurvival.Core.Types
{
    /// <summary>
    /// DTO for serialization of district state.
    /// Extracted from ThreadSafeDistrictState to reduce file size.
    /// All collections are IReadOnly* to enforce immutability after construction.
    /// </summary>
    public readonly struct DistrictSerializationData
    {
        public static DistrictSerializationData Empty => new(
            new Dictionary<DistrictRef, HashSet<BuildingCategory>>(),
            new Dictionary<DistrictRef, DistrictOverride>(),
            new HashSet<DistrictRef>(),
            new HashSet<DistrictRef>(),
            new Dictionary<DistrictRef, DistrictPenalties>(),
            new Dictionary<DistrictRef, PreShedState>(),
            SchedulePreset.Manual);

        public readonly IReadOnlyDictionary<DistrictRef, HashSet<BuildingCategory>> Blackouts;
        public readonly IReadOnlyDictionary<DistrictRef, DistrictOverride> DistrictOverrides;
        public readonly IReadOnlyCollection<DistrictRef> Vips;
        public readonly IReadOnlyCollection<DistrictRef> VipBypass;
        public readonly IReadOnlyDictionary<DistrictRef, DistrictPenalties> Penalties;
        public readonly IReadOnlyDictionary<DistrictRef, PreShedState> PreShedStates;
        public readonly IReadOnlyDictionary<DistrictRef, int> Priorities;
        public readonly SchedulePreset CitySchedule;

        public DistrictSerializationData(
            Dictionary<DistrictRef, HashSet<BuildingCategory>> blackouts,
            Dictionary<DistrictRef, DistrictOverride> districtOverrides,
            HashSet<DistrictRef> vips,
            HashSet<DistrictRef> vipBypass,
            Dictionary<DistrictRef, DistrictPenalties> penalties,
            Dictionary<DistrictRef, PreShedState> preShedStates,
            SchedulePreset citySchedule,
            Dictionary<DistrictRef, int> priorities = null!)
        {
            // Deep-copy all mutable collections to ensure true immutability after construction.
            var blackoutsCopy = new Dictionary<DistrictRef, HashSet<BuildingCategory>>(blackouts.Count);
            foreach (var kvp in blackouts)
            {
                blackoutsCopy[kvp.Key] = new HashSet<BuildingCategory>(kvp.Value);
            }
            Blackouts = blackoutsCopy;
            DistrictOverrides = new Dictionary<DistrictRef, DistrictOverride>(districtOverrides);
            Vips = new HashSet<DistrictRef>(vips);
            VipBypass = new HashSet<DistrictRef>(vipBypass);
            Penalties = new Dictionary<DistrictRef, DistrictPenalties>(penalties);

            // Deep-copy PreShedStates (CategoriesOff is mutable)
            var preShedCopy = new Dictionary<DistrictRef, PreShedState>(preShedStates.Count);
            foreach (var kvp in preShedStates)
            {
                preShedCopy[kvp.Key] = new PreShedState(
                    kvp.Value.Schedule,
                    new HashSet<BuildingCategory>(kvp.Value.CategoriesOff),
                    kvp.Value.WasVip,
                    kvp.Value.HadExplicitSchedule,
                    kvp.Value.WasVipBypass);
            }
            PreShedStates = preShedCopy;
            CitySchedule = citySchedule;
            Priorities = priorities != null ? new Dictionary<DistrictRef, int>(priorities) : new Dictionary<DistrictRef, int>();
        }
    }
}
