/**
 * Domain DTO types and validators.
 *
 * Interfaces and validators are generated from Docs/Contracts/ui-dto.contract.yaml.
 * Run: python ../../scripts/generate.py ui-dto
 *
 * SUB-TYPES for Raw JSON fields are in dtoSubTypes.ts.
 */

// Imports first (eslint import/first)
import { isRecord } from "../utils/typeGuards";
import {
    isMapBoundsDto,
    isThreatDto as isGeneratedThreatDto,
    type ActionAvailability,
    type AirDefenseDto as GeneratedAirDefenseDto,
    type AttackTimeEstimateDto,
    type AttentionDto as GeneratedAttentionDto,
    type BackupPowerDto as GeneratedBackupPowerDto,
    type BuckwheatDto as GeneratedBuckwheatDto,
    type CognitiveDto as GeneratedCognitiveDto,
    type CountermeasuresDto as GeneratedCountermeasuresDto,
    type CrisisSweepDto as GeneratedCrisisSweepDto,
    type DonorDto as GeneratedDonorDto,
    type ExportDto as GeneratedExportDto,
    type FinanceDto as GeneratedFinanceDto,
    type FocusRangeDto,
    type GridWarfareDto as GeneratedGridWarfareDto,
    type ImportDto as GeneratedImportDto,
    type IntelDto as GeneratedIntelDto,
    type MaintenanceDto as GeneratedMaintenanceDto,
    type MapBoundsDto,
    type MobilizationDto as GeneratedMobilizationDto,
    type NewsDto as GeneratedNewsDto,
    type OfficialTreasuryDto,
    type PowerGridDto as GeneratedPowerGridDto,
    type ReputationDto as GeneratedReputationDto,
    type SchemesDto as GeneratedSchemesDto,
    type ShadowWalletDto,
    type SpotterDto as GeneratedSpotterDto,
    type ThreatDto as GeneratedThreatDto,
} from './domainDtos.generated';
import {
    type AttackCosts,
    type RequestResult,
} from './dtoSubTypes';

// Re-export under historical names so existing consumers don't churn.
export type AttackTimeEstimate = AttackTimeEstimateDto;
export type FocusRange = FocusRangeDto;
export type OfficialTreasury = OfficialTreasuryDto;
export type ShadowWallet = ShadowWalletDto;

// Re-export generated interfaces (source of truth: ui-dto contract)
export type {
    AirDefenseDto,
    ArrestedModalPayloadDto,
    AttentionDto,
    BackupPowerDto,
    BuckwheatDto,
    CognitiveDto,
    CountermeasuresDto,
    CrisisSweepDto,
    DonorDto,
    ExportDto,
    FinanceDto,
    GridWarfareDto,
    ImportDto,
    IntelDto,
    MaintenanceDto,
    MobilizationDto,
    NewsDto,
    PowerGridDto,
    ReputationDto,
    SchemesDto,
    SettingsDto,
    SettingsLocalizationDto,
    SpotterDto,
    ThreatDto,
} from './domainDtos.generated';

export {
    isActionAvailability,
    isAirDefenseDto,
    isArrestedModalPayloadDto,
    isAttentionDto,
    isBackupPowerDto,
    isBuckwheatDto,
    isCognitiveDto,
    isCountermeasuresDto,
    isDonorDto,
    isExportDto,
    isFinanceDto,
    isGridWarfareDto,
    isImportDto,
    isIntelDto,
    isMaintenanceDto,
    isMobilizationDto,
    isNewsDto,
    isPowerGridDto,
    isReputationDto,
    isSchemesDto,
    isSettingsDto,
    isSettingsLocalizationDto,
    isSpotterDto,
} from './domainDtos.generated';

// Re-export hand-written sub-types (Raw JSON field shapes)
export * from './dtoSubTypes';

export function isThreatDto(value: unknown): value is GeneratedThreatDto {
    return isGeneratedThreatDto(value) && isMapBoundsDto(value.MapBounds);
}

export const DEFAULT_REQUEST_RESULT: RequestResult = {
    RequestId: 0,
    Status: "idle",
    ReasonId: "",
    CanonicalEcho: "",
    DiscriminatorKind: "none",
    DiscriminatorValue: "",
};

/**
 * Placeholder reason carried by DEFAULT_ACTION_AVAILABILITY while the
 * publishing system is feature-gated to a later beta wave (WaveLocked).
 * Tooltips translate it; inline status lines should suppress it — the
 * GlassCase WAVE badge already communicates the lock on those panels.
 */
