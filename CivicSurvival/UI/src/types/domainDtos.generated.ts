// GENERATED - DO NOT EDIT
// Source:           Docs/Contracts/ui-dto.contract.yaml
// SourceHash:       sha256:35dbbf520b380de18559719c3b738ab5fde5b12ea764031ad7de2a095b96b8d2
// Generator:        scripts/generators/ui_dto.py
// GeneratorVersion: 1.0.0
// ContractVersion:  1.0.0
// GeneratedAt:      2026-05-14T00:00:00Z

import { type BackupPolicyId, type BribeRiskWarning, type CounterChoiceType, type CounterHeatLevel, type CounterPhase, type DefensePolicyId, type FreezeReason, type GlobalConnectionStatus, type GridStatus, type GridStressZone, type PowerScheduleId, type ProcurementLevelId, type RequestResult, type SettingsDifficultyPreset, type SettingsLanguagePreference, type SettingsTheme, type ShockTier, type TensionStatus, type WaveDataStatus, type WavePhase } from "./dtoSubTypes";
import { isWaveDataStatus, isWavePhase, isRequestResult, DEFAULT_REQUEST_RESULT } from "./dtoSubTypes";
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
    IsOutbound: boolean;
}

export interface RadarDefenseDto {
    X: number;
    Z: number;
    Range: number;
}

export interface RadarBroadcastDto {
    X: number;
    Z: number;
    Range: number;
}

