/**
 * MarketSection - Compact Import/Export controls
 * Shows shadow import level and export percent with quick presets
 */

import React, { useMemo, useCallback, useRef } from "react";
import { Row } from "../../coherent";
import { useTheme, useAccents, formatCostArg, formatMoney } from "../../../themes";
import { bindingDataOrDefault, useImport, useExport } from "@hooks/domain";
import { useCorruptionActions, useRequestAction } from "@hooks/actions";
import { createStyles } from "./SectionStyles";
import { useLocale } from "../../../locales";
import { GlassCase, SectionHeader } from "../../shared/ui";
import { HelpSection } from "../../shared/common/HelpSection";
import { asPercentValue } from "../../../types/semantic";
import { DEFAULT_EXPORT_DTO, DEFAULT_IMPORT_DTO, WAVE_LOCKED_REASON_ID, type FreezeReason } from "../../../types/domainDtos";
import { decodeFreezeReason, FreezeReasonLocaleKeys } from "../../../types/sharedEnums.generated";

type LocaleApi = ReturnType<typeof useLocale>;
type CorruptionActions = ReturnType<typeof useCorruptionActions>;

const MARKET_PRESETS = [0, 25, 50, 75, 100];

const getFreezeReasonText = (reason: FreezeReason, l: LocaleApi): string => {
    const flags = decodeFreezeReason(reason);
    return flags.length > 0
        ? flags.map(flag => l.tDynamic(FreezeReasonLocaleKeys[flag] ?? "UI_MARKET_FREEZE_FROZEN")).join(" + ")
        : l.t("UI_MARKET_FREEZE_FROZEN");
};

const getMarketLockedText = (
    isFrozen: boolean | undefined,
    freezeReason: FreezeReason,
    lockedReasonId: string,
    l: LocaleApi,
): string => {
    if (isFrozen) return getFreezeReasonText(freezeReason, l);
    if (lockedReasonId && lockedReasonId !== WAVE_LOCKED_REASON_ID) return l.tDynamic(lockedReasonId);
    // No freeze flag and no specific cause (empty or the generic wave-preview
    // placeholder). Emit nothing: a red status line here would duplicate the
    // GlassCase WAVE badge and push the section into overflow.
    return "";
};

interface MarketSectionContentProps {
    actions: CorruptionActions;
}

