// GENERATED - DO NOT EDIT
// Source:           Docs/Contracts/ui-dto.contract.yaml
// SourceHash:       sha256:8c205574c11c79c4dfa169983191fa57ef8a56e50cad289c8c1d10fc1e2f220b
// Generator:        scripts/generators/ui_dto.py
// GeneratorVersion: 1.0.0
// ContractVersion:  1.0.0
// GeneratedAt:      2026-05-14T00:00:00Z

import { type BackupPolicyId, type BribeRiskWarning, type CounterChoiceType, type CounterHeatLevel, type CounterPhase, type DefensePolicyId, type FreezeReason, type GlobalConnectionStatus, type GridStatus, type GridStressZone, type PowerScheduleId, type ProcurementLevelId, type RequestResult, type SettingsDifficultyPreset, type SettingsLanguagePreference, type SettingsTheme, type ShockTier, type TensionStatus, type WaveDataStatus, type WavePhase } from "./dtoSubTypes";
import { isWaveDataStatus, isWavePhase, isRequestResult } from "./dtoSubTypes";
import { type ProgressFraction } from "./branded";
import { type DistrictIndex, type PlantId, type PlantState, type SchedulePresetId } from "./semantic";
import { isStringRecord } from "../utils/typeGuards";

export interface ActionAvailability {
    CanRun: boolean;
    LockedReasonId: string;
    EffectiveCost?: number;
}

export interface EntityRefDto {
    Index: number;
    Version: number;
}

export interface CrashDumpEntry {
    Name: string;
    SizeMb: number;
    TimeText: string;
}

export interface CivilianDamageData {
    Building: EntityRefDto;
    Name: string;
    HitCount: number;
    MaxHits: number;
    DamagePercent: number;
    IsRepairing: boolean;
    RepairHoursLeft: number;
    MunicipalRepairCharge: number;
    MunicipalKickbackRepairCharge: number;
    KickbackRepairAmount: number;
    CanMunicipalRepair: boolean;
    MunicipalRepairLockedReasonId: string;
    CanKickbackRepair: boolean;
    KickbackRepairLockedReasonId: string;
    ShadowOpsRepairCharge: number;
    CanShadowRepair: boolean;
    ShadowRepairLockedReasonId: string;
}

export interface ActiveContractEntry {
    EntityIndex: number;
    BuildingName: string;
    ContractType: string;
    VendorName: string;
    Quality: number;
    KickbackAmount: number;
    IsShady: boolean;
    DaysRemaining: number;
}

export interface PendingProcurementOfferEntry {
    EntityIndex: number;
    EntityVersion: number;
    Service: string;
    ContractType: string;
    OfficialVendorName: string;
    ShadyVendorName: string;
    OfficialPrice: number;
    ShadyPrice: number;
    KickbackOffer: number;
    OfficialQuality: number;
    ShadyQuality: number;
    CanAcceptShady: boolean;
    AcceptShadyLockedReasonId: string;
    AcceptShadyEffectiveCost: number;
    BuildingName: string;
}

export interface PlantWearData {
    PlantId: PlantId;
    Name: string;
    CapacityMW: number;
    CurrentOutputMW: number;
    WearPercent: ProgressFraction;
    RepairBillablePercent: number;
    IsRepairable: boolean;
    IsDestroyed: boolean;
    IsRepairing: boolean;
    RepairHoursLeft: number;
    HasExploded: boolean;
    IsUnderConstruction: boolean;
    ConstructionDaysLeft: number;
    OperationalDamagePercent: number;
    OperationalHitCount: number;
    OperationalHitMax: number;
    DisasterDamagePercent: number;
    IsAtRisk: boolean;
    MunicipalRepairCharge: number;
    MunicipalKickbackRepairCharge: number;
    KickbackRepairAmount: number;
    CanMunicipalRepair: boolean;
    MunicipalRepairLockedReasonId: string;
    CanKickbackRepair: boolean;
    KickbackRepairLockedReasonId: string;
    ShadowOpsRepairCharge: number;
    CanShadowRepair: boolean;
    ShadowRepairLockedReasonId: string;
    State: PlantState;
    SaturationFactor: number;
    FuelAvailabilityPercent: number;
    FuelFactor: number;
    RecoveryHours: number;
}

export interface MapBoundsDto {
    MinX: number;
    MaxX: number;
    MinZ: number;
    MaxZ: number;
}

export interface RadarInterceptionDto {
    X: number;
    Z: number;
    TimeAgo: number;
    Lifetime: number;
    Success: boolean;
}

export interface RadarTargetDto {
    Entity: EntityRefDto;
    X: number;
    Z: number;
    Name: string;
    SizeX: number;
    SizeY: number;
    SizeZ: number;
    RotationY: number;
}

export interface RadarThreatDto {
    Entity: EntityRefDto;
    X: number;
    Z: number;
    Vx: number;
    Vz: number;
    Eta: number;
    Altitude: number;
    Type: string;
    EvasionStatus: string;
    IsIdentified: boolean;
}

export interface RadarDefenseDto {
    X: number;
    Z: number;
    Range: number;
}

export interface ShadowProgramEntry {
    DistrictIndex: DistrictIndex;
    DistrictName: string;
    HasProgram: boolean;
    Contractor: string;
    EstimatedCost: number;
    CanModernizeHonest: boolean;
    ModernizeHonestLockedReasonId: string;
    CanModernizeCorrupt: boolean;
    ModernizeCorruptLockedReasonId: string;
    KickbackEarned: number;
    FireCount: number;
}

export interface Vector3IntDto {
    X: number;
    Y: number;
    Z: number;
}

export interface ThreatTargetDto {
    EntityIndex: number;
    EntityVersion: number;
    Name: string;
    Position: Vector3IntDto;
    ThreatCount: number;
    MinEtaSeconds: number;
}

export interface FocusRangeDto {
    Min: number;
    Max: number;
}

export interface OfficialTreasuryDto {
    Balance: number;
    TotalIncome: number;
    TotalExpenses: number;
}

export interface ShadowWalletDto {
    Available: number;
    LockedBalance: number;
    TotalAssets: number;
    ShadowIncome: number;
    ShadowExpenses: number;
}

export interface OperationSlotDto {
    AttackType: string;
    OperationState: string;
    Cost: number;
    Progress: number;
}

export interface AttackTimeEstimateDto {
    Status: string;
    MinHours?: number;
    MaxHours?: number;
}

export interface CognitiveDistrictEntry {
    DistrictIndex: DistrictIndex;
    Name: string;
    Integrity: number;
    HasInternet: boolean;
    IsCompromised: boolean;
    IsUnzoned: boolean;
}

export interface NewsPostDto {
    PostId: string;
    Source: string;
    Title: string;
    Body: string;
    Mood: string;
    Timestamp: number;
    Category: string;
    Scope: string;
    IsAiGenerated: boolean;
}

export interface SocialPostDto {
    Author: string;
    AuthorName: string;
    Message: string;
    Mood: string;
    Timestamp: number;
    IsOfficial: boolean;
}

export interface ToastDataDto {
    Id: number;
    Type: string;
    Priority: number;
    Title: string;
    Message: string;
    AcceptLabel: string;
    RejectLabel: string;
    RemainingSeconds: number;
    Progress: number;
    ContextData: number;
}

export interface RankTierDto {
    Name: string;
    MinFloorHits: number;
    Icon: string;
}

export interface LeaderboardEntryDto {
    Position: number;
    Nickname: string;
    FloorHits: number;
    TotalDamage: number;
    BestStreak: number;
    RankTier: string;
}

export interface WeeklyLeaderboardEntryDto {
    Position: number;
    Nickname: string;
    FloorHits: number;
    DamageDealt: number;
}

export interface AirDefenseDto {
    AaAmmo: number;
    AaMaxAmmo: number;
    AaStations: number;
    SirenActive: boolean;
    PatriotAmmo: number;
    PatriotMaxAmmo: number;
    PatriotResupplyCost: number;
    BoforsAmmo: number;
    BoforsMaxAmmo: number;
    HeritageAmmo: number;
    HeritageMaxAmmo: number;
    GepardAmmo: number;
    GepardMaxAmmo: number;
    GunsResupplyCost: number;
    HeritageCredits: number;
    HeritageCreditsMax: number;
    HeritageCrew: number;
    BoforsCrew: number;
    GepardCrew: number;
    HeritageBoforsCount: number;
    BoforsCount: number;
    GepardCount: number;
    PatriotCount: number;
    BoforsPrice: number;
    PaidBoforsAffordableCount: number;
    PaidGepardAffordableCount: number;
    PaidPatriotAffordableCount: number;
    GepardPrice: number;
    PatriotPrice: number;
    PatriotCrew: number;
    PatriotInterceptsDrones: boolean;
    AutoResupplyEnabled: boolean;
    DefensePolicyName: string;
    DefensePolicyId: DefensePolicyId;
    SpotterPenaltyPercent: number;
    DonorPatriotCredits: number;
    EmergencyResupplyRequest: RequestResult;
    DefensePolicyRequest: RequestResult;
    PatriotDroneToggleRequest: RequestResult;
    AirDefensePlacementRequest: RequestResult;
    CanResupplyPatriot: boolean;
    ResupplyPatriotLockedReasonId: string;
    CanResupplyGuns: boolean;
    ResupplyGunsLockedReasonId: string;
    CanPlaceHeritageBofors: boolean;
    HeritageBoforsLockedReasonId: string;
    CanPlaceDonorPatriot: boolean;
    DonorPatriotLockedReasonId: string;
    CanPlacePaidBofors: boolean;
    PaidBoforsLockedReasonId: string;
    CanPlacePaidGepard: boolean;
    PaidGepardLockedReasonId: string;
    CanPlacePaidPatriot: boolean;
    PaidPatriotLockedReasonId: string;
}

export interface ArrestedModalPayloadDto {
    ChargesCount: number;
    AssetsSeizedSnapshot: number;
    WalletBalanceAfter: number;
    LastChoiceResult: string;
}

