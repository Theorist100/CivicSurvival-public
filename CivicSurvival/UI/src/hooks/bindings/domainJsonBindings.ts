/**
 * Domain JSON bindings — one per CivicUIPanelSystem.
 * Each binding carries a single JSON string that the corresponding
 * domain hook parses via useMemo + safeJsonParse.
 *
 * Replaces ~291 individual ValueBindings with 18 JSON string bindings.
 */

import { bindCivicValue } from "../typedBinding.generated";
import { B } from "../bindingNames.generated";

// Power
export const gridState$ = bindCivicValue(B.PowerGridState, "{}");
export const backupState$ = bindCivicValue(B.BackupPowerState, "{}");

// Shadow Economy
export const exportState$ = bindCivicValue(B.ExportState, "{}");
export const importState$ = bindCivicValue(B.ImportState, "{}");

// Corruption
export const schemesState$ = bindCivicValue(B.SchemesState, "{}");
export const countermeasuresState$ = bindCivicValue(B.CountermeasuresState, "{}");
export const reputationState$ = bindCivicValue(B.ReputationState, "{}");
export const maintenanceState$ = bindCivicValue(B.MaintenanceState, "{}");

// Threats & Defense
export const threatState$ = bindCivicValue(B.ThreatState, "{}");
export const airDefenseState$ = bindCivicValue(B.AirDefenseState, "{}");
// Static map geometry { coast, water } (world-space polylines) — published once per
// loaded city, separate from the per-frame ThreatState payload.
export const mapContour$ = bindCivicValue(B.MapContour, "{}");
export const intelState$ = bindCivicValue(B.IntelState, "{}");
export const spotterState$ = bindCivicValue(B.SpotterState, "{}");

// Military
export const mobilizationState$ = bindCivicValue(B.MobilizationState, "{}");

// Attention
export const attentionState$ = bindCivicValue(B.AttentionState, "{}");

// Diplomacy
export const donorState$ = bindCivicValue(B.DonorState, "{}");

// Finance
export const financeState$ = bindCivicValue(B.FinanceState, "{}");

// Cognitive
export const cognitiveState$ = bindCivicValue(B.CognitiveState, "{}");
export const buckwheatState$ = bindCivicValue(B.BuckwheatState, "{}");

// Grid Warfare
export const gridWarfareState$ = bindCivicValue(B.GridWarfareState, "{}");

// Network
export const newsState$ = bindCivicValue(B.NewsState, "{}");

// Settings
export const settingsState$ = bindCivicValue(B.SettingsState, "{}");
export const settingsLocalizationState$ = bindCivicValue(B.SettingsLocalizationState, "{}");
