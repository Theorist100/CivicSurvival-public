using System;

namespace CivicSurvival.Core.Types
{
    /// <summary>
    /// Flags for shown modals (prevent showing same modal twice).
    /// Shared type: used by Scenario (persistence) and Tutorial (display logic).
    /// </summary>
    [Flags]
#pragma warning disable CA1711, S2344 // Identifiers should not have incorrect suffix - this IS a flags enum
    public enum ModalFlags : uint
#pragma warning restore CA1711, S2344
    {
        None = 0,
        IntroModal = 1 << 0,
        WarBegins = 1 << 1,
        FirstStrike = 1 << 2,
        RefugeesArriving = 1 << 3,
        BankingCollapse = 1 << 4,
        InfrastructureCollapse = 1 << 5,
        GhostTown = 1 << 6,
        WhoStaysBehind = 1 << 7,
        ScheduleTutorial = 1 << 8,
        FirstDonorAid = 1 << 9,
        FirstSuccessfulDefense = 1 << 10,
        GeneratorEra = 1 << 11,
        SpotterAlert = 1 << 12,
        CorruptionOffer = 1 << 13,
        WarFatigue = 1 << 14,
        OneYearVictory = 1 << 15,

        AllFlags = IntroModal
            | WarBegins
            | FirstStrike
            | RefugeesArriving
            | BankingCollapse
            | InfrastructureCollapse
            | GhostTown
            | WhoStaysBehind
            | ScheduleTutorial
            | FirstDonorAid
            | FirstSuccessfulDefense
            | GeneratorEra
            | SpotterAlert
            | CorruptionOffer
            | WarFatigue
            | OneYearVictory
    }
}
