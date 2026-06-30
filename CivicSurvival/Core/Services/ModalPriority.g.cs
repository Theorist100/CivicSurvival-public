// GENERATED - DO NOT EDIT
// Source:           Docs/Contracts/modal.contract.yaml
// SourceHash:       sha256:9f6e8df1023e805d39aa3e20ab44f550738f77792cf1de7c75bee8c638aaeb71
// Generator:        scripts/generators/modal.py
// GeneratorVersion: 1.0.0
// ContractVersion:  1.0.0
// GeneratedAt:      2026-06-06T00:00:00Z

namespace CivicSurvival.Core.Services
{
    /// <summary>
    /// Modal show-priority map: higher priority wins the active slot, lower priority queues.
    /// Generated from Docs/Contracts/modal.contract.yaml — edit the contract, not this file.
    /// </summary>
    internal static class ModalPriority
    {
        /// <summary>Fallback priority for ids absent from the contract.</summary>
        public const int Default = 30;

        public static int Get(string id) => id switch
        {
            "Arrested" => 100,
            "Defeat" => 95,
            "ModLoadFailure" => 90,
            "Victory" => 80,
            "WarFatigue" => 70,
            "OnlineConsent" => 67,
            "Intro" => 65,
            "FirstStrike" => 60,
            "GridCollapse" => 58,
            "ExodusWarning" => 55,
            "GridCritical" => 54,
            "WarBegins" => 50,
            "FirstDonorAid" => 45,
            "FirstSuccessfulDefense" => 45,
            "GeneratorEra" => 45,
            "SpotterAlert" => 45,
            "CorruptionOffer" => 45,
            "GhostTown" => 45,
            "WhoStaysBehind" => 45,
            "Refugee" => 40,
            "Collapse" => 40,
            "Debriefing" => 10,
            _ => Default
        };
    }
}