export interface AttentionDto {
    ShockLevel: number;
    ShockTier: ShockTier;
    CasualtiesThisWeek: number;
    BuildingsDestroyedThisWeek: number;
    CriticalHitsThisWeek: number;
    TotalCasualties: number;
    TotalBuildingsDestroyed: number;
    TotalCivilianBuildingsDestroyed: number;
    TotalCriticalHits: number;
    ExodusActive: boolean;
    BaseExodusRatePercentPerDay: number;
    ExodusRatePercentPerDay: number;
    TotalExodus: number;
}

export interface BackupPowerDto {
    BackupCharge: number;
    GeneratorsRunning: number;
    NoiseLevel: number;
    ProtectedBuildings: number;
    BackupCapacity: number;
    DischargingCount: number;
    ShadowProgramsJson: ShadowProgramEntry[];
    ProcurementCooldown: number;
    BackupPolicy: BackupPolicyId;
    HospitalsPowered: number;
    HospitalsTotal: number;
    SchoolsPowered: number;
    SchoolsTotal: number;
    ModernizationRequest: RequestResult;
    BackupPolicyRequest: RequestResult;
    CanSetBackupPolicy: boolean;
    SetBackupPolicyLockedReasonId: string;
}

export interface BuckwheatDto {
    BuckwheatTons: number;
    ProcurementLevel: ProcurementLevelId;
    DailyCost: number;
    BaseDailyCost: number;
    LastDistributeResult: RequestResult;
    ProcurementLevelRequest: RequestResult;
    CanDistribute: boolean;
    DistributeLockedReasonId: string;
    CanAffordProcurement: boolean;
    AffordProcurementLockedReasonId: string;
    CanSetProcurement25: boolean;
    Procurement25LockedReasonId: string;
    CanSetProcurement50: boolean;
    Procurement50LockedReasonId: string;
    CanSetProcurement75: boolean;
    Procurement75LockedReasonId: string;
    CanSetProcurement100: boolean;
    Procurement100LockedReasonId: string;
}

export interface CrisisSweepDto {
    Mode: number;
    HasResult: boolean;
    ComputedAtGameHours: number;
    ArchetypeId: number;
    PopulationPeak: number;
    WarDay: number;
    WorstCaseRecoveryBallisticOnly: number;
    WorstCaseRecoveryMixed: number;
    IsRecoverableBallisticOnly: boolean;
    IsRecoverableMixed: boolean;
    GraceWindowHours: number;
    DroneInterceptBallisticOnly: number;
    DroneInterceptMixed: number;
    FreeHeritageGrant: number;
    OperationalAaAtVerdict: number;
    ManpowerTotal: number;
    ManpowerUsed: number;
    ManpowerCasualties: number;
    ManpowerAvailable: number;
    AaHeritage: number;
    AaBofors: number;
    AaGepard: number;
    AaPatriot: number;
    CoveragePct: number;
    AreaKm2: number;
    BallisticInterceptBallisticOnly: number;
    BallisticInterceptMixed: number;
    BallisticTargets: number;
    MissilesSpentOnDrones: number;
    PatriotInterceptsDrones: boolean;
    CalmHours: number;
    WavePressureAtPeak: number;
    SampleCount: number;
    BlackoutProbabilityPct: number;
    MedianCollapseDay: number;
    UnsheddableFloorMW: number;
    RepairSlots: number;
    RepairFundingCash: number;
    RepairTier: number;
    RepairBudgetLive: boolean;
}

export interface DistrictDto {
    EntityIndex: number;
    EntityVersion: number;
    Name: string;
    IsUnzoned: boolean;
    ResidentialOff: boolean;
    CommercialOff: boolean;
    IndustrialOff: boolean;
    OfficeOff: boolean;
    ServicesOff: boolean;
    Schedule: SchedulePresetId;
    ScheduleName: string;
    ScheduleActive: boolean;
    TotalMW: number;
    ResidentialMW: number;
    CommercialMW: number;
    IndustrialMW: number;
    OfficeMW: number;
    ServicesMW: number;
    Priority: number;
    DeliveredMW: number;
    ThresholdCutMW: number;
    IsVIP: boolean;
    IsVIPBypass: boolean;
    IsAutoShedded: boolean;
    InternetDisabled: boolean;
    ThresholdCutBuildings: number;
    TotalHappinessPenalty: number;
    TotalCommercePenalty: number;
    BlackoutSource: string;
}

export interface CognitiveDto {
    CognitiveActive: boolean;
    InfectionRate: number;
    RecoveryRate: number;
    PenaltyThreshold: number;
    TotalDistricts: number;
    CompromisedDistricts: number;
    HeroStatus: number;
    HeroDeployCost: number;
    HeroInfectionReduction: number;
    HeroRecoveryBonus: number;
    HeroActionRequest: RequestResult;
    ProtestRisk: number;
    DominantNarrative: string;
    AvgIntegrity: number;
    TotalHouseholds: number;
    AvgInfection: number;
    AvgResistance: number;
    AvgTrauma: number;
    HouseholdsUnderBlackout: number;
    HouseholdsWithEnvy: number;
    HouseholdsUnderImpact: number;
    HouseholdsInfected: number;
    VulnerableHouseholds: number;
    AvgBlackoutHours: number;
    BlackoutVulnerability: number;
    InternetMode: number;
    CommercePenalty: number;
    InternetModeRequest: RequestResult;
    IpsoActive: boolean;
    IpsoIntensity: number;
    IpsoDistrictCount: number;
    IpsoTotalDistricts: number;
    TelemarathonActive: boolean;
    NarrativeMode: number;
    MediaTrust: number;
    IsInShock: boolean;
    ShockHoursRemaining: number;
    AudienceFatigue: number;
    TelemarathonModeRequest: RequestResult;
    TelemarathonActiveRequest: RequestResult;
    CanDeployHero: boolean;
    DeployHeroLockedReasonId: string;
    CanRecallHero: boolean;
    RecallHeroLockedReasonId: string;
    CanSetHeroCounter: boolean;
    SetHeroCounterLockedReasonId: string;
    CanSetHeroLecturing: boolean;
    SetHeroLecturingLockedReasonId: string;
}

export interface CountermeasuresDto {
    CorruptionScore: number;
    Heat: number;
    HeatLevel: CounterHeatLevel;
    CountermeasuresPhase: CounterPhase;
    InvestigationProgress: number;
    ChargesCount: number;
    ProtestCount: number;
    ChoiceRequired: boolean;
    ChoiceType: CounterChoiceType;
    BribeCost: number;
    BaseBribeCost: number;
    BribeAvailability: ActionAvailability;
    LastChoiceResult: string;
    CurrentJournalist: string;
    IsArrested: boolean;
    ArrestedAssetsSeized: number;
    ArrestedWalletAfter: number;
    BribeRiskWarning: BribeRiskWarning;
    SanctionsSuppressingCorruption: boolean;
    LastChoiceRequestResult: RequestResult;
}

export interface DonorDto {
    DonorUsesRemaining: number;
    DonorCooldownDays: number;
    DonorStatus: string;
    TrustIndex: number;
    ScandalPenalty: number;
    DonorExpectedAid: string;
    DonorDialogActive: boolean;
    ProducerReady: boolean;
    TrustLocked: boolean;
    ProducerReasonId: string;
    DonorFundsAmount: number;
    DonorGeneratorCount: number;
    DonorGeneratorMW: number;
    DonorPatriotDays: number;
    AidTierId: number;
    AidFundsOffered: number;
    AidFundsAccessible: number;
    PatriotOffered: boolean;
    PatriotBlocked: boolean;
    TrustMessageId: number;
    BlockedReasonId: number;
    HasBlockedItems: boolean;
    DonorActiveGenerators: number;
    SanctionsActive: boolean;
    SanctionDaysRemaining: number;
    SanctionTradePenalty: number;
    DonorDialogRequest: RequestResult;
    DonorSelectionRequest: RequestResult;
    DonorFundsAvailable: boolean;
    DonorFundsLockedReasonId: string;
    DonorPowerAvailable: boolean;
    DonorPowerLockedReasonId: string;
    DonorDefenseAvailable: boolean;
    DonorDefenseLockedReasonId: string;
}

export interface ExportDto {
    ExportPercent: number;
    ExportedMW: number;
    DailyIncome: number;
    OffshoreBalance: number;
    IsFrozen: boolean;
    FreezeReason: FreezeReason;
    ExportAvailability: ActionAvailability;
    ShadowTradeExportRequest: RequestResult;
}

export interface FinanceDto {
    CityTreasury: number;
    TotalLiquidity: number;
    OfficialTreasury: OfficialTreasuryDto;
    ShadowWallet: ShadowWalletDto;
    Expenses: Record<string, number>;
    Income: Record<string, number>;
    TotalExpenses: number;
    TotalIncome: number;
    TotalDebt: number;
    DebtBreakdown: Record<string, number>;
    DebtWarning: boolean;
    DebtRestructured: boolean;
    SanctionsMarkup: number;
}

export interface GridWarfareDto {
    ShadowBalance: number;
    ShadowLocked: number;
    ShadowTotal: number;
    EnemyPhysicalAxis: number;
    EnemyDigitalAxis: number;
    EnemySocialAxis: number;
    EnemyInterceptChance: number;
    DroneStock: number;
    BallisticStock: number;
    RespitePhysicalActive: boolean;
    RespiteDigitalActive: boolean;
    RespiteSocialActive: boolean;
    ObjectiveProgress: number;
    CityStability: number;
    StabilityDiscount: number;
    OperationSlots: OperationSlotDto[];
    AttackCosts: Record<string, number>;
    OperationRequest: RequestResult;
    GridWarfareUnlocked: boolean;
    CanPrepareDrone: boolean;
    PrepareDroneLockedReasonId: string;
    CanPrepareBlackout: boolean;
    PrepareBlackoutLockedReasonId: string;
    CanPrepareDisinfo: boolean;
    PrepareDisinfoLockedReasonId: string;
}

export interface ImportDto {
    ShadowImportMW: number;
    MaxShadowImportMW: number;
    SelectedPresetIndex: number;
    ShadowImportCost: number;
    DiscoveryRisk: number;
    ShadowImportDaysActive: number;
    IsSanctioned: boolean;
    ShadowImportSanctionDays: number;
    ShadowImportAvailability: ActionAvailability;
    IsFrozen: boolean;
    FreezeReason: FreezeReason;
    ShadowTradeImportRequest: RequestResult;
}

