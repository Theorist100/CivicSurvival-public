namespace CivicSurvival.Core.Types
{
    /// <summary>
    /// Type-safe enum for narrative triggers.
    /// Provides compile-time safety for trigger keys.
    ///
    /// Usage:
    /// - New code: Use enum + ToKey() extension
    /// - Legacy code: NarrativeTriggers.* constants still work
    ///
    /// Migration path:
    /// 1. New code uses NarrativeTrigger enum
    /// 2. ToKey() converts to string for NarrativeTriggerEvent
    /// 3. Gradually migrate existing call sites
    /// </summary>
    public enum NarrativeTrigger
    {
        // === Blackout/Power ===
        SatireBlackout = 0,
        SatireRestored,
        SatireFire,
        SatireCascade,
        SatireWinter,
        SatireBattery,

        // === Disaster ===
        SatireDisaster,
        SatireShadyDisaster,
        SatireImport,
        SatireExport,

        // === Investigation ===
        SatireInvestStart,
        SatireInvestProgress,
        SatireInvestStop,
        SatireArticle,
        SatirePolice,
        SatireArrest,
        SatireProtest,
        SatireSuspicion,

        // === Personas ===
        SatireKotleta,
        SatireBabcya,
        SatirePetrenko,
        SatireValera,
        SatireIt,
        SatireVolunteer,
        SatireTaxi,

        // === Threats ===
        SatireThreatImpact,
        SatireThreatDebris,
        SatireThreatSuccess,

        // === VIP/Donor ===
        SatireVip,
        SatireElite,
        SatireDonorCalled,
        SatireDonorFunds,
        SatireDonorGenerators,
        SatireDonorPatriot,
        SatireDonorPatriotExpired,
        SatireDonorRefused,
        SatireDonorSuspicion,
        SatireSanctionsApplied,

        // === Buckwheat ===
        SatireBuckwheatBabushka,
        SatireBuckwheatValera,
        SatireBuckwheatStudent,
        SatireBuckwheatProcurement,

        // === Spotter ===
        SpotterSpawn,
        SpotterReactivate,
        SpotterImpact,
        SpotterSbuVisit,
        SpotterEvacuated,
        SpotterReturn,
        SpotterCivilianReport,  // Telemarathon Alarmist mode - vigilant citizens report spotters

        // === Counter-OSINT ===
        CounterOsintStart,
        CounterOsintStop,
        CounterOsintCancel,
        InternetDisabled,

        // === Journalist ===
        MarianaSbu,
        MarianaInternet,

        // === Wave/AA ===
        GridSurplus,
        AaResupply,
        AaPartial,
        AaEmpty,
        AaEmergency,

        // === Attention/Shock ===
        ShockGlobalBreaking,
        ShockGlobalUn,
        ShockHeadlines,
        ShockStabilizing,
        ShockCriticalInfra,
        ExodusMass,
        ExodusModerate,
        ExodusBraindrain,

        // === Scenario/Crisis ===
        ShockStarted,
        ShockBanking,
        ShockWithdraw,

        // === Act Transitions ===
        ActShock,
        ActExodus,
        ActAdaptation,
        ActRoutine,

        // === Milestones ===
        Milestone30,
        Milestone90,
        Milestone180,
        MilestoneVictory,

        // === Refugees ===
        RefugeeArrived,
        RefugeeParkBuilt,
        RefugeeNoPark,
        RefugeeComplete,
        RefugeeUnNews,
        RefugeeCollapse,
        RefugeeBus,
        RefugeeIntegrated,

        // === Ominous Signs ===
        OminousTensions,
        OminousWar1,
        OminousWar2,
        OminousWar3,
        OminousEmergency,
        OminousSign,

        // === Chirper Storm ===
        StormValera1,
        StormMayor,
        StormGrid,
        StormResident,
        StormNexta,
        StormHospital,
        StormIt,
        StormValera2,
        StormBabcya,
        StormPetrenko
    }

    /// <summary>
    /// Extension methods for NarrativeTrigger enum.
    /// </summary>
#pragma warning disable CA1810, S3963 // Static constructor is intentional for PERF: pre-compute all enum keys once
    public static class NarrativeTriggerExtensions
    {
        // ===== Prefix Lengths for Key Computation =====
        private const int PREFIX_LEN_SATIRE = 6;
        private const int PREFIX_LEN_SPOTTER = 7;
        private const int PREFIX_LEN_COUNTER = 7;
        private const int PREFIX_LEN_MARIANA = 7;
        private const int PREFIX_LEN_EXODUS = 6;
        private const int PREFIX_LEN_MILESTONE = 9;
        private const int PREFIX_LEN_REFUGEE = 7;
        private const int PREFIX_LEN_OMINOUS = 7;
        private const int PREFIX_LEN_SHOCK = 5;
        private const int PREFIX_LEN_ACT = 3;
        private const int PREFIX_LEN_STORM = 5;

        // PERF: Pre-computed key cache to avoid ToString() allocation on every call
#pragma warning disable CIVIC148 // Cache derived from compile-time enum values — immutable after init
        private static readonly System.Collections.Generic.Dictionary<NarrativeTrigger, string> s_KeyCache;
#pragma warning restore CIVIC148

        static NarrativeTriggerExtensions()
        {
            var values = System.Enum.GetValues(typeof(NarrativeTrigger)) as NarrativeTrigger[];
            s_KeyCache = new System.Collections.Generic.Dictionary<NarrativeTrigger, string>(values!.Length);
            foreach (var trigger in values)
            {
                s_KeyCache[trigger] = ComputeKey(trigger);
            }
        }

        /// <summary>
        /// Convert enum to string key for NarrativeTriggerEvent.
        /// Format: SATIRE_{ENUM_NAME} in uppercase with underscores.
        /// PERF: Uses cached lookup — no allocation.
        /// </summary>
        public static string ToKey(this NarrativeTrigger trigger)
        {
            return s_KeyCache.TryGetValue(trigger, out var key) ? key : ComputeKey(trigger);
        }

        private static string ComputeKey(NarrativeTrigger trigger)
        {
            if (trigger == NarrativeTrigger.SatireInvestProgress)
                return "SATIRE_INVEST_PROG";

            // Convert PascalCase to SCREAMING_SNAKE_CASE
            // e.g., SatireBlackout → SATIRE_BLACKOUT
            var name = trigger.ToString();

            // Handle special prefixes
            if (name.StartsWith("Satire"))
                return "SATIRE_" + ToScreamingSnake(name.Substring(PREFIX_LEN_SATIRE));
            if (name.StartsWith("Spotter"))
                return "SATIRE_SPOTTER_" + ToScreamingSnake(name.Substring(PREFIX_LEN_SPOTTER));
            if (name.StartsWith("Counter"))
                return "SATIRE_COUNTER_" + ToScreamingSnake(name.Substring(PREFIX_LEN_COUNTER));
            if (name.StartsWith("Mariana"))
                return "SATIRE_MARIANA_" + ToScreamingSnake(name.Substring(PREFIX_LEN_MARIANA));
            if (name.StartsWith("Shock"))
                return "SATIRE_SHOCK_" + ToScreamingSnake(name.Substring(PREFIX_LEN_SHOCK));
            if (name.StartsWith("Exodus"))
                return "SATIRE_EXODUS_" + ToScreamingSnake(name.Substring(PREFIX_LEN_EXODUS));
            if (name.StartsWith("Act"))
                return "SATIRE_ACT_" + ToScreamingSnake(name.Substring(PREFIX_LEN_ACT));
            if (name.StartsWith("Milestone"))
                return "SATIRE_MILESTONE_" + ToScreamingSnake(name.Substring(PREFIX_LEN_MILESTONE));
            if (name.StartsWith("Refugee"))
                return "SATIRE_REFUGEE_" + ToScreamingSnake(name.Substring(PREFIX_LEN_REFUGEE));
            if (name.StartsWith("Ominous"))
                return "SATIRE_OMINOUS_" + ToScreamingSnake(name.Substring(PREFIX_LEN_OMINOUS));
            if (name.StartsWith("Storm"))
                return "SATIRE_STORM_" + ToScreamingSnake(name.Substring(PREFIX_LEN_STORM));

            // Grid, Aa, Internet - simple cases
            return "SATIRE_" + ToScreamingSnake(name);
        }

        private static string ToScreamingSnake(string pascalCase)
        {
            if (string.IsNullOrEmpty(pascalCase)) return "";

            var result = new System.Text.StringBuilder();
            for (int i = 0; i < pascalCase.Length; i++)
            {
                char c = pascalCase[i];
                // Add underscore before uppercase letters (except at start or after uppercase)
                if (i > 0 && char.IsUpper(c) && !char.IsUpper(pascalCase[i - 1]))
                {
                    result.Append('_');
                }
                // Add underscore before digits (except at start or after digit)
                if (i > 0 && char.IsDigit(c) && !char.IsDigit(pascalCase[i - 1]))
                {
                    result.Append('_');
                }
                result.Append(char.ToUpperInvariant(c));
            }
            return result.ToString();
        }
    }
#pragma warning restore CA1810, S3963
}