export interface AaPlacementOptionEntry {
    Prefab: string;
    Mode: number;
    NameKey: string;
    Icon: string;
    Munition: number;
    Range: number;
    InterceptShahed: number;
    InterceptBallistic: number;
    Crew: number;
    Deployed: number;
    Cost: number;
    CreditsLeft: number;
    AffordableCount: number;
    CanPlace: boolean;
    LockedReasonId: string;
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

export interface ResolvedStrikeDto {
    Axis: string;
    Intercepted: boolean;
    NoEffect: boolean;
    OldValue: number;
    NewValue: number;
    Seed: number;
    TargetId: number;
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

export interface CognitiveStratumEntry {
    Stratum: number;
    Count: number;
    Infection: number;
    Resistance: number;
    AllocatedHouseholds: number;
    CoveredHouseholds: number;
    EnemyReachHouseholds: number;
    HasFoggedReach: boolean;
    SignalCoverageFraction: number;
}

export interface CognitivePsyOpsEntry {
    PsyOpsType: number;
    TargetStratum: number;
    Phase: number;
    WindowHours: number;
    WindowFraction: number;
    EtaHours: number;
    Intensity: number;
    IsDominant: boolean;
    FogState: number;
    HeroShield: number;
    Blunted: number;
    TargetCoverage: number;
    ReachHouseholds: number;
    LandedByArchetype: number;
    ContactId: number;
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
    AvatarId: string;
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
    MinScore: number;
    Icon: string;
}

export interface LeaderboardEntryDto {
    Position: number;
    Nickname: string;
    WavesSurvived: number;
    Score: number;
    RankTier: string;
}

export interface WeeklyLeaderboardEntryDto {
    Position: number;
    Nickname: string;
    WavesSurvived: number;
    Score: number;
}

export interface ShadowLeaderboardEntryDto {
    Position: number;
    Nickname: string;
    Earned: number;
    Confiscated: number;
    Net: number;
    RankTier: string;
}

export interface WeeklyShadowLeaderboardEntryDto {
    Position: number;
    Nickname: string;
    Net: number;
}

export interface ProsperityLeaderboardEntryDto {
    Position: number;
    Nickname: string;
    Waves: number;
    AvgIndex: number;
    Score: number;
    RankTier: string;
}

export interface WeeklyProsperityLeaderboardEntryDto {
    Position: number;
    Nickname: string;
    Score: number;
}

export interface AirDefenseDto {
    AaStations: number;
    SirenActive: boolean;
    PatriotAmmo: number;
    PatriotMaxAmmo: number;
    PatriotResupplyCost: number;
    GunsAmmo: number;
    GunsMaxAmmo: number;
    GunsResupplyCost: number;
    AaRosterJson: AaPlacementOptionEntry[];
    HeritageCredits: number;
    HeritageCreditsMax: number;
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
    KineticRatePercentPerDay: number;
    PsyRatePercentPerDay: number;
    TotalExodus: number;
    MonacoFamiliesFled: number;
    MonacoCapitalFled: number;
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
    ShadowFunded: boolean;
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
    HeroArchetype: number;
    ArestovychDebtRatio: number;
    ArestovychDebtCollapsing: boolean;
    HeroArchetypeSwitchReady: boolean;
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
    CenterBuilt: boolean;
    CenterOnline: boolean;
    CenterTier: number;
    CenterMaxTier: number;
    CenterCost: number;
    CenterUpgradeCost: number;
    BroadcastCapacity: number;
    BroadcastCapacityFree: number;
    CenterReach: number;
    TelecomFactor: number;
    PropagandaCenterPlacementRequest: RequestResult;
    PropagandaCenterUpgradeRequest: RequestResult;
    RumorFoodRunIntensity: number;
    SickLeaveActiveCount: number;
    ForecastType: number;
    ForecastStratum: number;
    ForecastFog: number;
    CoverageCapacityHouseholds: number;
    AllocWeightPoor: number;
    AllocWeightMiddle: number;
    AllocWeightWealthy: number;
    ShowPsyDebrief: boolean;
    PsyDebriefWave: number;
    PsyDebriefRaidCount: number;
    PsyDebriefPropaganda: number;
    PsyDebriefFakeVideo: number;
    PsyDebriefRumor: number;
    PsyDebriefBlunted: number;
    PsyDebriefStrataMask: number;
    PsyDebriefPeakIntensity: number;
    PsyDebriefMediaTrust: number;
    PsyDebriefHouseholdsImpact: number;
    PsyDebriefFoodRunActive: boolean;
    PsyDebriefInfectedDelta: number;
    ShowPsyInbound: boolean;
    PsyInboundWave: number;
    PsyInboundCount: number;
    PsyInboundType: number;
    PsyInboundStratum: number;
    PsyInboundEtaHours: number;
    PsyInboundCounterReady: boolean;
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
    AvailableViaAttention: boolean;
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
    IntelLevel: number;
    PreferredTargetPhysical: number;
    PreferredTargetDigital: number;
    PreferredTargetSocial: number;
    EnemyAggregatePressure01: number;
    DroneStock: number;
    BallisticStock: number;
    DroneLauncherCount: number;
    DroneLauncherCost: number;
    RespitePhysicalActive: boolean;
    RespiteDigitalActive: boolean;
    RespiteSocialActive: boolean;
    RespitePhysicalHoursLeft: number;
    RespiteDigitalHoursLeft: number;
    RespiteSocialHoursLeft: number;
    ObjectiveProgress: number;
    CityStability: number;
    StabilityDiscount: number;
    OperationSlots: OperationSlotDto[];
    ResolvedStrikes: ResolvedStrikeDto[];
    AttackCosts: Record<string, number>;
    OperationRequest: RequestResult;
    DroneLauncherPlacementRequest: RequestResult;
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
    ManpowerDodgerFactor: number;
    ManpowerDodgerCount: number;
    ManpowerDeferral: number;
    ManpowerDisability: number;
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
    CanEditNickname: boolean;
    NicknameLockedReasonId: string;
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
    LossKnockedOutMW: number;
    LossDamageMW: number;
    LossSaturationMW: number;
    LossFuelMW: number;
    LossAllowedMW: number;
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
    DraftDeferralPercent: number;
    DraftDisabilityHeadcount: number;
    ConstructionKickbackPercent: number;
    ConstructionKickbackPending: number;
    EmergencyFundAvailability: ActionAvailability;
    FuelSiphonAvailability: ActionAvailability;
    DraftDeferralAvailability: ActionAvailability;
    ConstructionKickbackAvailability: ActionAvailability;
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
    RadarBroadcasts: RadarBroadcastDto[];
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
    ["IsOutbound", hasBoolean],
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

const radarBroadcastDtoChecks: [string, FieldCheck][] = [
    ["X", hasNumber],
    ["Z", hasNumber],
    ["Range", hasNumber],
];

export function isRadarBroadcastDto(value: unknown): value is RadarBroadcastDto {
    return isRecord(value) && radarBroadcastDtoChecks.every(([key, check]) => check(value, key));
}

const hasRadarBroadcastDtoArray = (value: Record<string, unknown>, key: string): boolean => {
    const items = value[key];
    return Array.isArray(items) && items.every(isRadarBroadcastDto);
};

const aaPlacementOptionEntryChecks: [string, FieldCheck][] = [
    ["Prefab", hasString],
    ["Mode", hasNumber],
    ["NameKey", hasString],
    ["Icon", hasString],
    ["Munition", hasNumber],
    ["Range", hasNumber],
    ["InterceptShahed", hasNumber],
    ["InterceptBallistic", hasNumber],
    ["Crew", hasNumber],
    ["Deployed", hasNumber],
    ["Cost", hasNumber],
    ["CreditsLeft", hasNumber],
    ["AffordableCount", hasNumber],
    ["CanPlace", hasBoolean],
    ["LockedReasonId", hasString],
];

export function isAaPlacementOptionEntry(value: unknown): value is AaPlacementOptionEntry {
    return isRecord(value) && aaPlacementOptionEntryChecks.every(([key, check]) => check(value, key));
}

const hasAaPlacementOptionEntryArray = (value: Record<string, unknown>, key: string): boolean => {
    const items = value[key];
    return Array.isArray(items) && items.every(isAaPlacementOptionEntry);
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

const resolvedStrikeDtoChecks: [string, FieldCheck][] = [
    ["Axis", hasString],
    ["Intercepted", hasBoolean],
    ["NoEffect", hasBoolean],
    ["OldValue", hasNumber],
    ["NewValue", hasNumber],
    ["Seed", hasNumber],
    ["TargetId", hasNumber],
];

export function isResolvedStrikeDto(value: unknown): value is ResolvedStrikeDto {
    return isRecord(value) && resolvedStrikeDtoChecks.every(([key, check]) => check(value, key));
}

const hasResolvedStrikeDtoArray = (value: Record<string, unknown>, key: string): boolean => {
    const items = value[key];
    return Array.isArray(items) && items.every(isResolvedStrikeDto);
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

const cognitiveStratumEntryChecks: [string, FieldCheck][] = [
    ["Stratum", hasNumber],
    ["Count", hasNumber],
    ["Infection", hasNumber],
    ["Resistance", hasNumber],
    ["AllocatedHouseholds", hasNumber],
    ["CoveredHouseholds", hasNumber],
    ["EnemyReachHouseholds", hasNumber],
    ["HasFoggedReach", hasBoolean],
    ["SignalCoverageFraction", hasNumber],
];

export function isCognitiveStratumEntry(value: unknown): value is CognitiveStratumEntry {
    return isRecord(value) && cognitiveStratumEntryChecks.every(([key, check]) => check(value, key));
}

const cognitivePsyOpsEntryChecks: [string, FieldCheck][] = [
    ["PsyOpsType", hasNumber],
    ["TargetStratum", hasNumber],
    ["Phase", hasNumber],
    ["WindowHours", hasNumber],
    ["WindowFraction", hasNumber],
    ["EtaHours", hasNumber],
    ["Intensity", hasNumber],
    ["IsDominant", hasBoolean],
    ["FogState", hasNumber],
    ["HeroShield", hasNumber],
    ["Blunted", hasNumber],
    ["TargetCoverage", hasNumber],
    ["ReachHouseholds", hasNumber],
    ["LandedByArchetype", hasNumber],
    ["ContactId", hasNumber],
];

export function isCognitivePsyOpsEntry(value: unknown): value is CognitivePsyOpsEntry {
    return isRecord(value) && cognitivePsyOpsEntryChecks.every(([key, check]) => check(value, key));
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
    ["AvatarId", hasString],
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
    ["MinScore", hasNumber],
    ["Icon", hasString],
];

export function isRankTierDto(value: unknown): value is RankTierDto {
    return isRecord(value) && rankTierDtoChecks.every(([key, check]) => check(value, key));
}

const leaderboardEntryDtoChecks: [string, FieldCheck][] = [
    ["Position", hasNumber],
    ["Nickname", hasString],
    ["WavesSurvived", hasNumber],
    ["Score", hasNumber],
    ["RankTier", hasString],
];

export function isLeaderboardEntryDto(value: unknown): value is LeaderboardEntryDto {
    return isRecord(value) && leaderboardEntryDtoChecks.every(([key, check]) => check(value, key));
}

const weeklyLeaderboardEntryDtoChecks: [string, FieldCheck][] = [
    ["Position", hasNumber],
    ["Nickname", hasString],
    ["WavesSurvived", hasNumber],
    ["Score", hasNumber],
];

export function isWeeklyLeaderboardEntryDto(value: unknown): value is WeeklyLeaderboardEntryDto {
    return isRecord(value) && weeklyLeaderboardEntryDtoChecks.every(([key, check]) => check(value, key));
}

const shadowLeaderboardEntryDtoChecks: [string, FieldCheck][] = [
    ["Position", hasNumber],
    ["Nickname", hasString],
    ["Earned", hasNumber],
    ["Confiscated", hasNumber],
    ["Net", hasNumber],
    ["RankTier", hasString],
];

export function isShadowLeaderboardEntryDto(value: unknown): value is ShadowLeaderboardEntryDto {
    return isRecord(value) && shadowLeaderboardEntryDtoChecks.every(([key, check]) => check(value, key));
}

const weeklyShadowLeaderboardEntryDtoChecks: [string, FieldCheck][] = [
    ["Position", hasNumber],
    ["Nickname", hasString],
    ["Net", hasNumber],
];

export function isWeeklyShadowLeaderboardEntryDto(value: unknown): value is WeeklyShadowLeaderboardEntryDto {
    return isRecord(value) && weeklyShadowLeaderboardEntryDtoChecks.every(([key, check]) => check(value, key));
}

const prosperityLeaderboardEntryDtoChecks: [string, FieldCheck][] = [
    ["Position", hasNumber],
    ["Nickname", hasString],
    ["Waves", hasNumber],
    ["AvgIndex", hasNumber],
    ["Score", hasNumber],
    ["RankTier", hasString],
];

export function isProsperityLeaderboardEntryDto(value: unknown): value is ProsperityLeaderboardEntryDto {
    return isRecord(value) && prosperityLeaderboardEntryDtoChecks.every(([key, check]) => check(value, key));
}

const weeklyProsperityLeaderboardEntryDtoChecks: [string, FieldCheck][] = [
    ["Position", hasNumber],
    ["Nickname", hasString],
    ["Score", hasNumber],
];

export function isWeeklyProsperityLeaderboardEntryDto(value: unknown): value is WeeklyProsperityLeaderboardEntryDto {
    return isRecord(value) && weeklyProsperityLeaderboardEntryDtoChecks.every(([key, check]) => check(value, key));
}

const airDefenseDtoChecks: [string, FieldCheck][] = [
    ["AaStations", hasNumber],
    ["SirenActive", hasBoolean],
    ["PatriotAmmo", hasNumber],
    ["PatriotMaxAmmo", hasNumber],
    ["PatriotResupplyCost", hasNumber],
    ["GunsAmmo", hasNumber],
    ["GunsMaxAmmo", hasNumber],
    ["GunsResupplyCost", hasNumber],
    ["AaRosterJson", hasAaPlacementOptionEntryArray],
    ["HeritageCredits", hasNumber],
    ["HeritageCreditsMax", hasNumber],
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
    ["KineticRatePercentPerDay", hasNumber],
    ["PsyRatePercentPerDay", hasNumber],
    ["TotalExodus", hasNumber],
    ["MonacoFamiliesFled", hasNumber],
    ["MonacoCapitalFled", hasNumber],
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
    ["ShadowFunded", hasBoolean],
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
    ["HeroArchetype", hasNumber],
    ["ArestovychDebtRatio", hasNumber],
    ["ArestovychDebtCollapsing", hasBoolean],
    ["HeroArchetypeSwitchReady", hasBoolean],
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
    ["CenterBuilt", hasBoolean],
    ["CenterOnline", hasBoolean],
    ["CenterTier", hasNumber],
    ["CenterMaxTier", hasNumber],
    ["CenterCost", hasNumber],
    ["CenterUpgradeCost", hasNumber],
    ["BroadcastCapacity", hasNumber],
    ["BroadcastCapacityFree", hasNumber],
    ["CenterReach", hasNumber],
    ["TelecomFactor", hasNumber],
    ["PropagandaCenterPlacementRequest", hasRequestResult],
    ["PropagandaCenterUpgradeRequest", hasRequestResult],
    ["RumorFoodRunIntensity", hasNumber],
    ["SickLeaveActiveCount", hasNumber],
    ["ForecastType", hasNumber],
    ["ForecastStratum", hasNumber],
    ["ForecastFog", hasNumber],
    ["CoverageCapacityHouseholds", hasNumber],
    ["AllocWeightPoor", hasNumber],
    ["AllocWeightMiddle", hasNumber],
    ["AllocWeightWealthy", hasNumber],
    ["ShowPsyDebrief", hasBoolean],
    ["PsyDebriefWave", hasNumber],
    ["PsyDebriefRaidCount", hasNumber],
    ["PsyDebriefPropaganda", hasNumber],
    ["PsyDebriefFakeVideo", hasNumber],
    ["PsyDebriefRumor", hasNumber],
    ["PsyDebriefBlunted", hasNumber],
    ["PsyDebriefStrataMask", hasNumber],
    ["PsyDebriefPeakIntensity", hasNumber],
    ["PsyDebriefMediaTrust", hasNumber],
    ["PsyDebriefHouseholdsImpact", hasNumber],
    ["PsyDebriefFoodRunActive", hasBoolean],
    ["PsyDebriefInfectedDelta", hasNumber],
    ["ShowPsyInbound", hasBoolean],
    ["PsyInboundWave", hasNumber],
    ["PsyInboundCount", hasNumber],
    ["PsyInboundType", hasNumber],
    ["PsyInboundStratum", hasNumber],
    ["PsyInboundEtaHours", hasNumber],
    ["PsyInboundCounterReady", hasBoolean],
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
    ["AvailableViaAttention", hasBoolean],
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
    ["IntelLevel", hasNumber],
    ["PreferredTargetPhysical", hasNumber],
    ["PreferredTargetDigital", hasNumber],
    ["PreferredTargetSocial", hasNumber],
    ["EnemyAggregatePressure01", hasNumber],
    ["DroneStock", hasNumber],
    ["BallisticStock", hasNumber],
    ["DroneLauncherCount", hasNumber],
    ["DroneLauncherCost", hasNumber],
    ["RespitePhysicalActive", hasBoolean],
    ["RespiteDigitalActive", hasBoolean],
    ["RespiteSocialActive", hasBoolean],
    ["RespitePhysicalHoursLeft", hasNumber],
    ["RespiteDigitalHoursLeft", hasNumber],
    ["RespiteSocialHoursLeft", hasNumber],
    ["ObjectiveProgress", hasNumber],
    ["CityStability", hasNumber],
    ["StabilityDiscount", hasNumber],
    ["OperationSlots", hasOperationSlotDtoArray],
    ["ResolvedStrikes", hasResolvedStrikeDtoArray],
    ["AttackCosts", hasObject],
    ["OperationRequest", hasRequestResult],
    ["DroneLauncherPlacementRequest", hasRequestResult],
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
    ["ManpowerDodgerFactor", hasNumber],
    ["ManpowerDodgerCount", hasNumber],
    ["ManpowerDeferral", hasNumber],
    ["ManpowerDisability", hasNumber],
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
    ["CanEditNickname", hasBoolean],
    ["NicknameLockedReasonId", hasString],
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
    ["LossKnockedOutMW", hasNumber],
    ["LossDamageMW", hasNumber],
    ["LossSaturationMW", hasNumber],
    ["LossFuelMW", hasNumber],
    ["LossAllowedMW", hasNumber],
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
    ["DraftDeferralPercent", hasNumber],
    ["DraftDisabilityHeadcount", hasNumber],
    ["ConstructionKickbackPercent", hasNumber],
    ["ConstructionKickbackPending", hasNumber],
    ["EmergencyFundAvailability", hasActionAvailability],
    ["FuelSiphonAvailability", hasActionAvailability],
    ["DraftDeferralAvailability", hasActionAvailability],
    ["ConstructionKickbackAvailability", hasActionAvailability],
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
    ["RadarBroadcasts", hasRadarBroadcastDtoArray],
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

export const DEFAULT_ACTION_AVAILABILITY: ActionAvailability = {
    CanRun: false,
    LockedReasonId: "UI_ACTION_WAVE_LOCKED",
    EffectiveCost: 0,
};

export const DEFAULT_MAP_BOUNDS: MapBoundsDto = {
    MinX: 0,
    MaxX: 0,
    MinZ: 0,
    MaxZ: 0,
};

export const DEFAULT_FOCUS_RANGE: FocusRangeDto = {
    Min: 0,
    Max: 0,
};

export const DEFAULT_OFFICIAL_TREASURY: OfficialTreasuryDto = {
    Balance: 0,
    TotalIncome: 0,
    TotalExpenses: 0,
};

export const DEFAULT_SHADOW_WALLET: ShadowWalletDto = {
    Available: 0,
    LockedBalance: 0,
    TotalAssets: 0,
    ShadowIncome: 0,
    ShadowExpenses: 0,
};

export const DEFAULT_ATTACK_TIME_ESTIMATE: AttackTimeEstimateDto = {
    Status: "unknown",
};

export const DEFAULT_AIR_DEFENSE_DTO: AirDefenseDto = {
    AaStations: 0,
    SirenActive: false,
    PatriotAmmo: 0,
    PatriotMaxAmmo: 0,
    PatriotResupplyCost: 0,
    GunsAmmo: 0,
    GunsMaxAmmo: 0,
    GunsResupplyCost: 0,
    AaRosterJson: [],
    HeritageCredits: 0,
    HeritageCreditsMax: 0,
    PatriotInterceptsDrones: false,
    AutoResupplyEnabled: true,
    DefensePolicyName: "",
    DefensePolicyId: 0,
    SpotterPenaltyPercent: 0,
    DonorPatriotCredits: 0,
    EmergencyResupplyRequest: DEFAULT_REQUEST_RESULT,
    DefensePolicyRequest: DEFAULT_REQUEST_RESULT,
    PatriotDroneToggleRequest: DEFAULT_REQUEST_RESULT,
    AirDefensePlacementRequest: DEFAULT_REQUEST_RESULT,
    CanResupplyPatriot: false,
    ResupplyPatriotLockedReasonId: "",
    CanResupplyGuns: false,
    ResupplyGunsLockedReasonId: "",
};

export const DEFAULT_ATTENTION_DTO: AttentionDto = {
    ShockLevel: 0,
    ShockTier: "DeepConcern",
    CasualtiesThisWeek: 0,
    BuildingsDestroyedThisWeek: 0,
    CriticalHitsThisWeek: 0,
    TotalCasualties: 0,
    TotalBuildingsDestroyed: 0,
    TotalCivilianBuildingsDestroyed: 0,
    TotalCriticalHits: 0,
    ExodusActive: false,
    BaseExodusRatePercentPerDay: 0,
    ExodusRatePercentPerDay: 0,
    KineticRatePercentPerDay: 0,
    PsyRatePercentPerDay: 0,
    TotalExodus: 0,
    MonacoFamiliesFled: 0,
    MonacoCapitalFled: 0,
};

export const DEFAULT_BACKUP_POWER_DTO: BackupPowerDto = {
    BackupCharge: 0,
    GeneratorsRunning: 0,
    NoiseLevel: 0,
    ProtectedBuildings: 0,
    BackupCapacity: 0,
    DischargingCount: 0,
    ShadowProgramsJson: [],
    ProcurementCooldown: 0,
    BackupPolicy: 0,
    HospitalsPowered: 0,
    HospitalsTotal: 0,
    SchoolsPowered: 0,
    SchoolsTotal: 0,
    ModernizationRequest: DEFAULT_REQUEST_RESULT,
    BackupPolicyRequest: DEFAULT_REQUEST_RESULT,
    CanSetBackupPolicy: false,
    SetBackupPolicyLockedReasonId: "",
};

export const DEFAULT_BUCKWHEAT_DTO: BuckwheatDto = {
    BuckwheatTons: 0,
    ProcurementLevel: 0,
    DailyCost: 0,
    BaseDailyCost: 0,
    ShadowFunded: false,
    LastDistributeResult: DEFAULT_REQUEST_RESULT,
    ProcurementLevelRequest: DEFAULT_REQUEST_RESULT,
    CanDistribute: false,
    DistributeLockedReasonId: "",
    CanAffordProcurement: false,
    AffordProcurementLockedReasonId: "",
    CanSetProcurement25: false,
    Procurement25LockedReasonId: "",
    CanSetProcurement50: false,
    Procurement50LockedReasonId: "",
    CanSetProcurement75: false,
    Procurement75LockedReasonId: "",
    CanSetProcurement100: false,
    Procurement100LockedReasonId: "",
};

export const DEFAULT_CRISIS_SWEEP_DTO: CrisisSweepDto = {
    Mode: 0,
    HasResult: false,
    ComputedAtGameHours: 0,
    ArchetypeId: 0,
    PopulationPeak: 0,
    WarDay: 0,
    WorstCaseRecoveryBallisticOnly: 0,
    WorstCaseRecoveryMixed: 0,
    IsRecoverableBallisticOnly: false,
    IsRecoverableMixed: false,
    GraceWindowHours: 0,
    DroneInterceptBallisticOnly: 0,
    DroneInterceptMixed: 0,
    FreeHeritageGrant: 0,
    OperationalAaAtVerdict: 0,
    ManpowerTotal: 0,
    ManpowerUsed: 0,
    ManpowerCasualties: 0,
    ManpowerAvailable: 0,
    AaHeritage: 0,
    AaBofors: 0,
    AaGepard: 0,
    AaPatriot: 0,
    CoveragePct: 0,
    AreaKm2: 0,
    BallisticInterceptBallisticOnly: 0,
    BallisticInterceptMixed: 0,
    BallisticTargets: 0,
    MissilesSpentOnDrones: 0,
    PatriotInterceptsDrones: false,
    CalmHours: 0,
    WavePressureAtPeak: 0,
    SampleCount: 0,
    BlackoutProbabilityPct: 0,
    MedianCollapseDay: 0,
    UnsheddableFloorMW: 0,
    RepairSlots: 0,
    RepairFundingCash: 0,
    RepairTier: 0,
    RepairBudgetLive: false,
};

export const DEFAULT_COGNITIVE_DTO: CognitiveDto = {
    CognitiveActive: false,
    InfectionRate: 0,
    RecoveryRate: 0,
    PenaltyThreshold: 0,
    TotalDistricts: 0,
    CompromisedDistricts: 0,
    HeroStatus: 0,
    HeroDeployCost: 0,
    HeroInfectionReduction: 0,
    HeroRecoveryBonus: 0,
    HeroActionRequest: DEFAULT_REQUEST_RESULT,
    HeroArchetype: 0,
    ArestovychDebtRatio: 0,
    ArestovychDebtCollapsing: false,
    HeroArchetypeSwitchReady: false,
    ProtestRisk: 0,
    DominantNarrative: "",
    AvgIntegrity: 0,
    TotalHouseholds: 0,
    AvgInfection: 0,
    AvgResistance: 0,
    AvgTrauma: 0,
    HouseholdsUnderBlackout: 0,
    HouseholdsWithEnvy: 0,
    HouseholdsUnderImpact: 0,
    HouseholdsInfected: 0,
    VulnerableHouseholds: 0,
    AvgBlackoutHours: 0,
    BlackoutVulnerability: 0,
    InternetMode: 0,
    CommercePenalty: 0,
    InternetModeRequest: DEFAULT_REQUEST_RESULT,
    IpsoActive: false,
    IpsoIntensity: 0,
    IpsoDistrictCount: 0,
    IpsoTotalDistricts: 0,
    TelemarathonActive: false,
    NarrativeMode: 0,
    MediaTrust: 0,
    IsInShock: false,
    ShockHoursRemaining: 0,
    AudienceFatigue: 0,
    TelemarathonModeRequest: DEFAULT_REQUEST_RESULT,
    TelemarathonActiveRequest: DEFAULT_REQUEST_RESULT,
    CenterBuilt: false,
    CenterOnline: false,
    CenterTier: 0,
    CenterMaxTier: 0,
    CenterCost: 0,
    CenterUpgradeCost: 0,
    BroadcastCapacity: 0,
    BroadcastCapacityFree: 0,
    CenterReach: 0,
    TelecomFactor: 0,
    PropagandaCenterPlacementRequest: DEFAULT_REQUEST_RESULT,
    PropagandaCenterUpgradeRequest: DEFAULT_REQUEST_RESULT,
    RumorFoodRunIntensity: 0,
    SickLeaveActiveCount: 0,
    ForecastType: -1,
    ForecastStratum: 0,
    ForecastFog: 0,
    CoverageCapacityHouseholds: 0,
    AllocWeightPoor: 0,
    AllocWeightMiddle: 0,
    AllocWeightWealthy: 0,
    ShowPsyDebrief: false,
    PsyDebriefWave: 0,
    PsyDebriefRaidCount: 0,
    PsyDebriefPropaganda: 0,
    PsyDebriefFakeVideo: 0,
    PsyDebriefRumor: 0,
    PsyDebriefBlunted: 0,
    PsyDebriefStrataMask: 0,
    PsyDebriefPeakIntensity: 0,
    PsyDebriefMediaTrust: 0,
    PsyDebriefHouseholdsImpact: 0,
    PsyDebriefFoodRunActive: false,
    PsyDebriefInfectedDelta: 0,
    ShowPsyInbound: false,
    PsyInboundWave: 0,
    PsyInboundCount: 0,
    PsyInboundType: -1,
    PsyInboundStratum: 0,
    PsyInboundEtaHours: 0,
    PsyInboundCounterReady: false,
    CanDeployHero: false,
    DeployHeroLockedReasonId: "",
    CanRecallHero: false,
    RecallHeroLockedReasonId: "",
    CanSetHeroCounter: false,
    SetHeroCounterLockedReasonId: "",
    CanSetHeroLecturing: false,
    SetHeroLecturingLockedReasonId: "",
};

export const DEFAULT_COUNTERMEASURES_DTO: CountermeasuresDto = {
    CorruptionScore: 0,
    Heat: 0,
    HeatLevel: "Safe",
    CountermeasuresPhase: "Idle",
    InvestigationProgress: 0,
    ChargesCount: 0,
    ProtestCount: 0,
    ChoiceRequired: false,
    ChoiceType: 0,
    BribeCost: 0,
    BaseBribeCost: 0,
    BribeAvailability: DEFAULT_ACTION_AVAILABILITY,
    LastChoiceResult: "",
    CurrentJournalist: "",
    IsArrested: false,
    ArrestedAssetsSeized: 0,
    ArrestedWalletAfter: 0,
    BribeRiskWarning: "",
    SanctionsSuppressingCorruption: false,
    LastChoiceRequestResult: DEFAULT_REQUEST_RESULT,
};

export const DEFAULT_DONOR_DTO: DonorDto = {
    DonorUsesRemaining: 0,
    DonorCooldownDays: 0,
    DonorStatus: "",
    AvailableViaAttention: false,
    TrustIndex: 0,
    ScandalPenalty: 0,
    DonorExpectedAid: "",
    DonorDialogActive: false,
    ProducerReady: false,
    TrustLocked: false,
    ProducerReasonId: "",
    DonorFundsAmount: 0,
    DonorGeneratorCount: 0,
    DonorGeneratorMW: 0,
    DonorPatriotDays: 0,
    AidTierId: 0,
    AidFundsOffered: 0,
    AidFundsAccessible: 0,
    PatriotOffered: false,
    PatriotBlocked: false,
    TrustMessageId: 0,
    BlockedReasonId: 0,
    HasBlockedItems: false,
    DonorActiveGenerators: 0,
    SanctionsActive: false,
    SanctionDaysRemaining: 0,
    SanctionTradePenalty: 0,
    DonorDialogRequest: DEFAULT_REQUEST_RESULT,
    DonorSelectionRequest: DEFAULT_REQUEST_RESULT,
    DonorFundsAvailable: false,
    DonorFundsLockedReasonId: "",
    DonorPowerAvailable: false,
    DonorPowerLockedReasonId: "",
    DonorDefenseAvailable: false,
    DonorDefenseLockedReasonId: "",
};

export const DEFAULT_EXPORT_DTO: ExportDto = {
    ExportPercent: 0,
    ExportedMW: 0,
    DailyIncome: 0,
    OffshoreBalance: 0,
    IsFrozen: false,
    FreezeReason: 0,
    ExportAvailability: DEFAULT_ACTION_AVAILABILITY,
    ShadowTradeExportRequest: DEFAULT_REQUEST_RESULT,
};

export const DEFAULT_FINANCE_DTO: FinanceDto = {
    CityTreasury: 0,
    TotalLiquidity: 0,
    OfficialTreasury: DEFAULT_OFFICIAL_TREASURY,
    ShadowWallet: DEFAULT_SHADOW_WALLET,
    Expenses: {},
    Income: {},
    TotalExpenses: 0,
    TotalIncome: 0,
    TotalDebt: 0,
    DebtBreakdown: {},
    DebtWarning: false,
    DebtRestructured: false,
    SanctionsMarkup: 0,
};

export const DEFAULT_GRID_WARFARE_DTO: GridWarfareDto = {
    ShadowBalance: 0,
    ShadowLocked: 0,
    ShadowTotal: 0,
    EnemyPhysicalAxis: 0,
    EnemyDigitalAxis: 0,
    EnemySocialAxis: 0,
    IntelLevel: 0,
    PreferredTargetPhysical: 65535,
    PreferredTargetDigital: 65535,
    PreferredTargetSocial: 65535,
    EnemyAggregatePressure01: -1,
    DroneStock: 0,
    BallisticStock: 0,
    DroneLauncherCount: 0,
    DroneLauncherCost: 0,
    RespitePhysicalActive: false,
    RespiteDigitalActive: false,
    RespiteSocialActive: false,
    RespitePhysicalHoursLeft: -1,
    RespiteDigitalHoursLeft: -1,
    RespiteSocialHoursLeft: -1,
    ObjectiveProgress: 0,
    CityStability: 0,
    StabilityDiscount: 0,
    OperationSlots: [],
    ResolvedStrikes: [],
    AttackCosts: { drone: 0, blackout: 0, disinfo: 0 },
    OperationRequest: DEFAULT_REQUEST_RESULT,
    DroneLauncherPlacementRequest: DEFAULT_REQUEST_RESULT,
    GridWarfareUnlocked: false,
    CanPrepareDrone: false,
    PrepareDroneLockedReasonId: "",
    CanPrepareBlackout: false,
    PrepareBlackoutLockedReasonId: "",
    CanPrepareDisinfo: false,
    PrepareDisinfoLockedReasonId: "",
};

export const DEFAULT_IMPORT_DTO: ImportDto = {
    ShadowImportMW: 0,
    MaxShadowImportMW: 0,
    SelectedPresetIndex: 0,
    ShadowImportCost: 0,
    DiscoveryRisk: 0,
    ShadowImportDaysActive: 0,
    IsSanctioned: false,
    ShadowImportSanctionDays: 0,
    ShadowImportAvailability: DEFAULT_ACTION_AVAILABILITY,
    IsFrozen: false,
    FreezeReason: 0,
    ShadowTradeImportRequest: DEFAULT_REQUEST_RESULT,
};

export const DEFAULT_INTEL_DTO: IntelDto = {
    TensionLevel: 0,
    TensionStatus: "LOW",
    WaveTypePrediction: "",
    IsMassiveStrike: false,
    EnergyFocusRange: DEFAULT_FOCUS_RANGE,
    InfraFocusRange: DEFAULT_FOCUS_RANGE,
    ResidentialFocusRange: DEFAULT_FOCUS_RANGE,
    TimeEstimate: DEFAULT_ATTACK_TIME_ESTIMATE,
    EstimatedShaheds: 0,
    EstimatedBallistics: 0,
    HasInsider: false,
    InsiderCost: 0,
    BaseInsiderCost: 0,
    TensionPriceMultiplier: 0,
    TensionPriceModifierPercent: 0,
    InsiderRequest: DEFAULT_REQUEST_RESULT,
    IntelUpgradeLevel: 0,
    IntelUpgradeCost: 0,
    IntelUpgradeRequest: DEFAULT_REQUEST_RESULT,
    CanBuyInsider: false,
    InsiderLockedReasonId: "",
    CanUpgradeIntel: false,
    IntelUpgradeLockedReasonId: "",
};

export const DEFAULT_MAINTENANCE_DTO: MaintenanceDto = {
    PendingProcurementOffer: null,
    ShadyContractCount: 0,
    TotalContractCount: 0,
    ActiveContractsJson: [],
    MaintenanceContractRequest: DEFAULT_REQUEST_RESULT,
};

export const DEFAULT_MOBILIZATION_DTO: MobilizationDto = {
    ManpowerAvailable: 0,
    ManpowerUsed: 0,
    ManpowerTotal: 0,
    ManpowerPercent: 0,
    ManpowerBasePool: 0,
    ManpowerCasualties: 0,
    ManpowerPatriotismFactor: 0,
    ManpowerMoraleFactor: 0,
    ManpowerFatigueFactor: 0,
    ManpowerDodgerFactor: 100,
    ManpowerDodgerCount: 0,
    ManpowerDeferral: 0,
    ManpowerDisability: 0,
    IsConscriptionActive: false,
    IsWarFatigued: false,
    IsManpowerCritical: false,
    IsManpowerOvercommitted: false,
    CallToArmsOnCooldown: false,
    ConscriptionReactivationOnCooldown: false,
    PredictedConscriptionRelease: 0,
    SocialPenaltyProducerReady: false,
    SocialPenaltyReasonId: "",
    WarDay: 0,
    CallToArmsRequest: DEFAULT_REQUEST_RESULT,
    ConscriptionToggleRequest: DEFAULT_REQUEST_RESULT,
    CanCallToArms: false,
    CallToArmsLockedReasonId: "",
    CanToggleConscription: false,
    ConscriptionLockedReasonId: "",
};

export const DEFAULT_NEWS_DTO: NewsDto = {
    GlobalOnlineNow: 0,
    GlobalOnlineHour: 0,
    GlobalOnlineToday: 0,
    GlobalOnlineTotal: 0,
    GlobalConnected: false,
    GlobalConnectionStatus: "Disconnected",
    NetworkConnectionEnabled: false,
    PlayerNickname: "",
    NicknameRequest: DEFAULT_REQUEST_RESULT,
    NicknameChangesRemaining: 0,
    NicknameInitialized: false,
    OnlineConsentRecorded: false,
    CanEditNickname: false,
    NicknameLockedReasonId: "",
};

export const DEFAULT_POWER_GRID_DTO: PowerGridDto = {
    GridStatus: "unknown",
    Production: 0,
    Demand: 0,
    Consumption: 0,
    GameHour: 0,
    GridFrequency: 0,
    StressZone: "normal",
    StressPercent: 0,
    RecoveryHours: 0,
    CollapseThresholdHours: 2,
    ThresholdActive: false,
    BuildingsCutCount: 0,
    DeliveredMW: 0,
    ForcedOffMW: 0,
    AutoCutMW: 0,
    DistrictShedMW: 0,
    AutoDispatchShedMW: 0,
    CitySchedule: 0,
    EffectiveCityMode: 0,
    DistrictsOverrideCity: false,
    CityScheduleAvailability: DEFAULT_ACTION_AVAILABILITY,
    AutoDispatchEnabled: false,
    AutoDispatchSheddedCount: 0,
    AutoDispatchBlockedByVip: false,
    ShadowBalance: 0,
    AtRiskPlantCount: 0,
    GenerationSources: [],
    CivilianDamage: [],
    PlantMunicipalRepairHours: 0,
    PlantShadowOpsRepairHours: 0,
    CivilianMunicipalRepairHours: 0,
    CivilianShadowOpsRepairHours: 0,
    PlantRepairRequest: DEFAULT_REQUEST_RESULT,
    CivilianRepairRequest: DEFAULT_REQUEST_RESULT,
    AutoDispatchToggleRequest: DEFAULT_REQUEST_RESULT,
    DistrictToggleRequest: DEFAULT_REQUEST_RESULT,
    CitySchedulePeriodRequest: DEFAULT_REQUEST_RESULT,
    DistrictInternetToggleRequest: DEFAULT_REQUEST_RESULT,
    FleetSaturationFactor: 1,
    CityDispatchableMW: 0,
    CapacityHeadroomMW: 0,
    GridExportMW: 0,
    HeadroomWarningMW: 0,
    LossKnockedOutMW: 0,
    LossDamageMW: 0,
    LossSaturationMW: 0,
    LossFuelMW: 0,
    LossAllowedMW: 0,
};

export const DEFAULT_REPUTATION_DTO: ReputationDto = {
    TrustLevel: 0,
    TrustTier: "",
    IsFrozenOut: false,
    OfferFrequencyMult: 0,
};

export const DEFAULT_SCHEMES_DTO: SchemesDto = {
    EmergencyFundWithdraw: 0,
    EmergencyFundBalance: 0,
    FuelSiphonPercent: 0,
    DraftDeferralPercent: 0,
    DraftDisabilityHeadcount: 0,
    ConstructionKickbackPercent: 0,
    ConstructionKickbackPending: 0,
    EmergencyFundAvailability: DEFAULT_ACTION_AVAILABILITY,
    FuelSiphonAvailability: DEFAULT_ACTION_AVAILABILITY,
    DraftDeferralAvailability: DEFAULT_ACTION_AVAILABILITY,
    ConstructionKickbackAvailability: DEFAULT_ACTION_AVAILABILITY,
    CorruptionSchemeRequest: DEFAULT_REQUEST_RESULT,
};

export const DEFAULT_SPOTTER_DTO: SpotterDto = {
    SpotterCount: 0,
    SpotterPenaltyPercent: 0,
    SpotterRawPenaltyPercent: 0,
    SbuVisitCost: 0,
    TotalSBUVisits: 0,
    EvacuationCost: 0,
    TotalEvacuations: 0,
    CounterOSINTActive: false,
    CounterOSINTDailyCost: 0,
    SpotterActionRequest: DEFAULT_REQUEST_RESULT,
    CanSbuVisit: false,
    SbuVisitLockedReasonId: "",
    CanEvacuationRun: false,
    EvacuationRunLockedReasonId: "",
    CanToggleCounterOSINT: false,
    CounterOSINTLockedReasonId: "",
};

export const DEFAULT_THREAT_DTO: ThreatDto = {
    WavePhase: "calm",
    WaveNumber: 0,
    ThreatsExpected: 0,
    ThreatsSpawned: 0,
    ThreatsRemaining: 0,
    ThreatsIntercepted: 0,
    ThreatsHit: 0,
    ThreatsCrashed: 0,
    TimeInPhase: 0,
    PhaseEndTime: 0,
    ScenarioStarted: false,
    ProducerReady: false,
    WaveDataStatus: "unavailable",
    WaitingForLaunchWindow: false,
    EarlyWarningMessage: "",
    IntelReportLabel: "",
    NoActiveThreatsLabel: "",
    ThreatTargets: [],
    RadarThreats: [],
    RadarTargets: [],
    RadarDefenses: [],
    RadarBroadcasts: [],
    MapBounds: DEFAULT_MAP_BOUNDS,
    IdentifyTrackedEntity: -1,
    IdentifyProgress: 0,
    IdentifyConfirmed: false,
    IdentifyFocusActive: false,
    ShowDebriefing: false,
    DebriefingWave: 0,
    DebriefingIntercepted: 0,
    DebriefingHits: 0,
    DebriefingShotsFired: 0,
    DebriefingCasualties: 0,
    DebriefingDamageCost: 0,
    DebriefingInfraDamageCost: 0,
    DebriefingCrashed: 0,
    DebriefingTotalThreats: 0,
    DebriefingEfficiency: 0,
    RadarInterceptions: [],
    CameraX: -1000000000,
    CameraZ: -1000000000,
};
