namespace CivicSurvival.Core.Types
{
    /// <summary>
    /// Explicit per-district power override. SchedulePreset.Manual means
    /// "always on for this district", not "inherit city schedule".
    /// </summary>
    public readonly struct DistrictOverride
    {
        public readonly SchedulePreset Schedule;

        public bool IsAlwaysOn => Schedule == SchedulePreset.Manual;

        private DistrictOverride(SchedulePreset schedule)
        {
            Schedule = schedule;
        }

        public static DistrictOverride AlwaysOn => new(SchedulePreset.Manual);

        public static DistrictOverride Scheduled(SchedulePreset schedule) => new(schedule);
    }
}