export interface IntelDto {
    TensionLevel: number;
    TensionStatus: TensionStatus;
    WaveTypePrediction: string;
    IsMassiveStrike: boolean;
    EnergyFocusRange: FocusRangeDto;
    InfraFocusRange: FocusRangeDto;
    ResidentialFocusRange: FocusRangeDto;
    TimeEstimate: AttackTimeEstimateDto;
    ThreatComposition: string;
    EstimatedShaheds: number;
    EstimatedBallistics: number;
    HasInsider: boolean;
    InsiderCost: number;
    BaseInsiderCost: number;
    TensionPriceMultiplier: number;
    TensionPriceModifierPercent: number;
    InsiderRequest: RequestResult;
    IntelUpgradeLevel: number;
    IntelUpgradeCost: number;
    IntelUpgradeRequest: RequestResult;
    CanBuyInsider: boolean;
    InsiderLockedReasonId: string;
    CanUpgradeIntel: boolean;
    IntelUpgradeLockedReasonId: string;
}

export interface MaintenanceDto {
    PendingProcurementOffer: PendingProcurementOfferEntry | null;
    ShadyContractCount: number;
    TotalContractCount: number;
    ActiveContractsJson: ActiveContractEntry[];
    MaintenanceContractRequest: RequestResult;
}

export interface ModalSnapshotDto {
    ActiveId: string;
    ActivePriority: number;
    ActiveData: Record<string, unknown> | null;
    Queue: string[];
    Version: number;
}

export interface MobilizationDto {
    ManpowerAvailable: number;
    ManpowerUsed: number;
    ManpowerTotal: number;
    ManpowerPercent: number;
    ManpowerBasePool: number;
    ManpowerCasualties: number;
    ManpowerPatriotismFactor: number;
    ManpowerMoraleFactor: number;
    ManpowerFatigueFactor: number;
    IsConscriptionActive: boolean;
    IsWarFatigued: boolean;
    IsManpowerCritical: boolean;
    IsManpowerOvercommitted: boolean;
    CallToArmsOnCooldown: boolean;
    ConscriptionReactivationOnCooldown: boolean;
    PredictedConscriptionRelease: number;
    SocialPenaltyProducerReady: boolean;
    SocialPenaltyReasonId: string;
    WarDay: number;
    CallToArmsRequest: RequestResult;
    ConscriptionToggleRequest: RequestResult;
    CanCallToArms: boolean;
    CallToArmsLockedReasonId: string;
    CanToggleConscription: boolean;
    ConscriptionLockedReasonId: string;
}

export interface NewsDto {
    GlobalOnlineNow: number;
    GlobalOnlineHour: number;
    GlobalOnlineToday: number;
    GlobalOnlineTotal: number;
    GlobalConnected: boolean;
    GlobalConnectionStatus: GlobalConnectionStatus;
    NetworkConnectionEnabled: boolean;
    PlayerNickname: string;
    NicknameRequest: RequestResult;
    NicknameChangesRemaining: number;
    NicknameInitialized: boolean;
    OnlineConsentRecorded: boolean;
}

export interface PowerGridDto {
    GridStatus: GridStatus;
    Production: number;
    Demand: number;
    Consumption: number;
    GameHour: number;
    GridFrequency: number;
    StressZone: GridStressZone;
    StressPercent: number;
    RecoveryHours: number;
    CollapseThresholdHours: number;
    ThresholdActive: boolean;
    BuildingsCutCount: number;
    DeliveredMW: number;
    ForcedOffMW: number;
    AutoCutMW: number;
    DistrictShedMW: number;
    AutoDispatchShedMW: number;
    CitySchedule: PowerScheduleId;
    EffectiveCityMode: PowerScheduleId;
    DistrictsOverrideCity: boolean;
    CityScheduleAvailability: ActionAvailability;
    AutoDispatchEnabled: boolean;
    AutoDispatchSheddedCount: number;
    AutoDispatchBlockedByVip: boolean;
    ShadowBalance: number;
    AtRiskPlantCount: number;
    GenerationSources: PlantWearData[];
    CivilianDamage: CivilianDamageData[];
    PlantMunicipalRepairHours: number;
    PlantShadowOpsRepairHours: number;
    CivilianMunicipalRepairHours: number;
    CivilianShadowOpsRepairHours: number;
    PlantRepairRequest: RequestResult;
    CivilianRepairRequest: RequestResult;
    AutoDispatchToggleRequest: RequestResult;
    DistrictToggleRequest: RequestResult;
    CitySchedulePeriodRequest: RequestResult;
    DistrictInternetToggleRequest: RequestResult;
    FleetSaturationFactor: number;
    CityDispatchableMW: number;
    CapacityHeadroomMW: number;
    GridExportMW: number;
    HeadroomWarningMW: number;
}

export interface ReputationDto {
    TrustLevel: number;
    TrustTier: string;
    IsFrozenOut: boolean;
    OfferFrequencyMult: number;
}

export interface SchemesDto {
    EmergencyFundWithdraw: number;
    EmergencyFundBalance: number;
    FuelSiphonPercent: number;
    CorruptionWindowActive: boolean;
    EmergencyFundAvailability: ActionAvailability;
    FuelSiphonAvailability: ActionAvailability;
    CorruptionSchemeRequest: RequestResult;
}

export interface SettingsDto {
    DifficultyPreset: SettingsDifficultyPreset;
    BasePreset: SettingsDifficultyPreset;
    LegalImportMW: number;
    LegalExportMW: number;
    ConstructionDelay: boolean;
    RandomDisasters: boolean;
    WinterMultiplier: boolean;
    NeighborEnvy: boolean;
    BackupPower: boolean;
    ProtectCriticalInfra: boolean;
    IsExpanded: boolean;
    UiTheme: SettingsTheme;
    TelemetryEnabled: boolean;
    MuteCivicAudio: boolean;
    MuteDroneAudio: boolean;
    MuteAlertAudio: boolean;
    MuteCombatAudio: boolean;
    ErrorCount: number;
    ReportStatus: string;
    ReportStatusKey: string;
    LanguagePreference: SettingsLanguagePreference;
    IsUncensored: boolean;
    AvailableLocales: number[];
    AvailableThemes: number[];
    CrashDumps: CrashDumpEntry[];
    LocaleRequest: RequestResult;
    CanToggleTelemetry: boolean;
    TelemetryLockedReasonId: string;
}

export interface SettingsLocalizationDto {
    CurrentLocale: string;
    LocalizationStrings: Record<string, string>;
    LocaleVersion: number;
}

export interface SpotterDto {
    SpotterCount: number;
    SpotterPenaltyPercent: number;
    SpotterRawPenaltyPercent: number;
    SbuVisitCost: number;
    TotalSBUVisits: number;
    EvacuationCost: number;
    TotalEvacuations: number;
    CounterOSINTActive: boolean;
    CounterOSINTDailyCost: number;
    SpotterActionRequest: RequestResult;
    CanSbuVisit: boolean;
    SbuVisitLockedReasonId: string;
    CanEvacuationRun: boolean;
    EvacuationRunLockedReasonId: string;
    CanToggleCounterOSINT: boolean;
    CounterOSINTLockedReasonId: string;
}

export interface ThreatDto {
    WavePhase: WavePhase;
    WaveNumber: number;
    ThreatsExpected: number;
    ThreatsSpawned: number;
    ThreatsRemaining: number;
    ThreatsIntercepted: number;
    ThreatsHit: number;
    ThreatsCrashed: number;
    TimeInPhase: number;
    PhaseEndTime: number;
    ScenarioStarted: boolean;
    ProducerReady: boolean;
    WaveDataStatus: WaveDataStatus;
    WaitingForLaunchWindow: boolean;
    EarlyWarningMessage: string;
    IntelReportLabel: string;
    NoActiveThreatsLabel: string;
    ThreatTargets: ThreatTargetDto[];
    RadarThreats: RadarThreatDto[];
    RadarTargets: RadarTargetDto[];
    RadarDefenses: RadarDefenseDto[];
    MapBounds: MapBoundsDto;
    IdentifyTrackedEntity: number;
    IdentifyProgress: number;
    IdentifyConfirmed: boolean;
    IdentifyFocusActive: boolean;
    ShowDebriefing: boolean;
    DebriefingWave: number;
    DebriefingIntercepted: number;
    DebriefingHits: number;
    DebriefingShotsFired: number;
    DebriefingCasualties: number;
    DebriefingDamageCost: number;
    DebriefingInfraDamageCost: number;
    DebriefingCrashed: number;
    DebriefingTotalThreats: number;
    DebriefingEfficiency: number;
    RadarInterceptions: RadarInterceptionDto[];
    CameraX: number;
    CameraZ: number;
}

const isRecord = (value: unknown): value is Record<string, unknown> =>
    typeof value === "object" && value !== null && !Array.isArray(value);

const hasNumber = (value: Record<string, unknown>, key: string): boolean => typeof value[key] === "number";
const hasString = (value: Record<string, unknown>, key: string): boolean => typeof value[key] === "string";
const hasBoolean = (value: Record<string, unknown>, key: string): boolean => typeof value[key] === "boolean";
const hasArray = (value: Record<string, unknown>, key: string): boolean => Array.isArray(value[key]);
const hasObject = (value: Record<string, unknown>, key: string): boolean => isRecord(value[key]);
const hasStringRecord = (value: Record<string, unknown>, key: string): boolean => isStringRecord(value[key]);
const hasNullableObject = (value: Record<string, unknown>, key: string): boolean => value[key] === null || isRecord(value[key]);
const hasRequestResult = (value: Record<string, unknown>, key: string): boolean => isRequestResult(value[key]);

type FieldCheck = (value: Record<string, unknown>, key: string) => boolean;

const actionAvailabilityChecks: [string, FieldCheck][] = [
    ["CanRun", hasBoolean],
    ["LockedReasonId", hasString],
    ["EffectiveCost", hasNumber],
];

export function isActionAvailability(value: unknown): value is ActionAvailability {
    return isRecord(value) && actionAvailabilityChecks.every(([key, check]) => check(value, key));
}

