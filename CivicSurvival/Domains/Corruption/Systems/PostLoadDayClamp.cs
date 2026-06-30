using CivicSurvival.Core.Systems;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Domains.Corruption.Systems
{
    internal static class PostLoadDayClamp
    {
        public static DayChangedDedup ClampDedupToActivatedGameDay(DayChangedDedup dedup, LogContext log, string owner)
        {
            if (!GameTimeSystem.TryGetDay(out int currentDay))
            {
                log.Warn($"GameTimeSystem unavailable during {owner} ValidateAfterLoad; preserving saved lastProcessedDay");
                return dedup;
            }

            int normalized = System.Math.Max(0, dedup.LastProcessedDay);
            int clamped = System.Math.Min(normalized, System.Math.Max(0, currentDay));
            return DayChangedDedup.FromSave(clamped);
        }
    }
}
