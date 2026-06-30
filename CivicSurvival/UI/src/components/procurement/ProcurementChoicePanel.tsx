/**
 * ProcurementChoicePanel - Maintenance contract choice UI
 *
 * Shows side-by-side comparison of:
 * - Official vendor (expensive, 98% reliable)
 * - Shady vendor (cheap + kickback, 70% reliable)
 *
 * Consequences:
 * - Shady contracts increase disaster chance by (1 - quality) * 100%
 * - Kickback goes to offshore account
 * - +15 corruption score per shady contract
 */

import React, { memo, useMemo, useCallback } from "react";
import { useTheme, useAccents, formatMoney } from "../../themes";
import { createStyles } from "./ProcurementChoicePanel.styles";
import { useMaintenance } from "@hooks/domain";
import {
    parseProcurementOffer,
} from "../../hooks/bindings/procurementBindings";
import { IconAlert, IconPhone } from "../shared/common/Icons";
import { HelpSection } from "../shared/common/HelpSection";
import { InlineWarning, ProgressBar, SectionHeader, StatRow } from "../shared/ui";
import { useLocale } from "../../locales";
import { useCorruptionActions, useRequestActionPerOffer } from "@hooks/actions";

const formatPercent = (value: number): string =>
    `${Math.round(value * 100)}%`;

const getVendorMetricColor = (
    highlight: boolean,
    isGood: boolean,
    t: ReturnType<typeof useTheme>,
    accents: ReturnType<typeof useAccents>,
) => highlight ? (isGood ? accents.schemes.accent : accents.crisis.accent) : t.colors.textPrimary;