const hasActionAvailability = (value: Record<string, unknown>, key: string): boolean => isActionAvailability(value[key]);

const entityRefDtoChecks: [string, FieldCheck][] = [
    ["Index", hasNumber],
    ["Version", hasNumber],
];

export function isEntityRefDto(value: unknown): value is EntityRefDto {
    return isRecord(value) && entityRefDtoChecks.every(([key, check]) => check(value, key));
}

const hasEntityRefDto = (value: Record<string, unknown>, key: string): boolean => isEntityRefDto(value[key]);

const crashDumpEntryChecks: [string, FieldCheck][] = [
    ["Name", hasString],
    ["SizeMb", hasNumber],
    ["TimeText", hasString],
];

export function isCrashDumpEntry(value: unknown): value is CrashDumpEntry {
    return isRecord(value) && crashDumpEntryChecks.every(([key, check]) => check(value, key));
}

const hasCrashDumpEntryArray = (value: Record<string, unknown>, key: string): boolean => {
    const items = value[key];
    return Array.isArray(items) && items.every(isCrashDumpEntry);
};

const civilianDamageDataChecks: [string, FieldCheck][] = [
    ["Building", hasEntityRefDto],
    ["Name", hasString],
    ["HitCount", hasNumber],
    ["MaxHits", hasNumber],
    ["DamagePercent", hasNumber],
    ["IsRepairing", hasBoolean],
    ["RepairHoursLeft", hasNumber],
    ["MunicipalRepairCharge", hasNumber],
    ["MunicipalKickbackRepairCharge", hasNumber],
    ["KickbackRepairAmount", hasNumber],
    ["CanMunicipalRepair", hasBoolean],
    ["MunicipalRepairLockedReasonId", hasString],
    ["CanKickbackRepair", hasBoolean],
    ["KickbackRepairLockedReasonId", hasString],
    ["ShadowOpsRepairCharge", hasNumber],
    ["CanShadowRepair", hasBoolean],
    ["ShadowRepairLockedReasonId", hasString],
];

export function isCivilianDamageData(value: unknown): value is CivilianDamageData {
    return isRecord(value) && civilianDamageDataChecks.every(([key, check]) => check(value, key));
}

const hasCivilianDamageDataArray = (value: Record<string, unknown>, key: string): boolean => {
    const items = value[key];
    return Array.isArray(items) && items.every(isCivilianDamageData);
};

const activeContractEntryChecks: [string, FieldCheck][] = [
    ["EntityIndex", hasNumber],
    ["BuildingName", hasString],
    ["ContractType", hasString],
    ["VendorName", hasString],
    ["Quality", hasNumber],
    ["KickbackAmount", hasNumber],
    ["IsShady", hasBoolean],
    ["DaysRemaining", hasNumber],
];

export function isActiveContractEntry(value: unknown): value is ActiveContractEntry {
    return isRecord(value) && activeContractEntryChecks.every(([key, check]) => check(value, key));
}

const hasActiveContractEntryArray = (value: Record<string, unknown>, key: string): boolean => {
    const items = value[key];
    return Array.isArray(items) && items.every(isActiveContractEntry);
};

const pendingProcurementOfferEntryChecks: [string, FieldCheck][] = [
    ["EntityIndex", hasNumber],
    ["EntityVersion", hasNumber],
    ["Service", hasString],
    ["ContractType", hasString],
    ["OfficialVendorName", hasString],
    ["ShadyVendorName", hasString],
    ["OfficialPrice", hasNumber],
    ["ShadyPrice", hasNumber],
    ["KickbackOffer", hasNumber],
    ["OfficialQuality", hasNumber],
    ["ShadyQuality", hasNumber],
    ["CanAcceptShady", hasBoolean],
    ["AcceptShadyLockedReasonId", hasString],
    ["AcceptShadyEffectiveCost", hasNumber],
    ["BuildingName", hasString],
];

export function isPendingProcurementOfferEntry(value: unknown): value is PendingProcurementOfferEntry {
    return isRecord(value) && pendingProcurementOfferEntryChecks.every(([key, check]) => check(value, key));
}

const plantWearDataChecks: [string, FieldCheck][] = [
    ["PlantId", hasNumber],
    ["Name", hasString],
    ["CapacityMW", hasNumber],
    ["CurrentOutputMW", hasNumber],
    ["WearPercent", hasNumber],
    ["RepairBillablePercent", hasNumber],
    ["IsRepairable", hasBoolean],
    ["IsDestroyed", hasBoolean],
    ["IsRepairing", hasBoolean],
    ["RepairHoursLeft", hasNumber],
    ["HasExploded", hasBoolean],
    ["IsUnderConstruction", hasBoolean],
    ["ConstructionDaysLeft", hasNumber],
    ["OperationalDamagePercent", hasNumber],
    ["OperationalHitCount", hasNumber],
    ["OperationalHitMax", hasNumber],
    ["DisasterDamagePercent", hasNumber],
    ["IsAtRisk", hasBoolean],
    ["MunicipalRepairCharge", hasNumber],
    ["MunicipalKickbackRepairCharge", hasNumber],
    ["KickbackRepairAmount", hasNumber],
    ["CanMunicipalRepair", hasBoolean],
    ["MunicipalRepairLockedReasonId", hasString],
    ["CanKickbackRepair", hasBoolean],
    ["KickbackRepairLockedReasonId", hasString],
    ["ShadowOpsRepairCharge", hasNumber],
    ["CanShadowRepair", hasBoolean],
    ["ShadowRepairLockedReasonId", hasString],
    ["State", hasNumber],
    ["SaturationFactor", hasNumber],
    ["FuelAvailabilityPercent", hasNumber],
    ["FuelFactor", hasNumber],
    ["RecoveryHours", hasNumber],
];

export function isPlantWearData(value: unknown): value is PlantWearData {
    return isRecord(value) && plantWearDataChecks.every(([key, check]) => check(value, key));
}

const hasPlantWearDataArray = (value: Record<string, unknown>, key: string): boolean => {
    const items = value[key];
    return Array.isArray(items) && items.every(isPlantWearData);
};

const mapBoundsDtoChecks: [string, FieldCheck][] = [
    ["MinX", hasNumber],
    ["MaxX", hasNumber],
    ["MinZ", hasNumber],
    ["MaxZ", hasNumber],
];

export function isMapBoundsDto(value: unknown): value is MapBoundsDto {
    return isRecord(value) && mapBoundsDtoChecks.every(([key, check]) => check(value, key));
}

const hasMapBoundsDto = (value: Record<string, unknown>, key: string): boolean => isMapBoundsDto(value[key]);

const radarInterceptionDtoChecks: [string, FieldCheck][] = [
    ["X", hasNumber],
    ["Z", hasNumber],
    ["TimeAgo", hasNumber],
    ["Lifetime", hasNumber],
    ["Success", hasBoolean],
];

export function isRadarInterceptionDto(value: unknown): value is RadarInterceptionDto {
    return isRecord(value) && radarInterceptionDtoChecks.every(([key, check]) => check(value, key));
}

const hasRadarInterceptionDtoArray = (value: Record<string, unknown>, key: string): boolean => {
    const items = value[key];
    return Array.isArray(items) && items.every(isRadarInterceptionDto);
};

const radarTargetDtoChecks: [string, FieldCheck][] = [
    ["Entity", hasEntityRefDto],
    ["X", hasNumber],
    ["Z", hasNumber],
    ["Name", hasString],
    ["SizeX", hasNumber],
    ["SizeY", hasNumber],
    ["SizeZ", hasNumber],
    ["RotationY", hasNumber],
];

export function isRadarTargetDto(value: unknown): value is RadarTargetDto {
    return isRecord(value) && radarTargetDtoChecks.every(([key, check]) => check(value, key));
}

const hasRadarTargetDtoArray = (value: Record<string, unknown>, key: string): boolean => {
    const items = value[key];
    return Array.isArray(items) && items.every(isRadarTargetDto);
};

const radarThreatDtoChecks: [string, FieldCheck][] = [
    ["Entity", hasEntityRefDto],
    ["X", hasNumber],
    ["Z", hasNumber],
    ["Vx", hasNumber],
    ["Vz", hasNumber],
    ["Eta", hasNumber],
    ["Altitude", hasNumber],
    ["Type", hasString],
    ["EvasionStatus", hasString],
    ["IsIdentified", hasBoolean],
];

export function isRadarThreatDto(value: unknown): value is RadarThreatDto {
    return isRecord(value) && radarThreatDtoChecks.every(([key, check]) => check(value, key));
}

const hasRadarThreatDtoArray = (value: Record<string, unknown>, key: string): boolean => {
    const items = value[key];
    return Array.isArray(items) && items.every(isRadarThreatDto);
};

const radarDefenseDtoChecks: [string, FieldCheck][] = [
    ["X", hasNumber],
    ["Z", hasNumber],
    ["Range", hasNumber],
];

export function isRadarDefenseDto(value: unknown): value is RadarDefenseDto {
    return isRecord(value) && radarDefenseDtoChecks.every(([key, check]) => check(value, key));
}

const hasRadarDefenseDtoArray = (value: Record<string, unknown>, key: string): boolean => {
    const items = value[key];
    return Array.isArray(items) && items.every(isRadarDefenseDto);
};

const shadowProgramEntryChecks: [string, FieldCheck][] = [
    ["DistrictIndex", hasNumber],
    ["DistrictName", hasString],
    ["HasProgram", hasBoolean],
    ["Contractor", hasString],
    ["EstimatedCost", hasNumber],
    ["CanModernizeHonest", hasBoolean],
    ["ModernizeHonestLockedReasonId", hasString],
    ["CanModernizeCorrupt", hasBoolean],
    ["ModernizeCorruptLockedReasonId", hasString],
    ["KickbackEarned", hasNumber],
    ["FireCount", hasNumber],
];

export function isShadowProgramEntry(value: unknown): value is ShadowProgramEntry {
    return isRecord(value) && shadowProgramEntryChecks.every(([key, check]) => check(value, key));
}

const hasShadowProgramEntryArray = (value: Record<string, unknown>, key: string): boolean => {
    const items = value[key];
    return Array.isArray(items) && items.every(isShadowProgramEntry);
};