export const WAVE_LOCKED_REASON_ID = "UI_ACTION_WAVE_LOCKED";

/**
 * "No camera position" sentinel for ThreatDto.CameraX/CameraZ. Mirrors
 * ThreatDto.CameraMarkerSentinel (C#). After map normalization it lands far
 * outside [0,100], so the radar omits the "you are here" marker.
 */
export const CAMERA_MARKER_SENTINEL = -1_000_000_000;

export const DEFAULT_ACTION_AVAILABILITY: ActionAvailability = {
    CanRun: false,
    // Real reason key, not "": an empty id makes lock tooltips render the
    // missing-key fallback "[]".
    LockedReasonId: WAVE_LOCKED_REASON_ID,
    EffectiveCost: 0,
};

export const DEFAULT_FOCUS_RANGE: FocusRangeDto = {
    Min: 0,
    Max: 0,
};

export const DEFAULT_ATTACK_TIME_ESTIMATE: AttackTimeEstimateDto = {
    Status: "unknown",
};

export const DEFAULT_MAP_BOUNDS: MapBoundsDto = {
    MinX: 0,
    MaxX: 0,
    MinZ: 0,
    MaxZ: 0,
};

export const DEFAULT_ATTACK_COSTS: AttackCosts = {
    drone: 0,
    blackout: 0,
    disinfo: 0,
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

export const DEFAULT_FINANCE_DTO: GeneratedFinanceDto = {
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

export const DEFAULT_POWER_GRID_DTO: GeneratedPowerGridDto = {
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
};

export const DEFAULT_BACKUP_POWER_DTO: GeneratedBackupPowerDto = {
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
    CanSetBackupPolicy: false,
    SetBackupPolicyLockedReasonId: "",
    ModernizationRequest: DEFAULT_REQUEST_RESULT,
    BackupPolicyRequest: DEFAULT_REQUEST_RESULT,
};

export const DEFAULT_AIR_DEFENSE_DTO: GeneratedAirDefenseDto = {
    AaAmmo: 0,
    AaMaxAmmo: 0,
    AaStations: 0,
    SirenActive: false,
    PatriotAmmo: 0,
    PatriotMaxAmmo: 0,
    PatriotResupplyCost: 0,
    BoforsAmmo: 0,
    BoforsMaxAmmo: 0,
    HeritageAmmo: 0,
    HeritageMaxAmmo: 0,
    GepardAmmo: 0,
    GepardMaxAmmo: 0,
    GunsResupplyCost: 0,
    CanResupplyPatriot: false,
    ResupplyPatriotLockedReasonId: "",
    CanResupplyGuns: false,
    ResupplyGunsLockedReasonId: "",
    CanPlaceHeritageBofors: false,
    HeritageBoforsLockedReasonId: "",
    CanPlaceDonorPatriot: false,
    DonorPatriotLockedReasonId: "",
    CanPlacePaidBofors: false,
    PaidBoforsLockedReasonId: "",
    CanPlacePaidGepard: false,
    PaidGepardLockedReasonId: "",
    CanPlacePaidPatriot: false,
    PaidPatriotLockedReasonId: "",
    HeritageCredits: 0,
    HeritageCreditsMax: 0,
    HeritageCrew: 0,
    BoforsCrew: 0,
    GepardCrew: 0,
    HeritageBoforsCount: 0,
    BoforsCount: 0,
    GepardCount: 0,
    PatriotCount: 0,
    BoforsPrice: 0,
    PaidBoforsAffordableCount: 0,
    PaidGepardAffordableCount: 0,
    PaidPatriotAffordableCount: 0,
    GepardPrice: 0,
    PatriotPrice: 0,
    PatriotCrew: 0,
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
};

export const DEFAULT_THREAT_DTO: GeneratedThreatDto = {
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
    // Far out-of-bounds sentinel: "no camera position" → normalizes outside
    // [0,100] so the radar omits the "you are here" marker (mirrors
    // ThreatDto.CameraMarkerSentinel in C#).
    CameraX: CAMERA_MARKER_SENTINEL,
    CameraZ: CAMERA_MARKER_SENTINEL,
};

export const DEFAULT_INTEL_DTO: GeneratedIntelDto = {
    TensionLevel: 0,
    TensionStatus: "LOW",
    WaveTypePrediction: "",
    IsMassiveStrike: false,
    EnergyFocusRange: DEFAULT_FOCUS_RANGE,
    InfraFocusRange: DEFAULT_FOCUS_RANGE,
    ResidentialFocusRange: DEFAULT_FOCUS_RANGE,
    TimeEstimate: DEFAULT_ATTACK_TIME_ESTIMATE,
    ThreatComposition: "",
    EstimatedShaheds: 0,
    EstimatedBallistics: 0,
    HasInsider: false,
    InsiderCost: 0,
    BaseInsiderCost: 0,
    CanBuyInsider: false,
    InsiderLockedReasonId: "",
    CanUpgradeIntel: false,
    IntelUpgradeLockedReasonId: "",
    TensionPriceMultiplier: 0,
    TensionPriceModifierPercent: 0,
    InsiderRequest: DEFAULT_REQUEST_RESULT,
    IntelUpgradeLevel: 0,
    IntelUpgradeCost: 0,
    IntelUpgradeRequest: DEFAULT_REQUEST_RESULT,
};

export const DEFAULT_SPOTTER_DTO: GeneratedSpotterDto = {
    SpotterCount: 0,
    SpotterPenaltyPercent: 0,
    SpotterRawPenaltyPercent: 0,
    SbuVisitCost: 0,
    TotalSBUVisits: 0,
    EvacuationCost: 0,
    TotalEvacuations: 0,
    CounterOSINTActive: false,
    CounterOSINTDailyCost: 0,
    CanSbuVisit: false,
    SbuVisitLockedReasonId: "",
    CanEvacuationRun: false,
    EvacuationRunLockedReasonId: "",
    CanToggleCounterOSINT: false,
    CounterOSINTLockedReasonId: "",
    SpotterActionRequest: DEFAULT_REQUEST_RESULT,
};

export const DEFAULT_EXPORT_DTO: GeneratedExportDto = {
    ExportPercent: 0,
    ExportedMW: 0,
    DailyIncome: 0,
    OffshoreBalance: 0,
    IsFrozen: false,
    FreezeReason: 0,
    ExportAvailability: DEFAULT_ACTION_AVAILABILITY,
    ShadowTradeExportRequest: DEFAULT_REQUEST_RESULT,
};

export const DEFAULT_IMPORT_DTO: GeneratedImportDto = {
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

export const DEFAULT_SCHEMES_DTO: GeneratedSchemesDto = {
    EmergencyFundWithdraw: 0,
    EmergencyFundBalance: 0,
    FuelSiphonPercent: 0,
    CorruptionWindowActive: false,
    EmergencyFundAvailability: DEFAULT_ACTION_AVAILABILITY,
    FuelSiphonAvailability: DEFAULT_ACTION_AVAILABILITY,
    CorruptionSchemeRequest: DEFAULT_REQUEST_RESULT,
};

export const DEFAULT_BUCKWHEAT_DTO: GeneratedBuckwheatDto = {
    BuckwheatTons: 0,
    ProcurementLevel: 0,
    DailyCost: 0,
    BaseDailyCost: 0,
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
    LastDistributeResult: DEFAULT_REQUEST_RESULT,
    ProcurementLevelRequest: DEFAULT_REQUEST_RESULT,
};

export const DEFAULT_COUNTERMEASURES_DTO: GeneratedCountermeasuresDto = {
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

export const DEFAULT_CRISIS_SWEEP_DTO: GeneratedCrisisSweepDto = {
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

export const DEFAULT_REPUTATION_DTO: GeneratedReputationDto = {
    TrustLevel: 0,
    TrustTier: "",
    IsFrozenOut: false,
    OfferFrequencyMult: 0,
};

export const DEFAULT_MAINTENANCE_DTO: GeneratedMaintenanceDto = {
    PendingProcurementOffer: null,
    ShadyContractCount: 0,
    TotalContractCount: 0,
    ActiveContractsJson: [],
    MaintenanceContractRequest: DEFAULT_REQUEST_RESULT,
};

export const DEFAULT_MOBILIZATION_DTO: GeneratedMobilizationDto = {
    ManpowerAvailable: 0,
    ManpowerUsed: 0,
    ManpowerTotal: 0,
    ManpowerPercent: 0,
    ManpowerBasePool: 0,
    ManpowerCasualties: 0,
    ManpowerPatriotismFactor: 0,
    ManpowerMoraleFactor: 0,
    ManpowerFatigueFactor: 0,
    IsConscriptionActive: false,
    IsWarFatigued: false,
    IsManpowerCritical: false,
    IsManpowerOvercommitted: false,
    CallToArmsOnCooldown: false,
    ConscriptionReactivationOnCooldown: false,
    PredictedConscriptionRelease: 0,
    SocialPenaltyProducerReady: false,
    SocialPenaltyReasonId: "",
    CanCallToArms: false,
    CallToArmsLockedReasonId: "",
    CanToggleConscription: false,
    ConscriptionLockedReasonId: "",
    WarDay: 0,
    CallToArmsRequest: DEFAULT_REQUEST_RESULT,
    ConscriptionToggleRequest: DEFAULT_REQUEST_RESULT,
};

export const DEFAULT_ATTENTION_DTO: GeneratedAttentionDto = {
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
    TotalExodus: 0,
};

export const DEFAULT_DONOR_DTO: GeneratedDonorDto = {
    DonorUsesRemaining: 0,
    DonorCooldownDays: 0,
    DonorStatus: "",
    TrustIndex: 0,
    ScandalPenalty: 0,
    DonorExpectedAid: "",
    DonorDialogActive: false,
    ProducerReady: false,
    TrustLocked: false,
    ProducerReasonId: "",
    DonorFundsAvailable: false,
    DonorFundsLockedReasonId: "",
    DonorPowerAvailable: false,
    DonorPowerLockedReasonId: "",
    DonorDefenseAvailable: false,
    DonorDefenseLockedReasonId: "",
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
};

export const DEFAULT_COGNITIVE_DTO: GeneratedCognitiveDto = {
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
    CanDeployHero: false,
    DeployHeroLockedReasonId: "",
    CanRecallHero: false,
    RecallHeroLockedReasonId: "",
    CanSetHeroCounter: false,
    SetHeroCounterLockedReasonId: "",
    CanSetHeroLecturing: false,
    SetHeroLecturingLockedReasonId: "",
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
};

export const DEFAULT_GRID_WARFARE_DTO: GeneratedGridWarfareDto = {
    ShadowBalance: 0,
    ShadowLocked: 0,
    ShadowTotal: 0,
    EnemyPhysicalAxis: 0,
    EnemyDigitalAxis: 0,
    EnemySocialAxis: 0,
    EnemyInterceptChance: 0,
    RespitePhysicalActive: false,
    RespiteDigitalActive: false,
    RespiteSocialActive: false,
    ObjectiveProgress: 0,
    DroneStock: 0,
    BallisticStock: 0,
    CanPrepareDrone: false,
    PrepareDroneLockedReasonId: "",
    CanPrepareBlackout: false,
    PrepareBlackoutLockedReasonId: "",
    CanPrepareDisinfo: false,
    PrepareDisinfoLockedReasonId: "",
    CityStability: 0,
    StabilityDiscount: 0,
    OperationSlots: [],
    AttackCosts: DEFAULT_ATTACK_COSTS,
    OperationRequest: DEFAULT_REQUEST_RESULT,
    GridWarfareUnlocked: false,
};

export const DEFAULT_NEWS_DTO: GeneratedNewsDto = {
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
};

export type DebugToggleGroup = "threat" | "domain" | "core" | "sub";

export interface DebugToggleEntry {
    key: string;
    enabled: boolean;
    canDisable: boolean;
    lockedReasonId: string;
    group: DebugToggleGroup;
    parent: string;
    systemCount: number;
}

export interface DebugToggleSnapshot {
    version: number;
    entries: DebugToggleEntry[];
}

export const DEFAULT_DEBUG_TOGGLE_SNAPSHOT: DebugToggleSnapshot = {
    version: 0,
    entries: [],
};

const isDebugToggleGroup = (value: unknown): value is DebugToggleGroup =>
    value === "threat" || value === "domain" || value === "core" || value === "sub";

export function isDebugToggleEntry(value: unknown): value is DebugToggleEntry {
    if (!isRecord(value)) return false;
    const record = value;
    return typeof record.key === "string"
        && typeof record.enabled === "boolean"
        && typeof record.canDisable === "boolean"
        && typeof record.lockedReasonId === "string"
        && isDebugToggleGroup(record.group)
        && typeof record.parent === "string"
        && typeof record.systemCount === "number";
}

export function isDebugToggleSnapshot(value: unknown): value is DebugToggleSnapshot {
    if (!isRecord(value)) return false;
    const record = value;
    return typeof record.version === "number"
        && Array.isArray(record.entries)
        && record.entries.every(isDebugToggleEntry);
}