export const ProcurementChoicePanel: React.FC = memo(() => {
    const t = useTheme();
    const accents = useAccents();
    const l = useLocale();
    const s = useMemo(() => createStyles(t, accents), [t, accents]);
    const maintenanceState = useMaintenance();
    const actions = useCorruptionActions();
    const maintenance = maintenanceState.status === "ready" ? maintenanceState.data : null;
    const offer = useMemo(
        () => maintenance ? parseProcurementOffer(maintenance.PendingProcurementOffer) : null,
        [maintenance],
    );
    const requestResult = maintenance?.MaintenanceContractRequest;
    const offerKey = offer ? `${offer.entityIndex}:${offer.entityVersion}` : "";

    const handleAcceptOfficial = useCallback(() => {
        if (!offer) return false;
        actions.acceptOfficialContract(offer.entityIndex, offer.entityVersion, offer.officialPrice);
        return true;
    }, [actions, offer]);

    const handleAcceptShady = useCallback(() => {
        if (!offer) return false;
        actions.acceptShadyContract(offer.entityIndex, offer.entityVersion, offer.shadyPrice);
        return true;
    }, [actions, offer]);

    const handleDecline = useCallback(() => {
        if (!offer) return false;
        actions.declineProcurement(offer.entityIndex, offer.entityVersion);
        return true;
    }, [actions, offer]);

    const officialAction = useRequestActionPerOffer(handleAcceptOfficial, requestResult, offerKey);
    const shadyAction = useRequestActionPerOffer(handleAcceptShady, requestResult, offerKey);
    const declineAction = useRequestActionPerOffer(handleDecline, requestResult, offerKey);
    const actionPending = officialAction.isPending || shadyAction.isPending || declineAction.isPending;
    const shadyDisabled = actionPending || !offer?.canAcceptShady;

    if (!offer) return null;

    const isSupplyContract = offer.contractType === "Supply";
    const titleKey = isSupplyContract ? "UI_PROC_MODAL_TITLE_SUPPLY" : "UI_PROC_MODAL_TITLE_MAINTENANCE";
    const descriptionKey = isSupplyContract ? "UI_PROC_DESCRIPTION_SUPPLY" : "UI_PROC_DESCRIPTION_MAINTENANCE";
    const riskKey = isSupplyContract ? "UI_PROC_STAT_EFFICIENCY_RISK" : "UI_PROC_STAT_DISASTER_RISK";
    const riskNormalKey = isSupplyContract ? "UI_PROC_STAT_EFFICIENCY_NORMAL" : "UI_PROC_STAT_NORMAL";

    // Low shady quality increases risk/loss; label depends on contract type.
    const disasterRiskIncrease = Math.round((1 - offer.shadyQuality) * 100);

    const vendorMetricStyle = (highlight: boolean): React.CSSProperties => ({
        fontSize: t.typography.sizeSM,
        fontWeight: highlight ? t.typography.weightBold : t.typography.weightNormal,
    });
    const vendorLabelStyle: React.CSSProperties = {
        minWidth: 0,
        fontSize: t.typography.sizeSM,
        textTransform: "none",
        color: t.colors.textSecondary,
    };
    const vendorRowStyle: React.CSSProperties = { marginBottom: t.spacing.xs };
    const qualityStyle: React.CSSProperties = {
        backgroundColor: t.colors.surface,
        marginTop: t.spacing.xs,
    };

    return (
        <div style={s.overlay}>
            <div style={s.panel} data-civic-delegated-click onClick={(e) => e.stopPropagation()}>
                {/* Header */}
                <div style={s.header}>
                    <span style={{ ...s.headerIcon, ...s.headerChild }}><IconPhone /></span>
                    <div style={s.headerTitle}>
                        <SectionHeader
                            title={l.t(titleKey)}
                            titleAs="h2"
                            titleStyle={s.title}
                            help={<HelpSection id="procurement" title={l.t("UI_PROCUREMENT_TITLE")}>{l.t("HELP_PROCUREMENT")}</HelpSection>}
                        />
                        <p style={s.subtitle}>{offer.buildingName}</p>
                    </div>
                </div>

                {/* Body */}
                <div style={s.body}>
                    <p style={s.description}>
                        {l.t(descriptionKey, offer.buildingName)}
                    </p>

                    {/* Vendor Comparison */}
                    <div style={s.vendorsContainer}>
                        {/* Official Vendor */}
                        <div
                            style={{ ...s.vendorCard(false), ...s.vendorsContainerChild, opacity: actionPending ? 0.65 : 1 }}
                            role="button"
                            tabIndex={actionPending ? -1 : 0}
                            onClick={actionPending ? undefined : officialAction.execute}
                            onKeyDown={(e) => {
                                if (!actionPending && (e.key === "Enter" || e.key === " ")) {
                                    e.preventDefault();
                                    officialAction.execute();
                                }
                            }}
                        >
                            <div style={s.vendorHeader(false)}>
                                <span style={s.vendorType(false)}>{l.t("UI_PROC_VENDOR_OFFICIAL")}</span>
                                <span style={s.vendorBadge(false)}>{l.t("UI_PROC_BADGE_RECOMMENDED")}</span>
                            </div>
                            <div style={s.vendorName}>{offer.officialVendorName}</div>

                            <StatRow
                                compact
                                label={l.t("UI_PROC_STAT_ANNUAL_COST")}
                                value={formatMoney(offer.officialPrice)}
                                color={getVendorMetricColor(false, true, t, accents)}
                                style={vendorRowStyle}
                                labelStyle={vendorLabelStyle}
                                valueStyle={vendorMetricStyle(false)}
                            />

                            <StatRow
                                compact
                                label={l.t("UI_PROC_STAT_RELIABILITY")}
                                value={formatPercent(offer.officialQuality)}
                                color={getVendorMetricColor(true, true, t, accents)}
                                style={vendorRowStyle}
                                labelStyle={vendorLabelStyle}
                                valueStyle={vendorMetricStyle(true)}
                            />
                            <ProgressBar
                                value={offer.officialQuality * 100}
                                color={accents.schemes.accent}
                                height="8rem"
                                style={qualityStyle}
                            />

                            <StatRow
                                compact
                                label={l.t(riskKey)}
                                value={l.t(riskNormalKey)}
                                color={getVendorMetricColor(false, true, t, accents)}
                                style={vendorRowStyle}
                                labelStyle={vendorLabelStyle}
                                valueStyle={vendorMetricStyle(false)}
                            />
                        </div>

                        {/* Shady Vendor */}
                        <div
                            style={{ ...s.vendorCard(true), opacity: shadyDisabled ? 0.65 : 1 }}
                            role="button"
                            tabIndex={shadyDisabled ? -1 : 0}
                            onClick={shadyDisabled ? undefined : shadyAction.execute}
                            onKeyDown={(e) => {
                                if (!shadyDisabled && (e.key === "Enter" || e.key === " ")) {
                                    e.preventDefault();
                                    shadyAction.execute();
                                }
                            }}
                        >
                            <div style={s.vendorHeader(true)}>
                                <span style={s.vendorType(true)}>{l.t("UI_PROC_VENDOR_SHADY")}</span>
                                <span style={s.vendorBadge(true)}>{l.t("UI_PROC_BADGE_RISKY")}</span>
                            </div>
                            <div style={s.vendorName}>{offer.shadyVendorName}</div>

                            <StatRow
                                compact
                                label={l.t("UI_PROC_STAT_ANNUAL_COST")}
                                value={formatMoney(offer.shadyPrice)}
                                color={getVendorMetricColor(true, true, t, accents)}
                                style={vendorRowStyle}
                                labelStyle={vendorLabelStyle}
                                valueStyle={vendorMetricStyle(true)}
                            />

                            <StatRow
                                compact
                                label={l.t("UI_PROC_STAT_RELIABILITY")}
                                value={formatPercent(offer.shadyQuality)}
                                color={getVendorMetricColor(true, false, t, accents)}
                                style={vendorRowStyle}
                                labelStyle={vendorLabelStyle}
                                valueStyle={vendorMetricStyle(true)}
                            />
                            <ProgressBar
                                value={offer.shadyQuality * 100}
                                color={accents.crisis.accent}
                                height="8rem"
                                style={qualityStyle}
                            />

                            <StatRow
                                compact
                                label={l.t(riskKey)}
                                value={`+${disasterRiskIncrease}%`}
                                color={getVendorMetricColor(true, false, t, accents)}
                                style={vendorRowStyle}
                                labelStyle={vendorLabelStyle}
                                valueStyle={vendorMetricStyle(true)}
                            />

                            {/* Kickback highlight */}
                            <div style={s.kickbackRow}>
                                <StatRow
                                    compact
                                    label={l.t("UI_PROC_STAT_YOUR_CUT")}
                                    value={`+${formatMoney(offer.kickbackOffer)}`}
                                    labelStyle={s.kickbackLabel}
                                    valueStyle={s.kickbackValue}
                                />
                            </div>
                            {!offer.canAcceptShady && (
                                <InlineWarning
                                    accent={accents.crisis.accent}
                                    icon={<IconAlert />}
                                    style={{ marginTop: t.spacing.sm, marginBottom: 0 }}
                                >
                                    {l.tDynamic(offer.acceptShadyLockedReasonId)}
                                </InlineWarning>
                            )}
                        </div>
                    </div>

                    {/* Warning */}
                    <InlineWarning
                        accent={accents.crisis.accent}
                        icon={<IconAlert />}
                        style={{ marginTop: 0, marginBottom: t.spacing.md }}
                    >
                        {l.t("UI_PROC_WARNING")}
                    </InlineWarning>

                    {/* Action Buttons */}
                    <div style={s.actions}>
                        <button style={{ ...s.button("decline"), ...s.actionsChild }} disabled={actionPending} onClick={actionPending ? undefined : declineAction.execute}>
                            {actionPending ? l.t("UI_PROCESSING") : l.t("UI_PROC_BTN_DECLINE")}
                        </button>
                        <button style={{ ...s.button("official"), ...s.actionsChild }} disabled={actionPending} onClick={actionPending ? undefined : officialAction.execute}>
                            {actionPending ? l.t("UI_PROCESSING") : l.t("UI_PROC_BTN_ACCEPT_OFFICIAL")}
                        </button>
                        <button style={s.button("shady")} disabled={shadyDisabled} onClick={shadyDisabled ? undefined : shadyAction.execute}>
                            {actionPending ? l.t("UI_PROCESSING") : l.t("UI_PROC_BTN_ACCEPT_SHADY")}
                        </button>
                    </div>
                </div>
            </div>
        </div>
    );
});
ProcurementChoicePanel.displayName = "ProcurementChoicePanel";