const vector3IntDtoChecks: [string, FieldCheck][] = [
    ["X", hasNumber],
    ["Y", hasNumber],
    ["Z", hasNumber],
];

export function isVector3IntDto(value: unknown): value is Vector3IntDto {
    return isRecord(value) && vector3IntDtoChecks.every(([key, check]) => check(value, key));
}

const hasVector3IntDto = (value: Record<string, unknown>, key: string): boolean => isVector3IntDto(value[key]);

const threatTargetDtoChecks: [string, FieldCheck][] = [
    ["EntityIndex", hasNumber],
    ["EntityVersion", hasNumber],
    ["Name", hasString],
    ["Position", hasVector3IntDto],
    ["ThreatCount", hasNumber],
    ["MinEtaSeconds", hasNumber],
];

export function isThreatTargetDto(value: unknown): value is ThreatTargetDto {
    return isRecord(value) && threatTargetDtoChecks.every(([key, check]) => check(value, key));
}

const hasThreatTargetDtoArray = (value: Record<string, unknown>, key: string): boolean => {
    const items = value[key];
    return Array.isArray(items) && items.every(isThreatTargetDto);
};

const focusRangeDtoChecks: [string, FieldCheck][] = [
    ["Min", hasNumber],
    ["Max", hasNumber],
];

export function isFocusRangeDto(value: unknown): value is FocusRangeDto {
    return isRecord(value) && focusRangeDtoChecks.every(([key, check]) => check(value, key));
}

const hasFocusRangeDto = (value: Record<string, unknown>, key: string): boolean => isFocusRangeDto(value[key]);

const officialTreasuryDtoChecks: [string, FieldCheck][] = [
    ["Balance", hasNumber],
    ["TotalIncome", hasNumber],
    ["TotalExpenses", hasNumber],
];

export function isOfficialTreasuryDto(value: unknown): value is OfficialTreasuryDto {
    return isRecord(value) && officialTreasuryDtoChecks.every(([key, check]) => check(value, key));
}

const hasOfficialTreasuryDto = (value: Record<string, unknown>, key: string): boolean => isOfficialTreasuryDto(value[key]);

const shadowWalletDtoChecks: [string, FieldCheck][] = [
    ["Available", hasNumber],
    ["LockedBalance", hasNumber],
    ["TotalAssets", hasNumber],
    ["ShadowIncome", hasNumber],
    ["ShadowExpenses", hasNumber],
];

export function isShadowWalletDto(value: unknown): value is ShadowWalletDto {
    return isRecord(value) && shadowWalletDtoChecks.every(([key, check]) => check(value, key));
}

const hasShadowWalletDto = (value: Record<string, unknown>, key: string): boolean => isShadowWalletDto(value[key]);

const operationSlotDtoChecks: [string, FieldCheck][] = [
    ["AttackType", hasString],
    ["OperationState", hasString],
    ["Cost", hasNumber],
    ["Progress", hasNumber],
];

export function isOperationSlotDto(value: unknown): value is OperationSlotDto {
    return isRecord(value) && operationSlotDtoChecks.every(([key, check]) => check(value, key));
}

const hasOperationSlotDtoArray = (value: Record<string, unknown>, key: string): boolean => {
    const items = value[key];
    return Array.isArray(items) && items.every(isOperationSlotDto);
};

const attackTimeEstimateDtoChecks: [string, FieldCheck][] = [
    ["Status", hasString],
    ["MinHours", (value, key) => !(key in value) || (hasNumber)(value, key)],
    ["MaxHours", (value, key) => !(key in value) || (hasNumber)(value, key)],
];

export function isAttackTimeEstimateDto(value: unknown): value is AttackTimeEstimateDto {
    return isRecord(value) && attackTimeEstimateDtoChecks.every(([key, check]) => check(value, key));
}

const hasAttackTimeEstimateDto = (value: Record<string, unknown>, key: string): boolean => isAttackTimeEstimateDto(value[key]);

const cognitiveDistrictEntryChecks: [string, FieldCheck][] = [
    ["DistrictIndex", hasNumber],
    ["Name", hasString],
    ["Integrity", hasNumber],
    ["HasInternet", hasBoolean],
    ["IsCompromised", hasBoolean],
    ["IsUnzoned", hasBoolean],
];

export function isCognitiveDistrictEntry(value: unknown): value is CognitiveDistrictEntry {
    return isRecord(value) && cognitiveDistrictEntryChecks.every(([key, check]) => check(value, key));
}

const newsPostDtoChecks: [string, FieldCheck][] = [
    ["PostId", hasString],
    ["Source", hasString],
    ["Title", hasString],
    ["Body", hasString],
    ["Mood", hasString],
    ["Timestamp", hasNumber],
    ["Category", hasString],
    ["Scope", hasString],
    ["IsAiGenerated", hasBoolean],
];

export function isNewsPostDto(value: unknown): value is NewsPostDto {
    return isRecord(value) && newsPostDtoChecks.every(([key, check]) => check(value, key));
}

const socialPostDtoChecks: [string, FieldCheck][] = [
    ["Author", hasString],
    ["AuthorName", hasString],
    ["Message", hasString],
    ["Mood", hasString],
    ["Timestamp", hasNumber],
    ["IsOfficial", hasBoolean],
];

export function isSocialPostDto(value: unknown): value is SocialPostDto {
    return isRecord(value) && socialPostDtoChecks.every(([key, check]) => check(value, key));
}

const toastDataDtoChecks: [string, FieldCheck][] = [
    ["Id", hasNumber],
    ["Type", hasString],
    ["Priority", hasNumber],
    ["Title", hasString],
    ["Message", hasString],
    ["AcceptLabel", hasString],
    ["RejectLabel", hasString],
    ["RemainingSeconds", hasNumber],
    ["Progress", hasNumber],
    ["ContextData", hasNumber],
];

export function isToastDataDto(value: unknown): value is ToastDataDto {
    return isRecord(value) && toastDataDtoChecks.every(([key, check]) => check(value, key));
}

const rankTierDtoChecks: [string, FieldCheck][] = [
    ["Name", hasString],
    ["MinFloorHits", hasNumber],
    ["Icon", hasString],
];

export function isRankTierDto(value: unknown): value is RankTierDto {
    return isRecord(value) && rankTierDtoChecks.every(([key, check]) => check(value, key));
}

const leaderboardEntryDtoChecks: [string, FieldCheck][] = [
    ["Position", hasNumber],
    ["Nickname", hasString],
    ["FloorHits", hasNumber],
    ["TotalDamage", hasNumber],
    ["BestStreak", hasNumber],
    ["RankTier", hasString],
];

export function isLeaderboardEntryDto(value: unknown): value is LeaderboardEntryDto {
    return isRecord(value) && leaderboardEntryDtoChecks.every(([key, check]) => check(value, key));
}

const weeklyLeaderboardEntryDtoChecks: [string, FieldCheck][] = [
    ["Position", hasNumber],
    ["Nickname", hasString],
    ["FloorHits", hasNumber],
    ["DamageDealt", hasNumber],
];

export function isWeeklyLeaderboardEntryDto(value: unknown): value is WeeklyLeaderboardEntryDto {
    return isRecord(value) && weeklyLeaderboardEntryDtoChecks.every(([key, check]) => check(value, key));
}

const airDefenseDtoChecks: [string, FieldCheck][] = [
    ["AaAmmo", hasNumber],
    ["AaMaxAmmo", hasNumber],
    ["AaStations", hasNumber],
    ["SirenActive", hasBoolean],
    ["PatriotAmmo", hasNumber],
    ["PatriotMaxAmmo", hasNumber],
    ["PatriotResupplyCost", hasNumber],
    ["BoforsAmmo", hasNumber],
    ["BoforsMaxAmmo", hasNumber],
    ["HeritageAmmo", hasNumber],
    ["HeritageMaxAmmo", hasNumber],
    ["GepardAmmo", hasNumber],
    ["GepardMaxAmmo", hasNumber],
    ["GunsResupplyCost", hasNumber],
    ["HeritageCredits", hasNumber],
    ["HeritageCreditsMax", hasNumber],
    ["HeritageCrew", hasNumber],
    ["BoforsCrew", hasNumber],
    ["GepardCrew", hasNumber],
    ["HeritageBoforsCount", hasNumber],
    ["BoforsCount", hasNumber],
    ["GepardCount", hasNumber],
    ["PatriotCount", hasNumber],
    ["BoforsPrice", hasNumber],
    ["PaidBoforsAffordableCount", hasNumber],
    ["PaidGepardAffordableCount", hasNumber],
    ["PaidPatriotAffordableCount", hasNumber],
    ["GepardPrice", hasNumber],
    ["PatriotPrice", hasNumber],
    ["PatriotCrew", hasNumber],
    ["PatriotInterceptsDrones", hasBoolean],
    ["AutoResupplyEnabled", hasBoolean],
    ["DefensePolicyName", hasString],
    ["DefensePolicyId", hasNumber],
    ["SpotterPenaltyPercent", hasNumber],
    ["DonorPatriotCredits", hasNumber],
    ["EmergencyResupplyRequest", hasRequestResult],
    ["DefensePolicyRequest", hasRequestResult],
    ["PatriotDroneToggleRequest", hasRequestResult],
    ["AirDefensePlacementRequest", hasRequestResult],
    ["CanResupplyPatriot", hasBoolean],
    ["ResupplyPatriotLockedReasonId", hasString],
    ["CanResupplyGuns", hasBoolean],
    ["ResupplyGunsLockedReasonId", hasString],
    ["CanPlaceHeritageBofors", hasBoolean],
    ["HeritageBoforsLockedReasonId", hasString],
    ["CanPlaceDonorPatriot", hasBoolean],
    ["DonorPatriotLockedReasonId", hasString],
    ["CanPlacePaidBofors", hasBoolean],
    ["PaidBoforsLockedReasonId", hasString],
    ["CanPlacePaidGepard", hasBoolean],
    ["PaidGepardLockedReasonId", hasString],
    ["CanPlacePaidPatriot", hasBoolean],
    ["PaidPatriotLockedReasonId", hasString],
];

