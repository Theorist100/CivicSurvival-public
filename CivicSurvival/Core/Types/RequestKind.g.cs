// GENERATED - DO NOT EDIT
// Source:           Docs/Contracts/request-lifecycle.contract.yaml
// SourceHash:       sha256:cfc0d169fd0de4d469917aec0edd40ffdbea785a043671e0bfe907ac19c82c45
// Generator:        scripts/generators/request_lifecycle.py
// GeneratorVersion: 1.2.0
// ContractVersion:  1.0.0
// GeneratedAt:      2026-05-14T00:00:00Z

using CivicSurvival.Core.Attributes;

namespace CivicSurvival.Core.Types
{
    public enum RequestKind : ushort
    {
        Unknown = 0,
        [RequestResultKey("EmergencyResupplyRequest")]
        EmergencyResupply = 1,
        [RequestResultKey("PlantRepairRequest")]
        PlantRepair = 2,
        [RequestResultKey("CivilianRepairRequest")]
        CivilianRepair = 3,
        [RequestResultKey("DonorSelectionRequest")]
        DonorSelection = 4,
        [RequestResultKey("DonorDialogRequest")]
        DonorDialog = 5,
        [RequestResultKey("InsiderRequest")]
        IntelPurchase = 6,
        [RequestResultKey("IntelUpgradeRequest")]
        IntelUpgrade = 7,
        [RequestResultKey("HeroActionRequest")]
        HeroAction = 8,
        [RequestResultKey("LastChoiceRequestResult")]
        CountermeasureChoice = 9,
        [RequestResultKey("ModernizationRequest")]
        Modernization = 10,
        [RequestResultKey("SpotterActionRequest")]
        SpotterAction = 11,
        [RequestResultKey("AirDefensePlacementRequest")]
        AirDefensePlacement = 12,
        [RequestResultKey("BackupPolicyRequest")]
        BackupPolicy = 13,
        [RequestResultKey("DefensePolicyRequest")]
        DefensePolicy = 14,
        [RequestResultKey("PatriotDroneToggleRequest")]
        PatriotDroneToggle = 15,
        [RequestResultKey("CallToArmsRequest")]
        Mobilization = 16,
        [RequestResultKey("ConscriptionToggleRequest")]
        ConscriptionToggle = 17,
        [RequestResultKey("DistrictToggleRequest")]
        DistrictToggle = 18,
        [RequestResultKey("DistrictInternetToggleRequest")]
        DistrictInternetToggle = 19,
        [RequestResultKey("CitySchedulePeriodRequest")]
        CitySchedule = 20,
        [RequestResultKey("InternetModeRequest")]
        InternetMode = 21,
        [RequestResultKey("ProcurementLevelRequest")]
        ProcurementLevel = 22,
        [RequestResultKey("LastDistributeResult")]
        AidDistribution = 23,
        [RequestResultKey("CorruptionSchemeRequest")]
        CorruptionScheme = 24,
        [RequestResultKey("MaintenanceContractRequest")]
        MaintenanceContract = 25,
        [RequestResultKey("ShadowTradeImportRequest")]
        ShadowTradeImport = 26,
        [RequestResultKey("ShadowTradeExportRequest")]
        ShadowTradeExport = 27,
        [RequestResultKey("TelemarathonModeRequest")]
        TelemarathonMode = 28,
        [RequestResultKey("TelemarathonActiveRequest")]
        TelemarathonActive = 29,
        [RequestResultKey("AutoDispatchToggleRequest")]
        AutoDispatchToggle = 30,
        [RequestResultKey("OperationRequest")]
        OperationLaunch = 31,
        [RequestResultKey("NicknameRequest")]
        NicknameUpdate = 32,
        [RequestResultKey("LocaleRequest")]
        LocaleChange = 33,
        [RequestResultKey("ArenaLastRefreshResult")]
        ArenaRefresh = 34,
        [RequestResultKey("OneMoreYearRequest")]
        OneMoreYear = 35,
        [RequestResultKey("EndlessModeRequest")]
        EndlessMode = 36,
        [RequestResultKey("CrisisSweepRequest")]
        CrisisSweep = 37
    }
}
