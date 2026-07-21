// GENERATED - DO NOT EDIT
// Source:           Docs/Contracts/request-lifecycle.contract.yaml
// SourceHash:       sha256:dd3cd9d3d783da53d6cbbccaebb95dd1d4e31f9cdba817aeb8852742e58fdff6
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
        [RequestResultKey("BackupPolicyRequest")]
        BackupPolicy = 15,
        [RequestResultKey("DefensePolicyRequest")]
        DefensePolicy = 16,
        [RequestResultKey("PatriotDroneToggleRequest")]
        PatriotDroneToggle = 17,
        [RequestResultKey("CallToArmsRequest")]
        Mobilization = 18,
        [RequestResultKey("ConscriptionToggleRequest")]
        ConscriptionToggle = 19,
        [RequestResultKey("DistrictToggleRequest")]
        DistrictToggle = 20,
        [RequestResultKey("DistrictInternetToggleRequest")]
        DistrictInternetToggle = 21,
        [RequestResultKey("CitySchedulePeriodRequest")]
        CitySchedule = 22,
        [RequestResultKey("InternetModeRequest")]
        InternetMode = 23,
        [RequestResultKey("PropagandaCenterUpgradeRequest")]
        PropagandaCenterUpgrade = 24,
        [RequestResultKey("ProcurementLevelRequest")]
        ProcurementLevel = 25,
        [RequestResultKey("LastDistributeResult")]
        AidDistribution = 26,
        [RequestResultKey("CorruptionSchemeRequest")]
        CorruptionScheme = 27,
        [RequestResultKey("MaintenanceContractRequest")]
        MaintenanceContract = 28,
        [RequestResultKey("ShadowTradeImportRequest")]
        ShadowTradeImport = 29,
        [RequestResultKey("ShadowTradeExportRequest")]
        ShadowTradeExport = 30,
        [RequestResultKey("TelemarathonModeRequest")]
        TelemarathonMode = 31,
        [RequestResultKey("TelemarathonActiveRequest")]
        TelemarathonActive = 32,
        [RequestResultKey("AutoDispatchToggleRequest")]
        AutoDispatchToggle = 33,
        [RequestResultKey("OperationRequest")]
        OperationLaunch = 34,
        [RequestResultKey("NicknameRequest")]
        NicknameUpdate = 35,
        [RequestResultKey("LocaleRequest")]
        LocaleChange = 36,
        [RequestResultKey("ArenaLastRefreshResult")]
        ArenaRefresh = 37,
        [RequestResultKey("OneMoreYearRequest")]
        OneMoreYear = 38,
        [RequestResultKey("EndlessModeRequest")]
        EndlessMode = 39,
        [RequestResultKey("CrisisSweepRequest")]
        CrisisSweep = 40
    }
}