export function isAirDefenseDto(value: unknown): value is AirDefenseDto {
    return isRecord(value) && airDefenseDtoChecks.every(([key, check]) => check(value, key));
}

const arrestedModalPayloadDtoChecks: [string, FieldCheck][] = [
    ["ChargesCount", hasNumber],
    ["AssetsSeizedSnapshot", hasNumber],
    ["WalletBalanceAfter", hasNumber],
    ["LastChoiceResult", hasString],
];

export function isArrestedModalPayloadDto(value: unknown): value is ArrestedModalPayloadDto {
    return isRecord(value) && arrestedModalPayloadDtoChecks.every(([key, check]) => check(value, key));
}

const attentionDtoChecks: [string, FieldCheck][] = [
    ["ShockLevel", hasNumber],
    ["ShockTier", hasString],
    ["CasualtiesThisWeek", hasNumber],
    ["BuildingsDestroyedThisWeek", hasNumber],
    ["CriticalHitsThisWeek", hasNumber],
    ["TotalCasualties", hasNumber],
    ["TotalBuildingsDestroyed", hasNumber],
    ["TotalCivilianBuildingsDestroyed", hasNumber],
    ["TotalCriticalHits", hasNumber],
    ["ExodusActive", hasBoolean],
    ["BaseExodusRatePercentPerDay", hasNumber],
    ["ExodusRatePercentPerDay", hasNumber],
    ["TotalExodus", hasNumber],
];

export function isAttentionDto(value: unknown): value is AttentionDto {
    return isRecord(value) && attentionDtoChecks.every(([key, check]) => check(value, key));
}

const backupPowerDtoChecks: [string, FieldCheck][] = [
    ["BackupCharge", hasNumber],
    ["GeneratorsRunning", hasNumber],
    ["NoiseLevel", hasNumber],
    ["ProtectedBuildings", hasNumber],
    ["BackupCapacity", hasNumber],
    ["DischargingCount", hasNumber],
    ["ShadowProgramsJson", hasShadowProgramEntryArray],
    ["ProcurementCooldown", hasNumber],
    ["BackupPolicy", hasNumber],
    ["HospitalsPowered", hasNumber],
    ["HospitalsTotal", hasNumber],
    ["SchoolsPowered", hasNumber],
    ["SchoolsTotal", hasNumber],
    ["ModernizationRequest", hasRequestResult],
    ["BackupPolicyRequest", hasRequestResult],
    ["CanSetBackupPolicy", hasBoolean],
    ["SetBackupPolicyLockedReasonId", hasString],
];

export function isBackupPowerDto(value: unknown): value is BackupPowerDto {
    return isRecord(value) && backupPowerDtoChecks.every(([key, check]) => check(value, key));
}

const buckwheatDtoChecks: [string, FieldCheck][] = [
    ["BuckwheatTons", hasNumber],
    ["ProcurementLevel", hasNumber],
    ["DailyCost", hasNumber],
    ["BaseDailyCost", hasNumber],
    ["LastDistributeResult", hasRequestResult],
    ["ProcurementLevelRequest", hasRequestResult],
    ["CanDistribute", hasBoolean],
    ["DistributeLockedReasonId", hasString],
    ["CanAffordProcurement", hasBoolean],
    ["AffordProcurementLockedReasonId", hasString],
    ["CanSetProcurement25", hasBoolean],
    ["Procurement25LockedReasonId", hasString],
    ["CanSetProcurement50", hasBoolean],
    ["Procurement50LockedReasonId", hasString],
    ["CanSetProcurement75", hasBoolean],
    ["Procurement75LockedReasonId", hasString],
    ["CanSetProcurement100", hasBoolean],
    ["Procurement100LockedReasonId", hasString],
];

export function isBuckwheatDto(value: unknown): value is BuckwheatDto {
    return isRecord(value) && buckwheatDtoChecks.every(([key, check]) => check(value, key));
}

const crisisSweepDtoChecks: [string, FieldCheck][] = [
    ["Mode", hasNumber],
    ["HasResult", hasBoolean],
    ["ComputedAtGameHours", hasNumber],
    ["ArchetypeId", hasNumber],
    ["PopulationPeak", hasNumber],
    ["WarDay", hasNumber],
    ["WorstCaseRecoveryBallisticOnly", hasNumber],
    ["WorstCaseRecoveryMixed", hasNumber],
    ["IsRecoverableBallisticOnly", hasBoolean],
    ["IsRecoverableMixed", hasBoolean],
    ["GraceWindowHours", hasNumber],
    ["DroneInterceptBallisticOnly", hasNumber],
    ["DroneInterceptMixed", hasNumber],
    ["FreeHeritageGrant", hasNumber],
    ["OperationalAaAtVerdict", hasNumber],
    ["ManpowerTotal", hasNumber],
    ["ManpowerUsed", hasNumber],
    ["ManpowerCasualties", hasNumber],
    ["ManpowerAvailable", hasNumber],
    ["AaHeritage", hasNumber],
    ["AaBofors", hasNumber],
    ["AaGepard", hasNumber],
    ["AaPatriot", hasNumber],
    ["CoveragePct", hasNumber],
    ["AreaKm2", hasNumber],
    ["BallisticInterceptBallisticOnly", hasNumber],
    ["BallisticInterceptMixed", hasNumber],
    ["BallisticTargets", hasNumber],
    ["MissilesSpentOnDrones", hasNumber],
    ["PatriotInterceptsDrones", hasBoolean],
    ["CalmHours", hasNumber],
    ["WavePressureAtPeak", hasNumber],
    ["SampleCount", hasNumber],
    ["BlackoutProbabilityPct", hasNumber],
    ["MedianCollapseDay", hasNumber],
    ["UnsheddableFloorMW", hasNumber],
    ["RepairSlots", hasNumber],
    ["RepairFundingCash", hasNumber],
    ["RepairTier", hasNumber],
    ["RepairBudgetLive", hasBoolean],
];

export function isCrisisSweepDto(value: unknown): value is CrisisSweepDto {
    return isRecord(value) && crisisSweepDtoChecks.every(([key, check]) => check(value, key));
}

const districtDtoChecks: [string, FieldCheck][] = [
    ["EntityIndex", hasNumber],
    ["EntityVersion", hasNumber],
    ["Name", hasString],
    ["IsUnzoned", hasBoolean],
    ["ResidentialOff", hasBoolean],
    ["CommercialOff", hasBoolean],
    ["IndustrialOff", hasBoolean],
    ["OfficeOff", hasBoolean],
    ["ServicesOff", hasBoolean],
    ["Schedule", hasNumber],
    ["ScheduleName", hasString],
    ["ScheduleActive", hasBoolean],
    ["TotalMW", hasNumber],
    ["ResidentialMW", hasNumber],
    ["CommercialMW", hasNumber],
    ["IndustrialMW", hasNumber],
    ["OfficeMW", hasNumber],
    ["ServicesMW", hasNumber],
    ["Priority", hasNumber],
    ["DeliveredMW", hasNumber],
    ["ThresholdCutMW", hasNumber],
    ["IsVIP", hasBoolean],
    ["IsVIPBypass", hasBoolean],
    ["IsAutoShedded", hasBoolean],
    ["InternetDisabled", hasBoolean],
    ["ThresholdCutBuildings", hasNumber],
    ["TotalHappinessPenalty", hasNumber],
    ["TotalCommercePenalty", hasNumber],
    ["BlackoutSource", hasString],
];

export function isDistrictDto(value: unknown): value is DistrictDto {
    return isRecord(value) && districtDtoChecks.every(([key, check]) => check(value, key));
}

const cognitiveDtoChecks: [string, FieldCheck][] = [
    ["CognitiveActive", hasBoolean],
    ["InfectionRate", hasNumber],
    ["RecoveryRate", hasNumber],
    ["PenaltyThreshold", hasNumber],
    ["TotalDistricts", hasNumber],
    ["CompromisedDistricts", hasNumber],
    ["HeroStatus", hasNumber],
    ["HeroDeployCost", hasNumber],
    ["HeroInfectionReduction", hasNumber],
    ["HeroRecoveryBonus", hasNumber],
    ["HeroActionRequest", hasRequestResult],
    ["ProtestRisk", hasNumber],
    ["DominantNarrative", hasString],
    ["AvgIntegrity", hasNumber],
    ["TotalHouseholds", hasNumber],
    ["AvgInfection", hasNumber],
    ["AvgResistance", hasNumber],
    ["AvgTrauma", hasNumber],
    ["HouseholdsUnderBlackout", hasNumber],
    ["HouseholdsWithEnvy", hasNumber],
    ["HouseholdsUnderImpact", hasNumber],
    ["HouseholdsInfected", hasNumber],
    ["VulnerableHouseholds", hasNumber],
    ["AvgBlackoutHours", hasNumber],
    ["BlackoutVulnerability", hasNumber],
    ["InternetMode", hasNumber],
    ["CommercePenalty", hasNumber],
    ["InternetModeRequest", hasRequestResult],
    ["IpsoActive", hasBoolean],
    ["IpsoIntensity", hasNumber],
    ["IpsoDistrictCount", hasNumber],
    ["IpsoTotalDistricts", hasNumber],
    ["TelemarathonActive", hasBoolean],
    ["NarrativeMode", hasNumber],
    ["MediaTrust", hasNumber],
    ["IsInShock", hasBoolean],
    ["ShockHoursRemaining", hasNumber],
    ["AudienceFatigue", hasNumber],
    ["TelemarathonModeRequest", hasRequestResult],
    ["TelemarathonActiveRequest", hasRequestResult],
    ["CanDeployHero", hasBoolean],
    ["DeployHeroLockedReasonId", hasString],
    ["CanRecallHero", hasBoolean],
    ["RecallHeroLockedReasonId", hasString],
    ["CanSetHeroCounter", hasBoolean],
    ["SetHeroCounterLockedReasonId", hasString],
    ["CanSetHeroLecturing", hasBoolean],
    ["SetHeroLecturingLockedReasonId", hasString],
];

export function isCognitiveDto(value: unknown): value is CognitiveDto {
    return isRecord(value) && cognitiveDtoChecks.every(([key, check]) => check(value, key));
}

