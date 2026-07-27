// GENERATED - DO NOT EDIT
// Source:           Docs/Contracts/request-lifecycle.contract.yaml
// SourceHash:       sha256:d1b71ca38aa6d70c22097c0028ea415e36785267688c632ba4377d6cdd12802b
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
        [RequestResultKey("PropagandaCenterPlacementRequest")]
        PropagandaCenterPlacement = 13,
        [RequestResultKey("DroneLauncherPlacementRequest")]
        DroneLauncherPlacement = 14,
        [RequestResultKey("BallisticLauncherPlacementRequest")]
        BallisticLauncherPlacement = 15,
        [RequestResultKey("BackupPolicyRequest")]
        BackupPolicy = 16,
        [RequestResultKey("DefensePolicyRequest")]
        DefensePolicy = 17,
        [RequestResultKey("PatriotDroneToggleRequest")]
        PatriotDroneToggle = 18,
        [RequestResultKey("CallToArmsRequest")]
        Mobilization = 19,
        [RequestResultKey("ConscriptionToggleRequest")]
        ConscriptionToggle = 20,
        [RequestResultKey("DistrictToggleRequest")]
        DistrictToggle = 21,
        [RequestResultKey("DistrictInternetToggleRequest")]
        DistrictInternetToggle = 22,
        [RequestResultKey("CitySchedulePeriodRequest")]
        CitySchedule = 23,
        [RequestResultKey("InternetModeRequest")]
        InternetMode = 24,
        [RequestResultKey("PropagandaCenterUpgradeRequest")]
        PropagandaCenterUpgrade = 25,
        [RequestResultKey("ProcurementLevelRequest")]
        ProcurementLevel = 26,
        [RequestResultKey("LastDistributeResult")]
        AidDistribution = 27,
        [RequestResultKey("CorruptionSchemeRequest")]
        CorruptionScheme = 28,
        [RequestResultKey("MaintenanceContractRequest")]
        MaintenanceContract = 29,
        [RequestResultKey("ShadowTradeImportRequest")]
        ShadowTradeImport = 30,
        [RequestResultKey("ShadowTradeExportRequest")]
        ShadowTradeExport = 31,
        [RequestResultKey("TelemarathonModeRequest")]
        TelemarathonMode = 32,
        [RequestResultKey("TelemarathonActiveRequest")]
        TelemarathonActive = 33,
        [RequestResultKey("AutoDispatchToggleRequest")]
        AutoDispatchToggle = 34,
        [RequestResultKey("OperationRequest")]
        OperationLaunch = 35,
        [RequestResultKey("NicknameRequest")]
        NicknameUpdate = 36,
        [RequestResultKey("LocaleRequest")]
        LocaleChange = 37,
        [RequestResultKey("ArenaLastRefreshResult")]
        ArenaRefresh = 38,
        [RequestResultKey("OneMoreYearRequest")]
        OneMoreYear = 39,
        [RequestResultKey("EndlessModeRequest")]
        EndlessMode = 40,
        [RequestResultKey("CrisisSweepRequest")]
        CrisisSweep = 41
    }
}
