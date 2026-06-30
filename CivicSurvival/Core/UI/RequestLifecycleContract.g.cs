// GENERATED - DO NOT EDIT
// Source:           Docs/Contracts/request-lifecycle.contract.yaml
// SourceHash:       sha256:cfc0d169fd0de4d469917aec0edd40ffdbea785a043671e0bfe907ac19c82c45
// Generator:        scripts/generators/request_lifecycle.py
// GeneratorVersion: 1.2.0
// ContractVersion:  1.0.0
// GeneratedAt:      2026-05-14T00:00:00Z

using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.UI
{
    internal enum RequestResultMode
    {
        Simple,
        Split,
        Keyed,
        PerField
    }

    internal enum RequestResultDiscriminator
    {
        None,
        DistrictIndex,
        OfferKey,
        Field,
        OperationSlot
    }

    internal static class RequestLifecycleContract
    {
        internal static string KeyForKind(RequestKind kind)
        {
            switch (kind)
            {
                case RequestKind.EmergencyResupply: return "EmergencyResupplyRequest";
                case RequestKind.PlantRepair: return "PlantRepairRequest";
                case RequestKind.CivilianRepair: return "CivilianRepairRequest";
                case RequestKind.DonorSelection: return "DonorSelectionRequest";
                case RequestKind.DonorDialog: return "DonorDialogRequest";
                case RequestKind.IntelPurchase: return "InsiderRequest";
                case RequestKind.IntelUpgrade: return "IntelUpgradeRequest";
                case RequestKind.HeroAction: return "HeroActionRequest";
                case RequestKind.CountermeasureChoice: return "LastChoiceRequestResult";
                case RequestKind.Modernization: return "ModernizationRequest";
                case RequestKind.SpotterAction: return "SpotterActionRequest";
                case RequestKind.AirDefensePlacement: return "AirDefensePlacementRequest";
                case RequestKind.BackupPolicy: return "BackupPolicyRequest";
                case RequestKind.DefensePolicy: return "DefensePolicyRequest";
                case RequestKind.PatriotDroneToggle: return "PatriotDroneToggleRequest";
                case RequestKind.Mobilization: return "CallToArmsRequest";
                case RequestKind.ConscriptionToggle: return "ConscriptionToggleRequest";
                case RequestKind.DistrictToggle: return "DistrictToggleRequest";
                case RequestKind.DistrictInternetToggle: return "DistrictInternetToggleRequest";
                case RequestKind.CitySchedule: return "CitySchedulePeriodRequest";
                case RequestKind.InternetMode: return "InternetModeRequest";
                case RequestKind.ProcurementLevel: return "ProcurementLevelRequest";
                case RequestKind.AidDistribution: return "LastDistributeResult";
                case RequestKind.CorruptionScheme: return "CorruptionSchemeRequest";
                case RequestKind.MaintenanceContract: return "MaintenanceContractRequest";
                case RequestKind.ShadowTradeImport: return "ShadowTradeImportRequest";
                case RequestKind.ShadowTradeExport: return "ShadowTradeExportRequest";
                case RequestKind.TelemarathonMode: return "TelemarathonModeRequest";
                case RequestKind.TelemarathonActive: return "TelemarathonActiveRequest";
                case RequestKind.AutoDispatchToggle: return "AutoDispatchToggleRequest";
                case RequestKind.OperationLaunch: return "OperationRequest";
                case RequestKind.NicknameUpdate: return "NicknameRequest";
                case RequestKind.LocaleChange: return "LocaleRequest";
                case RequestKind.ArenaRefresh: return "ArenaLastRefreshResult";
                case RequestKind.OneMoreYear: return "OneMoreYearRequest";
                case RequestKind.EndlessMode: return "EndlessModeRequest";
                case RequestKind.CrisisSweep: return "CrisisSweepRequest";
                default: return "";
            }
        }

        internal static RequestKind KindForKey(string key)
        {
            switch (key)
            {
                case "EmergencyResupplyRequest": return RequestKind.EmergencyResupply;
                case "PlantRepairRequest": return RequestKind.PlantRepair;
                case "CivilianRepairRequest": return RequestKind.CivilianRepair;
                case "DonorSelectionRequest": return RequestKind.DonorSelection;
                case "DonorDialogRequest": return RequestKind.DonorDialog;
                case "InsiderRequest": return RequestKind.IntelPurchase;
                case "IntelUpgradeRequest": return RequestKind.IntelUpgrade;
                case "HeroActionRequest": return RequestKind.HeroAction;
                case "LastChoiceRequestResult": return RequestKind.CountermeasureChoice;
                case "ModernizationRequest": return RequestKind.Modernization;
                case "SpotterActionRequest": return RequestKind.SpotterAction;
                case "AirDefensePlacementRequest": return RequestKind.AirDefensePlacement;
                case "BackupPolicyRequest": return RequestKind.BackupPolicy;
                case "DefensePolicyRequest": return RequestKind.DefensePolicy;
                case "PatriotDroneToggleRequest": return RequestKind.PatriotDroneToggle;
                case "CallToArmsRequest": return RequestKind.Mobilization;
                case "ConscriptionToggleRequest": return RequestKind.ConscriptionToggle;
                case "DistrictToggleRequest": return RequestKind.DistrictToggle;
                case "DistrictInternetToggleRequest": return RequestKind.DistrictInternetToggle;
                case "CitySchedulePeriodRequest": return RequestKind.CitySchedule;
                case "InternetModeRequest": return RequestKind.InternetMode;
                case "ProcurementLevelRequest": return RequestKind.ProcurementLevel;
                case "LastDistributeResult": return RequestKind.AidDistribution;
                case "CorruptionSchemeRequest": return RequestKind.CorruptionScheme;
                case "MaintenanceContractRequest": return RequestKind.MaintenanceContract;
                case "ShadowTradeImportRequest": return RequestKind.ShadowTradeImport;
                case "ShadowTradeExportRequest": return RequestKind.ShadowTradeExport;
                case "TelemarathonModeRequest": return RequestKind.TelemarathonMode;
                case "TelemarathonActiveRequest": return RequestKind.TelemarathonActive;
                case "AutoDispatchToggleRequest": return RequestKind.AutoDispatchToggle;
                case "OperationRequest": return RequestKind.OperationLaunch;
                case "NicknameRequest": return RequestKind.NicknameUpdate;
                case "LocaleRequest": return RequestKind.LocaleChange;
                case "ArenaLastRefreshResult": return RequestKind.ArenaRefresh;
                case "OneMoreYearRequest": return RequestKind.OneMoreYear;
                case "EndlessModeRequest": return RequestKind.EndlessMode;
                case "CrisisSweepRequest": return RequestKind.CrisisSweep;
                default: return RequestKind.Unknown;
            }
        }

        internal static RequestResultMode ResultModeForKind(RequestKind kind)
        {
            switch (kind)
            {
                case RequestKind.EmergencyResupply: return RequestResultMode.Simple;
                case RequestKind.PlantRepair: return RequestResultMode.Simple;
                case RequestKind.CivilianRepair: return RequestResultMode.Simple;
                case RequestKind.DonorSelection: return RequestResultMode.Simple;
                case RequestKind.DonorDialog: return RequestResultMode.Simple;
                case RequestKind.IntelPurchase: return RequestResultMode.Simple;
                case RequestKind.IntelUpgrade: return RequestResultMode.Simple;
                case RequestKind.HeroAction: return RequestResultMode.Simple;
                case RequestKind.CountermeasureChoice: return RequestResultMode.Simple;
                case RequestKind.Modernization: return RequestResultMode.Keyed;
                case RequestKind.SpotterAction: return RequestResultMode.Simple;
                case RequestKind.AirDefensePlacement: return RequestResultMode.Simple;
                case RequestKind.BackupPolicy: return RequestResultMode.Simple;
                case RequestKind.DefensePolicy: return RequestResultMode.Simple;
                case RequestKind.PatriotDroneToggle: return RequestResultMode.Simple;
                case RequestKind.Mobilization: return RequestResultMode.Simple;
                case RequestKind.ConscriptionToggle: return RequestResultMode.Simple;
                case RequestKind.DistrictToggle: return RequestResultMode.Keyed;
                case RequestKind.DistrictInternetToggle: return RequestResultMode.Keyed;
                case RequestKind.CitySchedule: return RequestResultMode.Simple;
                case RequestKind.InternetMode: return RequestResultMode.Simple;
                case RequestKind.ProcurementLevel: return RequestResultMode.Simple;
                case RequestKind.AidDistribution: return RequestResultMode.Simple;
                case RequestKind.CorruptionScheme: return RequestResultMode.Simple;
                case RequestKind.MaintenanceContract: return RequestResultMode.Keyed;
                case RequestKind.ShadowTradeImport: return RequestResultMode.Simple;
                case RequestKind.ShadowTradeExport: return RequestResultMode.Simple;
                case RequestKind.TelemarathonMode: return RequestResultMode.Simple;
                case RequestKind.TelemarathonActive: return RequestResultMode.Simple;
                case RequestKind.AutoDispatchToggle: return RequestResultMode.Simple;
                case RequestKind.OperationLaunch: return RequestResultMode.Keyed;
                case RequestKind.NicknameUpdate: return RequestResultMode.Simple;
                case RequestKind.LocaleChange: return RequestResultMode.Simple;
                case RequestKind.ArenaRefresh: return RequestResultMode.Simple;
                case RequestKind.OneMoreYear: return RequestResultMode.Simple;
                case RequestKind.EndlessMode: return RequestResultMode.Simple;
                case RequestKind.CrisisSweep: return RequestResultMode.Simple;
                default: return RequestResultMode.Simple;
            }
        }

        internal static RequestResultDiscriminator DiscriminatorForKind(RequestKind kind)
        {
            switch (kind)
            {
                case RequestKind.EmergencyResupply: return RequestResultDiscriminator.None;
                case RequestKind.PlantRepair: return RequestResultDiscriminator.None;
                case RequestKind.CivilianRepair: return RequestResultDiscriminator.None;
                case RequestKind.DonorSelection: return RequestResultDiscriminator.None;
                case RequestKind.DonorDialog: return RequestResultDiscriminator.None;
                case RequestKind.IntelPurchase: return RequestResultDiscriminator.None;
                case RequestKind.IntelUpgrade: return RequestResultDiscriminator.None;
                case RequestKind.HeroAction: return RequestResultDiscriminator.None;
                case RequestKind.CountermeasureChoice: return RequestResultDiscriminator.None;
                case RequestKind.Modernization: return RequestResultDiscriminator.DistrictIndex;
                case RequestKind.SpotterAction: return RequestResultDiscriminator.None;
                case RequestKind.AirDefensePlacement: return RequestResultDiscriminator.None;
                case RequestKind.BackupPolicy: return RequestResultDiscriminator.None;
                case RequestKind.DefensePolicy: return RequestResultDiscriminator.None;
                case RequestKind.PatriotDroneToggle: return RequestResultDiscriminator.None;
                case RequestKind.Mobilization: return RequestResultDiscriminator.None;
                case RequestKind.ConscriptionToggle: return RequestResultDiscriminator.None;
                case RequestKind.DistrictToggle: return RequestResultDiscriminator.DistrictIndex;
                case RequestKind.DistrictInternetToggle: return RequestResultDiscriminator.DistrictIndex;
                case RequestKind.CitySchedule: return RequestResultDiscriminator.None;
                case RequestKind.InternetMode: return RequestResultDiscriminator.None;
                case RequestKind.ProcurementLevel: return RequestResultDiscriminator.None;
                case RequestKind.AidDistribution: return RequestResultDiscriminator.None;
                case RequestKind.CorruptionScheme: return RequestResultDiscriminator.None;
                case RequestKind.MaintenanceContract: return RequestResultDiscriminator.OfferKey;
                case RequestKind.ShadowTradeImport: return RequestResultDiscriminator.None;
                case RequestKind.ShadowTradeExport: return RequestResultDiscriminator.None;
                case RequestKind.TelemarathonMode: return RequestResultDiscriminator.None;
                case RequestKind.TelemarathonActive: return RequestResultDiscriminator.None;
                case RequestKind.AutoDispatchToggle: return RequestResultDiscriminator.None;
                case RequestKind.OperationLaunch: return RequestResultDiscriminator.OperationSlot;
                case RequestKind.NicknameUpdate: return RequestResultDiscriminator.None;
                case RequestKind.LocaleChange: return RequestResultDiscriminator.None;
                case RequestKind.ArenaRefresh: return RequestResultDiscriminator.None;
                case RequestKind.OneMoreYear: return RequestResultDiscriminator.None;
                case RequestKind.EndlessMode: return RequestResultDiscriminator.None;
                case RequestKind.CrisisSweep: return RequestResultDiscriminator.None;
                default: return RequestResultDiscriminator.None;
            }
        }
    }
}