const countermeasuresDtoChecks: [string, FieldCheck][] = [
    ["CorruptionScore", hasNumber],
    ["Heat", hasNumber],
    ["HeatLevel", hasString],
    ["CountermeasuresPhase", hasString],
    ["InvestigationProgress", hasNumber],
    ["ChargesCount", hasNumber],
    ["ProtestCount", hasNumber],
    ["ChoiceRequired", hasBoolean],
    ["ChoiceType", hasNumber],
    ["BribeCost", hasNumber],
    ["BaseBribeCost", hasNumber],
    ["BribeAvailability", hasActionAvailability],
    ["LastChoiceResult", hasString],
    ["CurrentJournalist", hasString],
    ["IsArrested", hasBoolean],
    ["ArrestedAssetsSeized", hasNumber],
    ["ArrestedWalletAfter", hasNumber],
    ["BribeRiskWarning", hasString],
    ["SanctionsSuppressingCorruption", hasBoolean],
    ["LastChoiceRequestResult", hasRequestResult],
];

export function isCountermeasuresDto(value: unknown): value is CountermeasuresDto {
    return isRecord(value) && countermeasuresDtoChecks.every(([key, check]) => check(value, key));
}

const donorDtoChecks: [string, FieldCheck][] = [
    ["DonorUsesRemaining", hasNumber],
    ["DonorCooldownDays", hasNumber],
    ["DonorStatus", hasString],
    ["TrustIndex", hasNumber],
    ["ScandalPenalty", hasNumber],
    ["DonorExpectedAid", hasString],
    ["DonorDialogActive", hasBoolean],
    ["ProducerReady", hasBoolean],
    ["TrustLocked", hasBoolean],
    ["ProducerReasonId", hasString],
    ["DonorFundsAmount", hasNumber],
    ["DonorGeneratorCount", hasNumber],
    ["DonorGeneratorMW", hasNumber],
    ["DonorPatriotDays", hasNumber],
    ["AidTierId", hasNumber],
    ["AidFundsOffered", hasNumber],
    ["AidFundsAccessible", hasNumber],
    ["PatriotOffered", hasBoolean],
    ["PatriotBlocked", hasBoolean],
    ["TrustMessageId", hasNumber],
    ["BlockedReasonId", hasNumber],
    ["HasBlockedItems", hasBoolean],
    ["DonorActiveGenerators", hasNumber],
    ["SanctionsActive", hasBoolean],
    ["SanctionDaysRemaining", hasNumber],
    ["SanctionTradePenalty", hasNumber],
    ["DonorDialogRequest", hasRequestResult],
    ["DonorSelectionRequest", hasRequestResult],
    ["DonorFundsAvailable", hasBoolean],
    ["DonorFundsLockedReasonId", hasString],
    ["DonorPowerAvailable", hasBoolean],
    ["DonorPowerLockedReasonId", hasString],
    ["DonorDefenseAvailable", hasBoolean],
    ["DonorDefenseLockedReasonId", hasString],
];

export function isDonorDto(value: unknown): value is DonorDto {
    return isRecord(value) && donorDtoChecks.every(([key, check]) => check(value, key));
}

const exportDtoChecks: [string, FieldCheck][] = [
    ["ExportPercent", hasNumber],
    ["ExportedMW", hasNumber],
    ["DailyIncome", hasNumber],
    ["OffshoreBalance", hasNumber],
    ["IsFrozen", hasBoolean],
    ["FreezeReason", hasNumber],
    ["ExportAvailability", hasActionAvailability],
    ["ShadowTradeExportRequest", hasRequestResult],
];

export function isExportDto(value: unknown): value is ExportDto {
    return isRecord(value) && exportDtoChecks.every(([key, check]) => check(value, key));
}

const financeDtoChecks: [string, FieldCheck][] = [
    ["CityTreasury", hasNumber],
    ["TotalLiquidity", hasNumber],
    ["OfficialTreasury", hasOfficialTreasuryDto],
    ["ShadowWallet", hasShadowWalletDto],
    ["Expenses", hasObject],
    ["Income", hasObject],
    ["TotalExpenses", hasNumber],
    ["TotalIncome", hasNumber],
    ["TotalDebt", hasNumber],
    ["DebtBreakdown", hasObject],
    ["DebtWarning", hasBoolean],
    ["DebtRestructured", hasBoolean],
    ["SanctionsMarkup", hasNumber],
];

export function isFinanceDto(value: unknown): value is FinanceDto {
    return isRecord(value) && financeDtoChecks.every(([key, check]) => check(value, key));
}

const gridWarfareDtoChecks: [string, FieldCheck][] = [
    ["ShadowBalance", hasNumber],
    ["ShadowLocked", hasNumber],
    ["ShadowTotal", hasNumber],
    ["EnemyPhysicalAxis", hasNumber],
    ["EnemyDigitalAxis", hasNumber],
    ["EnemySocialAxis", hasNumber],
    ["EnemyInterceptChance", hasNumber],
    ["DroneStock", hasNumber],
    ["BallisticStock", hasNumber],
    ["RespitePhysicalActive", hasBoolean],
    ["RespiteDigitalActive", hasBoolean],
    ["RespiteSocialActive", hasBoolean],
    ["ObjectiveProgress", hasNumber],
    ["CityStability", hasNumber],
    ["StabilityDiscount", hasNumber],
    ["OperationSlots", hasOperationSlotDtoArray],
    ["AttackCosts", hasObject],
    ["OperationRequest", hasRequestResult],
    ["GridWarfareUnlocked", hasBoolean],
    ["CanPrepareDrone", hasBoolean],
    ["PrepareDroneLockedReasonId", hasString],
    ["CanPrepareBlackout", hasBoolean],
    ["PrepareBlackoutLockedReasonId", hasString],
    ["CanPrepareDisinfo", hasBoolean],
    ["PrepareDisinfoLockedReasonId", hasString],
];

export function isGridWarfareDto(value: unknown): value is GridWarfareDto {
    return isRecord(value) && gridWarfareDtoChecks.every(([key, check]) => check(value, key));
}

const importDtoChecks: [string, FieldCheck][] = [
    ["ShadowImportMW", hasNumber],
    ["MaxShadowImportMW", hasNumber],
    ["SelectedPresetIndex", hasNumber],
    ["ShadowImportCost", hasNumber],
    ["DiscoveryRisk", hasNumber],
    ["ShadowImportDaysActive", hasNumber],
    ["IsSanctioned", hasBoolean],
    ["ShadowImportSanctionDays", hasNumber],
    ["ShadowImportAvailability", hasActionAvailability],
    ["IsFrozen", hasBoolean],
    ["FreezeReason", hasNumber],
    ["ShadowTradeImportRequest", hasRequestResult],
];

export function isImportDto(value: unknown): value is ImportDto {
    return isRecord(value) && importDtoChecks.every(([key, check]) => check(value, key));
}

const intelDtoChecks: [string, FieldCheck][] = [
    ["TensionLevel", hasNumber],
    ["TensionStatus", hasString],
    ["WaveTypePrediction", hasString],
    ["IsMassiveStrike", hasBoolean],
    ["EnergyFocusRange", hasFocusRangeDto],
    ["InfraFocusRange", hasFocusRangeDto],
    ["ResidentialFocusRange", hasFocusRangeDto],
    ["TimeEstimate", hasAttackTimeEstimateDto],
    ["ThreatComposition", hasString],
    ["EstimatedShaheds", hasNumber],
    ["EstimatedBallistics", hasNumber],
    ["HasInsider", hasBoolean],
    ["InsiderCost", hasNumber],
    ["BaseInsiderCost", hasNumber],
    ["TensionPriceMultiplier", hasNumber],
    ["TensionPriceModifierPercent", hasNumber],
    ["InsiderRequest", hasRequestResult],
    ["IntelUpgradeLevel", hasNumber],
    ["IntelUpgradeCost", hasNumber],
    ["IntelUpgradeRequest", hasRequestResult],
    ["CanBuyInsider", hasBoolean],
    ["InsiderLockedReasonId", hasString],
    ["CanUpgradeIntel", hasBoolean],
    ["IntelUpgradeLockedReasonId", hasString],
];

export function isIntelDto(value: unknown): value is IntelDto {
    return isRecord(value) && intelDtoChecks.every(([key, check]) => check(value, key));
}

const maintenanceDtoChecks: [string, FieldCheck][] = [
    ["PendingProcurementOffer", hasNullableObject],
    ["ShadyContractCount", hasNumber],
    ["TotalContractCount", hasNumber],
    ["ActiveContractsJson", hasActiveContractEntryArray],
    ["MaintenanceContractRequest", hasRequestResult],
];

export function isMaintenanceDto(value: unknown): value is MaintenanceDto {
    return isRecord(value) && maintenanceDtoChecks.every(([key, check]) => check(value, key));
}

const modalSnapshotDtoChecks: [string, FieldCheck][] = [
    ["ActiveId", hasString],
    ["ActivePriority", hasNumber],
    ["ActiveData", hasNullableObject],
    ["Queue", hasArray],
    ["Version", hasNumber],
];

export function isModalSnapshotDto(value: unknown): value is ModalSnapshotDto {
    return isRecord(value) && modalSnapshotDtoChecks.every(([key, check]) => check(value, key));
}

const mobilizationDtoChecks: [string, FieldCheck][] = [
    ["ManpowerAvailable", hasNumber],
    ["ManpowerUsed", hasNumber],
    ["ManpowerTotal", hasNumber],
    ["ManpowerPercent", hasNumber],
    ["ManpowerBasePool", hasNumber],
    ["ManpowerCasualties", hasNumber],
    ["ManpowerPatriotismFactor", hasNumber],
    ["ManpowerMoraleFactor", hasNumber],
    ["ManpowerFatigueFactor", hasNumber],
    ["IsConscriptionActive", hasBoolean],
    ["IsWarFatigued", hasBoolean],
    ["IsManpowerCritical", hasBoolean],
    ["IsManpowerOvercommitted", hasBoolean],
    ["CallToArmsOnCooldown", hasBoolean],
    ["ConscriptionReactivationOnCooldown", hasBoolean],
    ["PredictedConscriptionRelease", hasNumber],
    ["SocialPenaltyProducerReady", hasBoolean],
    ["SocialPenaltyReasonId", hasString],
    ["WarDay", hasNumber],
    ["CallToArmsRequest", hasRequestResult],
    ["ConscriptionToggleRequest", hasRequestResult],
    ["CanCallToArms", hasBoolean],
    ["CallToArmsLockedReasonId", hasString],
    ["CanToggleConscription", hasBoolean],
    ["ConscriptionLockedReasonId", hasString],
];

