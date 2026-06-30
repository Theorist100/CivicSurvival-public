#if DEBUG
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Services.DevTools
{
    internal enum ScenarioLogPhase
    {
        Unknown = 0,
        Before = 1,
        After = 2,
        Reaction = 3,
        Validate = 4
    }

    internal enum ScenarioLogDomain
    {
        Unknown = 0,
        Preset = 1,
        Wave = 2,
        AirDefense = 3,
        Grid = 4,
        Blackout = 5,
        Shock = 6,
        Exodus = 7,
        Cognitive = 8,
        Corruption = 9,
        Act = 10,
        Defeat = 11
    }

    internal readonly struct ScenarioLogSnapshot
    {
        public readonly Act Act;
        public readonly float ShockLevel;
        public readonly float StressHours;
        public readonly float CityIntegrity;
        public readonly bool ExodusActive;
        public readonly int Population;

        public ScenarioLogSnapshot(
            Act act,
            float shockLevel,
            float stressHours,
            float cityIntegrity,
            bool exodusActive,
            int population)
        {
            Act = act;
            ShockLevel = shockLevel;
            StressHours = stressHours;
            CityIntegrity = cityIntegrity;
            ExodusActive = exodusActive;
            Population = population;
        }
    }

    internal static class ScenarioLog
    {
        private static readonly LogContext Log = new("ScenarioLog");

        public static void PresetSnapshot(int presetId, ScenarioLogPhase phase, in ScenarioLogSnapshot snapshot)
        {
            if (!Log.IsDebugEnabled) return;

            Log.Debug($"[SCENARIO:{presetId}] {GetPhaseTag(phase)} " +
                $"Act={snapshot.Act}, Shock={snapshot.ShockLevel:F0}%, " +
                $"Stress={snapshot.StressHours:F1}h, Integrity={snapshot.CityIntegrity:F2}, " +
                $"Exodus={snapshot.ExodusActive}, Pop={snapshot.Population}");
        }

        public static void Domain(ScenarioLogDomain domain, string message)
        {
            if (!Log.IsDebugEnabled) return;

            Log.Debug($"[SCENARIO:{GetDomainTag(domain)}] {message}");
        }

        private static string GetPhaseTag(ScenarioLogPhase phase)
        {
            switch (phase)
            {
                case ScenarioLogPhase.Before: return "BEFORE";
                case ScenarioLogPhase.After: return "AFTER";
                case ScenarioLogPhase.Reaction: return "REACTION";
                case ScenarioLogPhase.Validate: return "VALIDATE";
                default: return "UNKNOWN";
            }
        }

        private static string GetDomainTag(ScenarioLogDomain domain)
        {
            switch (domain)
            {
                case ScenarioLogDomain.Preset: return "preset";
                case ScenarioLogDomain.Wave: return "wave";
                case ScenarioLogDomain.AirDefense: return "aa";
                case ScenarioLogDomain.Grid: return "grid";
                case ScenarioLogDomain.Blackout: return "blackout";
                case ScenarioLogDomain.Shock: return "shock";
                case ScenarioLogDomain.Exodus: return "exodus";
                case ScenarioLogDomain.Cognitive: return "cognitive";
                case ScenarioLogDomain.Corruption: return "corruption";
                case ScenarioLogDomain.Act: return "act";
                case ScenarioLogDomain.Defeat: return "defeat";
                default: return "unknown";
            }
        }
    }
}
#endif