const MarketSectionContent: React.FC<MarketSectionContentProps> = ({ actions }) => {
    const theme = useTheme();
    const accents = useAccents();
    const l = useLocale();
    const s = useMemo(() => createStyles(theme), [theme]);
    const impState = useImport();
    const expState = useExport();
    const imp = bindingDataOrDefault(impState, DEFAULT_IMPORT_DTO);
    const exp = bindingDataOrDefault(expState, DEFAULT_EXPORT_DTO);
    const importPresetRef = useRef(asPercentValue(0));
    const exportPercentRef = useRef(asPercentValue(0));
    const importAction = useRequestAction(() => {
        actions.setShadowImportPreset(importPresetRef.current);
        return true;
    }, imp?.ShadowTradeImportRequest);
    const exportAction = useRequestAction(() => {
        actions.setExportPercent(exportPercentRef.current);
        return true;
    }, exp?.ShadowTradeExportRequest);

    // Import data (use ?? for numeric values that can legitimately be 0)
    const shadowImportMW = imp?.ShadowImportMW ?? 0;
    const maxImportMW = imp?.MaxShadowImportMW ?? 0;
    const importCost = imp?.ShadowImportCost ?? 0;
    const discoveryRisk = imp?.DiscoveryRisk ?? 0;
    const importAvailability = imp?.ShadowImportAvailability;
    const shadowImportAvailable = importAvailability?.CanRun ?? false;
    const importLockedReasonId = importAvailability?.LockedReasonId ?? "";
    const importFrozen = imp?.IsFrozen;
    const importFreezeReason = imp?.FreezeReason ?? 0;
    const selectedImportPresetIndex = imp?.SelectedPresetIndex ?? -1;
    const importPending = importAction.isPending || imp?.ShadowTradeImportRequest.Status === "pending";
    const importError = imp?.ShadowTradeImportRequest.Status === "failed" && imp.ShadowTradeImportRequest.ReasonId
        ? l.tDynamic(imp.ShadowTradeImportRequest.ReasonId)
        : "";

    // Export data (use ?? for numeric values that can legitimately be 0)
    const exportPercent = exp?.ExportPercent ?? 0;
    const exportedMW = exp?.ExportedMW ?? 0;
    const dailyIncome = exp?.DailyIncome ?? 0;
    const isFrozen = exp?.IsFrozen;
    const freezeReason = exp?.FreezeReason ?? 0;
    const exportAvailability = exp?.ExportAvailability;
    const shadowExportAvailable = exportAvailability?.CanRun ?? false;
    const exportPending = exportAction.isPending || exp?.ShadowTradeExportRequest.Status === "pending";
    const exportLockedReasonId = exportAvailability?.LockedReasonId ?? "";
    const exportError = exp?.ShadowTradeExportRequest.Status === "failed" && exp.ShadowTradeExportRequest.ReasonId
        ? l.tDynamic(exp.ShadowTradeExportRequest.ReasonId)
        : "";

    const importUnavailableText = getMarketLockedText(importFrozen, importFreezeReason, importLockedReasonId, l);
    const exportUnavailableText = getMarketLockedText(isFrozen, freezeReason, exportLockedReasonId, l);
    const importControlsDisabled = !shadowImportAvailable || importPending;
    const exportControlsDisabled = !shadowExportAvailable || exportPending;

    // Stable handlers using data attributes to avoid inline closures in map
    const handleImportClick = useCallback((e: React.MouseEvent<HTMLButtonElement>) => {
        const pct = Number(e.currentTarget.dataset.pct);
        if (!Number.isNaN(pct)) {
            importPresetRef.current = asPercentValue(pct);
            importAction.execute();
        }
    }, [importAction]);

    const handleExportClick = useCallback((e: React.MouseEvent<HTMLButtonElement>) => {
        if (!shadowExportAvailable || exportPending) return;
        const preset = Number(e.currentTarget.dataset.preset);
        if (!Number.isNaN(preset)) {
            exportPercentRef.current = asPercentValue(preset);
            exportAction.execute();
        }
    }, [exportAction, exportPending, shadowExportAvailable]);

    // Risk color
    const riskColor = discoveryRisk >= 0.5 ? theme.colors.errorBright
        : discoveryRisk >= 0.2 ? accents.crisis.accent
        : discoveryRisk >= 0.05 ? accents.resilience.accent
        : theme.colors.textMuted;

    // ========================================================================
    // RENDER
    // ========================================================================

    return (
        <div style={s.market.container}>
            <SectionHeader
                title={l.t("UI_MARKET_TITLE")}
                titleStyle={s.market.title}
                help={<HelpSection id="market" title={l.t("UI_MARKET_TITLE")}>{l.t("HELP_MARKET")}</HelpSection>}
            />

            {/* IMPORT ROW */}
            <div style={s.market.row}>
                <div style={s.market.info}>
                    <span style={s.market.label}>
                        {l.t("UI_MARKET_SHADOW_IMPORT")}
                        <span style={{ color: theme.colors.textMuted, fontWeight: 400, marginLeft: "6rem" }}>
                            {l.t("UI_MARKET_MAX_MW", maxImportMW)}
                        </span>
                    </span>
                    <span style={s.market.price}>
                        {l.t("UI_MARKET_IMPORT_MW", shadowImportMW)}
                        {importCost > 0 && (
                            <span style={{ color: accents.crisis.accent, marginLeft: "8rem" }}>
                                {l.t("UI_MARKET_COST_DAY", formatCostArg(importCost))}
                            </span>
                        )}
                    </span>
                </div>
                <div style={{ display: "flex", flexDirection: "column" as const, minHeight: 0, alignItems: "flex-end" }}>
                    {/* Risk indicator - inline */}
                    <div style={{ fontSize: theme.typography.sizeXS, color: riskColor, marginBottom: "4rem" }}>
                        {l.t("UI_MARKET_IMPORT_RISK")} {discoveryRisk > 0 ? `${Math.round(discoveryRisk * 100)}%` : l.t("UI_MARKET_NA")}
                    </div>
                    <Row>
                        {MARKET_PRESETS.map((pct, idx) => {
                            const isSelected = selectedImportPresetIndex === idx;
                            return (
                                <button
                                    key={pct}
                                    style={{
                                        ...s.market.button("buy"),
                                        backgroundColor: isSelected
                                            ? accents.crisis.accent
                                            : "rgba(255, 255, 255, 0.1)",
                                        color: isSelected
                                            ? theme.colors.white
                                            : theme.colors.textSecondary,
                                        marginRight: idx < MARKET_PRESETS.length - 1 ? "4rem" : "0rem",
                                        opacity: importControlsDisabled ? 0.55 : 1,
                                        cursor: importControlsDisabled ? "not-allowed" : "pointer",
                                    }}
                                    data-pct={pct}
                                    disabled={importControlsDisabled}
                                    onClick={!importControlsDisabled ? handleImportClick : undefined}
                                >
                                    {`${pct}%`}
                                </button>
                            );
                        })}
                    </Row>
                    {!shadowImportAvailable && importUnavailableText && (
                        <div style={{ marginTop: "4rem", color: accents.crisis.accent, fontSize: theme.typography.sizeXS, fontWeight: 700 }}>
                            {importUnavailableText}
                        </div>
                    )}
                    {importPending && (
                        <div style={{ marginTop: "4rem", color: theme.colors.textMuted, fontSize: theme.typography.sizeXS, fontWeight: 700 }}>
                            {l.t("UI_PROCESSING")}
                        </div>
                    )}
                    {importError && (
                        <div style={{ marginTop: "4rem", color: accents.crisis.accent, fontSize: theme.typography.sizeXS, fontWeight: 700 }}>
                            {importError}
                        </div>
                    )}
                </div>
            </div>

            {/* EXPORT ROW */}
            <div style={s.market.row}>
                <div style={s.market.info}>
                    <span style={s.market.label}>
                        {l.t("UI_MARKET_SHADOW_EXPORT")}
                        <span style={{ color: theme.colors.textMuted, fontWeight: 400, marginLeft: "6rem" }}>
                            {l.t("UI_MARKET_SURPLUS")}
                        </span>
                    </span>
                    <span style={s.market.price}>
                        {l.t("UI_MARKET_EXPORT_VALUE", exportPercent, exportedMW)}
                    </span>
                </div>
                <div style={{ display: "flex", flexDirection: "column" as const, minHeight: 0, alignItems: "flex-end" }}>
                    {/* Income indicator */}
                    <div style={{ fontSize: theme.typography.sizeXS, color: dailyIncome > 0 ? accents.schemes.accent : theme.colors.textMuted, marginBottom: "4rem" }}>
                        {l.t("UI_MARKET_INCOME")} {dailyIncome > 0 ? `+${formatMoney(dailyIncome)}${l.t("UI_UNIT_PER_DAY")}` : l.t("UI_MARKET_NA")}
                    </div>
                    <Row>
                        {MARKET_PRESETS.map((preset, idx) => (
                            <button
                                key={preset}
                                style={{
                                    ...s.market.button("sell"),
                                    backgroundColor: exportPercent === preset
                                        ? accents.schemes.accent
                                        : "rgba(255, 255, 255, 0.1)",
                                    color: exportPercent === preset
                                        ? theme.colors.white
                                        : theme.colors.textSecondary,
                                    marginRight: idx < MARKET_PRESETS.length - 1 ? "4rem" : "0rem",
                                    opacity: exportControlsDisabled ? 0.55 : 1,
                                    cursor: exportControlsDisabled ? "not-allowed" : "pointer",
                                }}
                                data-preset={preset}
                                disabled={exportControlsDisabled}
                                onClick={!exportControlsDisabled ? handleExportClick : undefined}
                            >
                                {`${preset}%`}
                            </button>
                        ))}
                    </Row>
                    {!shadowExportAvailable && exportUnavailableText && (
                        <div style={{ marginTop: "4rem", color: accents.crisis.accent, fontSize: theme.typography.sizeXS, fontWeight: 700 }}>
                            {exportUnavailableText}
                        </div>
                    )}
                    {exportPending && (
                        <div style={{ marginTop: "4rem", color: theme.colors.textMuted, fontSize: theme.typography.sizeXS, fontWeight: 700 }}>
                            {l.t("UI_PROCESSING")}
                        </div>
                    )}
                    {exportError && (
                        <div style={{ marginTop: "4rem", color: accents.crisis.accent, fontSize: theme.typography.sizeXS, fontWeight: 700 }}>
                            {exportError}
                        </div>
                    )}
                </div>
            </div>
        </div>
    );
};

export const MarketSection: React.FC = () => {
    const actions = useCorruptionActions();
    return (
        <GlassCase
            feature="ShadowEconomy"
            name="Shadow Market"
            description="Buy power from the black market to plug deficits, or sell surplus for offshore cash. Trading on the shadow market raises discovery risk over time."
        >
            <MarketSectionContent actions={actions} />
        </GlassCase>
    );
};