export function isMobilizationDto(value: unknown): value is MobilizationDto {
    return isRecord(value) && mobilizationDtoChecks.every(([key, check]) => check(value, key));
}

const newsDtoChecks: [string, FieldCheck][] = [
    ["GlobalOnlineNow", hasNumber],
    ["GlobalOnlineHour", hasNumber],
    ["GlobalOnlineToday", hasNumber],
    ["GlobalOnlineTotal", hasNumber],
    ["GlobalConnected", hasBoolean],
    ["GlobalConnectionStatus", hasString],
    ["NetworkConnectionEnabled", hasBoolean],
    ["PlayerNickname", hasString],
    ["NicknameRequest", hasRequestResult],
    ["NicknameChangesRemaining", hasNumber],
    ["NicknameInitialized", hasBoolean],
    ["OnlineConsentRecorded", hasBoolean],
];

export function isNewsDto(value: unknown): value is NewsDto {
    return isRecord(value) && newsDtoChecks.every(([key, check]) => check(value, key));
}

const powerGridDtoChecks: [string, FieldCheck][] = [
    ["GridStatus", hasString],
    ["Production", hasNumber],
    ["Demand", hasNumber],
    ["Consumption", hasNumber],
    ["GameHour", hasNumber],
    ["GridFrequency", hasNumber],
    ["StressZone", hasString],
    ["StressPercent", hasNumber],
    ["RecoveryHours", hasNumber],
    ["CollapseThresholdHours", hasNumber],
    ["ThresholdActive", hasBoolean],
    ["BuildingsCutCount", hasNumber],
    ["DeliveredMW", hasNumber],
    ["ForcedOffMW", hasNumber],
    ["AutoCutMW", hasNumber],
    ["DistrictShedMW", hasNumber],
    ["AutoDispatchShedMW", hasNumber],
    ["CitySchedule", hasNumber],
    ["EffectiveCityMode", hasNumber],
    ["DistrictsOverrideCity", hasBoolean],
    ["CityScheduleAvailability", hasActionAvailability],
    ["AutoDispatchEnabled", hasBoolean],
    ["AutoDispatchSheddedCount", hasNumber],
    ["AutoDispatchBlockedByVip", hasBoolean],
    ["ShadowBalance", hasNumber],
    ["AtRiskPlantCount", hasNumber],
    ["GenerationSources", hasPlantWearDataArray],
    ["CivilianDamage", hasCivilianDamageDataArray],
    ["PlantMunicipalRepairHours", hasNumber],
    ["PlantShadowOpsRepairHours", hasNumber],
    ["CivilianMunicipalRepairHours", hasNumber],
    ["CivilianShadowOpsRepairHours", hasNumber],
    ["PlantRepairRequest", hasRequestResult],
    ["CivilianRepairRequest", hasRequestResult],
    ["AutoDispatchToggleRequest", hasRequestResult],
    ["DistrictToggleRequest", hasRequestResult],
    ["CitySchedulePeriodRequest", hasRequestResult],
    ["DistrictInternetToggleRequest", hasRequestResult],
    ["FleetSaturationFactor", hasNumber],
    ["CityDispatchableMW", hasNumber],
    ["CapacityHeadroomMW", hasNumber],
    ["GridExportMW", hasNumber],
    ["HeadroomWarningMW", hasNumber],
];

export function isPowerGridDto(value: unknown): value is PowerGridDto {
    return isRecord(value) && powerGridDtoChecks.every(([key, check]) => check(value, key));
}

const reputationDtoChecks: [string, FieldCheck][] = [
    ["TrustLevel", hasNumber],
    ["TrustTier", hasString],
    ["IsFrozenOut", hasBoolean],
    ["OfferFrequencyMult", hasNumber],
];

export function isReputationDto(value: unknown): value is ReputationDto {
    return isRecord(value) && reputationDtoChecks.every(([key, check]) => check(value, key));
}

const schemesDtoChecks: [string, FieldCheck][] = [
    ["EmergencyFundWithdraw", hasNumber],
    ["EmergencyFundBalance", hasNumber],
    ["FuelSiphonPercent", hasNumber],
    ["CorruptionWindowActive", hasBoolean],
    ["EmergencyFundAvailability", hasActionAvailability],
    ["FuelSiphonAvailability", hasActionAvailability],
    ["CorruptionSchemeRequest", hasRequestResult],
];

export function isSchemesDto(value: unknown): value is SchemesDto {
    return isRecord(value) && schemesDtoChecks.every(([key, check]) => check(value, key));
}

const settingsDtoChecks: [string, FieldCheck][] = [
    ["DifficultyPreset", hasNumber],
    ["BasePreset", hasNumber],
    ["LegalImportMW", hasNumber],
    ["LegalExportMW", hasNumber],
    ["ConstructionDelay", hasBoolean],
    ["RandomDisasters", hasBoolean],
    ["WinterMultiplier", hasBoolean],
    ["NeighborEnvy", hasBoolean],
    ["BackupPower", hasBoolean],
    ["ProtectCriticalInfra", hasBoolean],
    ["IsExpanded", hasBoolean],
    ["UiTheme", hasNumber],
    ["TelemetryEnabled", hasBoolean],
    ["MuteCivicAudio", hasBoolean],
    ["MuteDroneAudio", hasBoolean],
    ["MuteAlertAudio", hasBoolean],
    ["MuteCombatAudio", hasBoolean],
    ["ErrorCount", hasNumber],
    ["ReportStatus", hasString],
    ["ReportStatusKey", hasString],
    ["LanguagePreference", hasNumber],
    ["IsUncensored", hasBoolean],
    ["AvailableLocales", hasArray],
    ["AvailableThemes", hasArray],
    ["CrashDumps", hasCrashDumpEntryArray],
    ["LocaleRequest", hasRequestResult],
    ["CanToggleTelemetry", hasBoolean],
    ["TelemetryLockedReasonId", hasString],
];

export function isSettingsDto(value: unknown): value is SettingsDto {
    return isRecord(value) && settingsDtoChecks.every(([key, check]) => check(value, key));
}

const settingsLocalizationDtoChecks: [string, FieldCheck][] = [
    ["CurrentLocale", hasString],
    ["LocalizationStrings", hasStringRecord],
    ["LocaleVersion", hasNumber],
];

export function isSettingsLocalizationDto(value: unknown): value is SettingsLocalizationDto {
    return isRecord(value) && settingsLocalizationDtoChecks.every(([key, check]) => check(value, key));
}

const spotterDtoChecks: [string, FieldCheck][] = [
    ["SpotterCount", hasNumber],
    ["SpotterPenaltyPercent", hasNumber],
    ["SpotterRawPenaltyPercent", hasNumber],
    ["SbuVisitCost", hasNumber],
    ["TotalSBUVisits", hasNumber],
    ["EvacuationCost", hasNumber],
    ["TotalEvacuations", hasNumber],
    ["CounterOSINTActive", hasBoolean],
    ["CounterOSINTDailyCost", hasNumber],
    ["SpotterActionRequest", hasRequestResult],
    ["CanSbuVisit", hasBoolean],
    ["SbuVisitLockedReasonId", hasString],
    ["CanEvacuationRun", hasBoolean],
    ["EvacuationRunLockedReasonId", hasString],
    ["CanToggleCounterOSINT", hasBoolean],
    ["CounterOSINTLockedReasonId", hasString],
];

export function isSpotterDto(value: unknown): value is SpotterDto {
    return isRecord(value) && spotterDtoChecks.every(([key, check]) => check(value, key));
}

const threatDtoChecks: [string, FieldCheck][] = [
    ["WavePhase", (value, key) => isWavePhase(value[key])],
    ["WaveNumber", hasNumber],
    ["ThreatsExpected", hasNumber],
    ["ThreatsSpawned", hasNumber],
    ["ThreatsRemaining", hasNumber],
    ["ThreatsIntercepted", hasNumber],
    ["ThreatsHit", hasNumber],
    ["ThreatsCrashed", hasNumber],
    ["TimeInPhase", hasNumber],
    ["PhaseEndTime", hasNumber],
    ["ScenarioStarted", hasBoolean],
    ["ProducerReady", hasBoolean],
    ["WaveDataStatus", (value, key) => isWaveDataStatus(value[key])],
    ["WaitingForLaunchWindow", hasBoolean],
    ["EarlyWarningMessage", hasString],
    ["IntelReportLabel", hasString],
    ["NoActiveThreatsLabel", hasString],
    ["ThreatTargets", hasThreatTargetDtoArray],
    ["RadarThreats", hasRadarThreatDtoArray],
    ["RadarTargets", hasRadarTargetDtoArray],
    ["RadarDefenses", hasRadarDefenseDtoArray],
    ["MapBounds", hasMapBoundsDto],
    ["IdentifyTrackedEntity", hasNumber],
    ["IdentifyProgress", hasNumber],
    ["IdentifyConfirmed", hasBoolean],
    ["IdentifyFocusActive", hasBoolean],
    ["ShowDebriefing", hasBoolean],
    ["DebriefingWave", hasNumber],
    ["DebriefingIntercepted", hasNumber],
    ["DebriefingHits", hasNumber],
    ["DebriefingShotsFired", hasNumber],
    ["DebriefingCasualties", hasNumber],
    ["DebriefingDamageCost", hasNumber],
    ["DebriefingInfraDamageCost", hasNumber],
    ["DebriefingCrashed", hasNumber],
    ["DebriefingTotalThreats", hasNumber],
    ["DebriefingEfficiency", hasNumber],
    ["RadarInterceptions", hasRadarInterceptionDtoArray],
    ["CameraX", hasNumber],
    ["CameraZ", hasNumber],
];

export function isThreatDto(value: unknown): value is ThreatDto {
    return isRecord(value) && threatDtoChecks.every(([key, check]) => check(value, key));
}
