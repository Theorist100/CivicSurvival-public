/**
 * Domain hooks — JSON bindings parsed through the useDtoBinding seam.
 */

// Power
export { usePowerGrid, useBackupPower, useShadowPrograms, usePowerGridState, useBackupPowerState } from "./usePowerDomain";

// Corruption / Shadow Economy
export {
    useExport, useImport, useSchemes, useBuckwheat, useCountermeasures,
    useReputation, useMaintenance,
} from "./useCorruptionDomain";

// Threats & Defense
export { useThreat, useAirDefense, useSpotters, useThreatState, useAirDefenseState } from "./useThreatDomain";
export { useDefenseData } from "./useDefenseData";

// Standalone domains
export { useMobilizationDomain } from "./useMobilization";
export { useAttention } from "./useAttention";
export { useDonor } from "./useDonor";
export { useDonorData } from "./useDonorData";
export { useFinance } from "./useFinance";
export { useFinanceContext } from "./useFinanceContext";
export { useIntel, useIntelState } from "./useIntel";
export { useCognitive } from "./useCognitive";
export {
    HeroStatus, ProtestRisk, InternetMode, NarrativeMode,
    getHeroStatusLabelKey, getProtestRiskLabelKey,
    type HeroStatusType, type ProtestRiskType,
    type InternetModeType, type NarrativeModeType,
} from "./cognitiveLabels";
export { useGridWarfareDomain } from "./useGridWarfare";
export { useSettings } from "./useSettings";
export { bindingDataOrDefault, mapBindingState, useDtoBinding, type BindingState } from "./useDtoBinding";

// Re-export DTO types for consumers
export type {
    PowerGridDto, BackupPowerDto,
    ExportDto, ImportDto, SchemesDto, BuckwheatDto, CountermeasuresDto, ReputationDto, MaintenanceDto,
    ThreatDto, AirDefenseDto, IntelDto, SpotterDto, FocusRange, AttackTimeEstimate,
    MobilizationDto, AttentionDto, DonorDto, FinanceDto,
    CognitiveDto, GridWarfareDto,
    EnemyStance, StancePhase,
    NewsDto, SettingsDto,
} from "../../types/domainDtos";

export type {
    MapBoundsDto, RadarTargetDto, RadarThreatDto, RadarInterceptionDto,
    ThreatTargetDto,
} from "../../types/domainDtos.generated";
