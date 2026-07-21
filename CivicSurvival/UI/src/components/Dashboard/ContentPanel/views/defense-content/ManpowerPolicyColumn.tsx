import React, { useRef } from "react";
import { Column, Row } from "@coherent";
import { useAccents, useTheme } from "@themes";
import type { AirDefenseDto, MobilizationDto } from "@hooks/domain";
import { bindingDataOrDefault, useSchemes } from "@hooks/domain";
import { useLocale } from "@locales";
import { ActionButton } from "@shared/common/ActionButton";
import { HoverTip, HoverTipTarget } from "@shared/common/HoverTip";
import { InlineWarning, ProgressBar, StatRow } from "@shared/ui";
import { useCorruptionActions, useRequestAction } from "@hooks/actions";
import { asPercentValue, type DefensePolicyId } from "../../../../../types/semantic";
import { DEFAULT_SCHEMES_DTO } from "../../../../../types/domainDtos";
import type { AsyncAction, CallToArmsValidator, WarViewStyles } from "./types";
import { type useDefenseActions } from "@hooks/actions";

const DRAFT_DEFERRAL_LEVELS = [0, 15, 30, 50];

interface ManpowerPolicyColumnProps {
    aa: AirDefenseDto;
    actions: ReturnType<typeof useDefenseActions>;
    manpower: MobilizationDto;
    manpowerColor: string;
    callToArms: CallToArmsValidator;
    callToArmsAction: AsyncAction;
    conscriptionAction: AsyncAction;
    patriotDroneToggleAction: AsyncAction;
    styles: WarViewStyles;
}

/** Small circular "?" that reveals explanatory copy on hover — keeps standing descriptions
 * out of the vertical flow so the column fits without scrolling. */
const InfoTip: React.FC<{ text: string; color: string }> = ({ text, color }) => (
    <HoverTip text={text}>
        <span style={{
            // flex, not inline-flex: Coherent rejects inline-flex ("display invalid")
            display: "flex",
            alignItems: "center",
            justifyContent: "center",
            width: "14rem",
            height: "14rem",
            marginLeft: "6rem",
            borderRadius: "50%",
            border: `1rem solid ${color}`,
            color,
            fontSize: "10rem",
            fontWeight: 700,
            lineHeight: 1,
        }}>?</span>
    </HoverTip>
);

/** One highlighted-when-active segment in a policy toggle.
 *
 * `disabled` dims + locks the segment (request in flight OR unavailable). `busy` marks
 * the ACTIVE segment of a control whose request is in flight: it stays prominent and grows
 * a progress underline, while the rest just dim. The label is never swapped for "Processing"
 * — that overflowed the narrow cells and made every segment read "Processing" at once. */
const SegmentButton: React.FC<{
    label: string;
    color: string;
    active: boolean;
    disabled: boolean;
    busy?: boolean;
    onClick: () => void;
    styleFn: (color: string, opacity: number) => React.CSSProperties;
}> = ({ label, color, active, disabled, busy = false, onClick, styleFn }) => {
    const opacity = busy ? (active ? 0.85 : 0.3) : disabled ? 0.35 : active ? 1 : 0.5;
    return (
        <button
            style={{ ...styleFn(color, opacity), position: "relative", overflow: "hidden" }}
            disabled={disabled}
            onClick={onClick}
        >
            {label}
            {busy && active && (
                <span style={{
                    position: "absolute",
                    left: 0,
                    right: 0,
                    bottom: 0,
                    height: "2rem",
                    background: color,
                }} />
            )}
        </button>
    );
};

/** Compact one-line policy control: label (with hover "?" help) on the left, a fixed-width
 * button cluster on the right. Collapses a full title row + a tall full-width button pair
 * into a single row so the column fits without a scrollbar. */
const PolicyRow: React.FC<{
    label: string;
    color: string;
    help: string;
    controlWidth: string;
    styles: WarViewStyles;
    children: React.ReactNode;
}> = ({ label, color, help, controlWidth, styles: s, children }) => (
    <Row align="center" style={{ marginTop: "12rem" }}>
        <div style={{ display: "flex", alignItems: "center", flex: "1 1 auto", minWidth: 0 }}>
            <span style={{
                ...s.subsectionTitle(color),
                marginBottom: 0,
                whiteSpace: "nowrap",
                overflow: "hidden",
                textOverflow: "ellipsis",
            }}>{label}</span>
            <InfoTip text={help} color={color} />
        </div>
        <div style={{ display: "flex", flex: "0 0 auto", marginLeft: "8rem", width: controlWidth }}>
            {children}
        </div>
    </Row>
);

export const ManpowerPolicyColumn: React.FC<ManpowerPolicyColumnProps> = ({
    aa,
    actions,
    manpower,
    manpowerColor,
    callToArms,
    callToArmsAction,
    conscriptionAction,
    patriotDroneToggleAction,
    styles: s,
}) => {
    const theme = useTheme();
    const accents = useAccents();
    const l = useLocale();

    // Compact button style for the inline PolicyRow clusters (OFF/ON): tighter padding than
    // the full-width s.buttonWithOpacity so two options fit a ~104rem right-hand slot. A small
    // marginRight separates the pair; the trailing margin sits harmlessly inside the fixed slot.
    const inlineBtn = (color: string, opacity: number): React.CSSProperties => ({
        flex: 1,
        padding: "6rem 4rem",
        marginRight: "4rem",
        background: opacity === 1 ? color : "transparent",
        border: `2rem solid ${color}`,
        borderRadius: theme.layout.borderRadius,
        color: opacity === 1 ? theme.colors.paper : color,
        cursor: "pointer",
        fontSize: theme.typography.sizeXS,
        fontWeight: 600,
        textAlign: "center",
        opacity: opacity === 1 ? 1 : opacity,
    });

    const defensePolicyRef = useRef(aa.DefensePolicyId);
    const defensePolicyAction = useRequestAction(() => {
        actions.setDefensePolicy(defensePolicyRef.current);
        return true;
    }, aa.DefensePolicyRequest);
    const defensePolicyPending = defensePolicyAction.isPending;
    const defensePolicyError =
        aa.DefensePolicyRequest.Status === "failed" && aa.DefensePolicyRequest.ReasonId
            ? aa.DefensePolicyRequest.ReasonId
            : "";

    const setDefensePolicy = (policyId: DefensePolicyId) => {
        if (defensePolicyPending) return;
        defensePolicyRef.current = policyId;
        defensePolicyAction.execute();
    };

    // Draft-Exemption scheme (Corruption domain): the deferral-rate control lives here
    // because its cost is manpower — the player sees the bite next to the pool it bites.
    // The subsection renders only when the Corruption feature publishes SchemesState.
    const schemesState = useSchemes();
    const schemes = bindingDataOrDefault(schemesState, DEFAULT_SCHEMES_DTO);
    const corruptionActions = useCorruptionActions();
    const deferralRateRef = useRef(0);
    const deferralAction = useRequestAction(() => {
        corruptionActions.setDraftDeferralPercent(asPercentValue(deferralRateRef.current));
        return true;
    }, schemesState.status === "ready" ? schemesState.data.CorruptionSchemeRequest : undefined);
    const deferralPending = deferralAction.isPending;
    const isDeferralLocked = (percent: number): boolean =>
        percent > 0 && !schemes.DraftDeferralAvailability.CanRun;
    const setDeferralRate = (percent: number) => {
        if (deferralPending || percent === (schemes.DraftDeferralPercent ?? 0) || isDeferralLocked(percent)) return;
        deferralRateRef.current = percent;
        deferralAction.execute();
    };

    // Patriot-vs-drones is binary, so clicking the inactive segment always means "flip" —
    // the existing toggle action already flips, no per-target wiring needed.
    const patriotOn = aa.PatriotInterceptsDrones;
    const patriotPending = patriotDroneToggleAction.isPending;
    const setPatriot = (target: boolean) => {
        if (patriotPending || target === patriotOn) return;
        patriotDroneToggleAction.execute();
    };

    return (
        <Column style={{
            width: "280rem",
            minWidth: "280rem",
            maxWidth: "280rem",
            borderRight: `2rem solid ${theme.colors.border}`,
            padding: theme.spacing.md,
            overflowY: "auto",
            overflowX: "hidden",
            flexShrink: 0,
        }}>
            {/* Header: title + status badges share one baseline (panelHeader's built-in
                marginBottom is zeroed here, mirroring the subsection titles below). Badge
                spacing is the statusBadge marginLeft; gap/rowGap is unsupported in Coherent UI. */}
            <Row align="center" wrap="wrap" style={{ marginBottom: "10rem" }}>
                <span style={{ ...s.panelHeader(accents.operations.accent), marginBottom: 0 }}>
                    {l.t("MANPOWER_TITLE")}
                </span>
                {manpower.IsManpowerCritical && (
                    <HoverTipTarget text={l.t("MANPOWER_CRITICAL")}>
                        <span style={s.statusBadge(accents.crisis.accent, true)}>
                            {l.t("STATUS_CRITICAL")}
                        </span>
                    </HoverTipTarget>
                )}
                {manpower.IsConscriptionActive && (
                    <span style={s.statusBadge(accents.resilience.accent, true)}>
                        {l.t("MANPOWER_CONSCRIPTION_BADGE")}
                    </span>
                )}
            </Row>

            <StatRow
                label={l.t("MANPOWER_AVAILABLE")}
                value={`${manpower.ManpowerAvailable} / ${manpower.ManpowerTotal}`}
                color={manpowerColor}
            />
            <ProgressBar value={manpower.ManpowerPercent} color={manpowerColor} height="8rem" />

            {manpower.IsManpowerOvercommitted && (
                <InlineWarning accent={accents.crisis.accent}>{l.t("MANPOWER_OVERCOMMITTED")}</InlineWarning>
            )}

            {/* Patriot/morale factors framed as one strip so they read as a grouped
                "modifiers" block instead of floating between the bar and the button —
                in tone with the warning boxes and button groups lower in the column. */}
            <Row style={{
                padding: "8rem 10rem",
                marginTop: "10rem",
                background: theme.colors.paper,
                border: `1rem solid ${theme.colors.border}`,
                borderRadius: theme.layout.borderRadius,
            }}>
                <StatRow
                    compact
                    label={l.t("MANPOWER_PATRIOT")}
                    value={`${manpower.ManpowerPatriotismFactor}%`}
                    color={manpower.ManpowerPatriotismFactor < 100 ? accents.crisis.accent : theme.colors.textPrimary}
                    style={{ ...s.statItem, flex: 1 }}
                />
                <StatRow
                    compact
                    label={l.t("MANPOWER_MORALE")}
                    value={`${manpower.ManpowerMoraleFactor}%`}
                    color={manpower.ManpowerMoraleFactor < 100 ? accents.resilience.accent : theme.colors.textPrimary}
                    style={{ ...s.statItem, flex: 1, marginLeft: "12rem" }}
                />
            </Row>
            {manpower.IsWarFatigued && (
                <StatRow
                    compact
                    label={l.t("MANPOWER_FATIGUE")}
                    value={`${manpower.ManpowerFatigueFactor}%`}
                    color={accents.crisis.accent}
                    style={s.statItem}
                />
            )}
            {manpower.ManpowerDodgerCount > 0 && (
                <StatRow
                    compact
                    label={l.t("MANPOWER_DODGERS")}
                    value={`${manpower.ManpowerDodgerCount} (−${Math.max(0, 100 - manpower.ManpowerDodgerFactor)}%)`}
                    color={accents.crisis.accent}
                    style={s.statItem}
                />
            )}
            {manpower.ManpowerDeferral > 0 && (
                <StatRow
                    compact
                    label={l.t("MANPOWER_DEFERRALS")}
                    value={`−${manpower.ManpowerDeferral}`}
                    color={accents.crisis.accent}
                    style={s.statItem}
                />
            )}
            {manpower.ManpowerDisability > 0 && (
                <StatRow
                    compact
                    label={l.t("MANPOWER_DISABILITY")}
                    value={`−${manpower.ManpowerDisability}`}
                    color={accents.crisis.accent}
                    style={s.statItem}
                />
            )}

            {!manpower.SocialPenaltyProducerReady && manpower.SocialPenaltyReasonId && (
                <InlineWarning accent={theme.colors.textMuted}>
                    {l.tDynamic(manpower.SocialPenaltyReasonId)}
                </InlineWarning>
            )}

            {callToArms.hasCasualties && (
                <>
                    <StatRow
                        label={l.t("MANPOWER_CASUALTIES")}
                        value={manpower.ManpowerCasualties}
                        color={accents.crisis.accent}
                    />
                    <ActionButton
                        onClick={callToArmsAction.execute}
                        isPending={callToArmsAction.isPending}
                        disabled={!callToArms.canRun}
                        color={accents.resilience.accent}
                        pendingText={l.t("UI_PROCESSING")}
                    >
                        {!callToArms.canRun && callToArms.reasonId
                            ? l.tDynamic(callToArms.reasonId)
                            : l.t("MANPOWER_CALL_TO_ARMS")}
                    </ActionButton>
                    <div style={s.note}>{l.t("MANPOWER_CALL_TO_ARMS_NOTE")}</div>
                </>
            )}

            <ActionButton
                onClick={conscriptionAction.execute}
                isPending={conscriptionAction.isPending}
                disabled={!manpower.CanToggleConscription}
                color={manpower.IsConscriptionActive ? accents.crisis.accent : accents.operations.accent}
                pendingText={l.t("UI_PROCESSING")}
            >
                {!manpower.CanToggleConscription && manpower.ConscriptionLockedReasonId
                    ? l.tDynamic(manpower.ConscriptionLockedReasonId)
                    : manpower.IsConscriptionActive
                    ? l.t("MANPOWER_CONSCRIPTION_OFF")
                    : l.t("MANPOWER_CONSCRIPTION_ON")}
            </ActionButton>
            {manpower.IsConscriptionActive && (
                <>
                    <div style={s.note}>{l.t("MANPOWER_CONSCRIPTION_NOTE")}</div>
                    <InlineWarning accent={accents.crisis.accent}>
                        {l.t("MANPOWER_CONSCRIPTION_PENALTY")}
                        {manpower.PredictedConscriptionRelease > 0 && (
                            <div style={{ marginTop: "4rem" }}>
                                {l.t("MANPOWER_CONSCRIPTION_RELEASE_WARNING", manpower.PredictedConscriptionRelease)}
                            </div>
                        )}
                    </InlineWarning>
                </>
            )}

            {/* One divider opens the policy cluster; inside it, rows are spaced by margin
                (not per-section dividers) so the whole column fits without a scrollbar. */}
            <div style={{ height: "2rem", background: theme.colors.border, margin: "12rem 0" }} />

            {schemesState.status === "ready" && (
                <>
                    <Row align="center" style={{ marginBottom: "6rem" }}>
                        <span style={{ ...s.subsectionTitle(accents.crisis.accent), marginBottom: 0 }}>
                            {l.t("MANPOWER_DRAFT_SALES_TITLE")}
                        </span>
                        <InfoTip text={l.t("MANPOWER_DRAFT_SALES_HELP")} color={accents.crisis.accent} />
                    </Row>
                    <div style={s.buttonGroup}>
                        {DRAFT_DEFERRAL_LEVELS.map((percent) => (
                            <SegmentButton
                                key={percent}
                                label={`${percent}%`}
                                color={percent === 0 ? accents.operations.accent : accents.crisis.accent}
                                active={(schemes.DraftDeferralPercent ?? 0) === percent}
                                disabled={deferralPending || isDeferralLocked(percent)}
                                busy={deferralPending}
                                onClick={() => setDeferralRate(percent)}
                                styleFn={s.buttonWithOpacity}
                            />
                        ))}
                    </div>
                </>
            )}

            {/* Engagement keeps a full-width two-button row: "Humanitarian"/"Grid Integrity"
                are too wide to sit inline beside a label. */}
            <Row align="center" style={{ marginTop: "14rem", marginBottom: "6rem" }}>
                <span style={{ ...s.subsectionTitle(accents.operations.accent), marginBottom: 0 }}>
                    {l.t("DEFENSE_POLICY_SUBTITLE")}
                </span>
                <InfoTip
                    text={aa.DefensePolicyId === 0
                        ? l.t("POLICY_HUMANITARIAN_DESC")
                        : l.t("POLICY_GRID_DESC")}
                    color={accents.operations.accent}
                />
            </Row>
            <div style={s.buttonGroup}>
                <SegmentButton
                    label={l.t("POLICY_HUMANITARIAN_BTN")}
                    color={accents.operations.accent}
                    active={aa.DefensePolicyId === 0}
                    disabled={defensePolicyPending}
                    busy={defensePolicyPending}
                    onClick={() => setDefensePolicy(0)}
                    styleFn={s.buttonWithOpacity}
                />
                <SegmentButton
                    label={l.t("POLICY_GRID_BTN")}
                    color={accents.resilience.accent}
                    active={aa.DefensePolicyId === 1}
                    disabled={defensePolicyPending}
                    busy={defensePolicyPending}
                    onClick={() => setDefensePolicy(1)}
                    styleFn={s.buttonWithOpacity}
                />
            </div>
            {defensePolicyError && (
                <div style={s.note}>{l.tDynamic(defensePolicyError)}</div>
            )}

            {/* Binary OFF/ON toggles collapse to inline rows — short controls fit beside the label. */}
            <PolicyRow
                label={l.t("UI_AA_PATRIOT_DRONE_TOGGLE")}
                color={accents.crisis.accent}
                help={l.t("UI_AA_PATRIOT_DRONE_HELP")}
                controlWidth="104rem"
                styles={s}
            >
                <SegmentButton
                    label={l.t("UI_AA_PATRIOT_DRONE_OFF")}
                    color={accents.operations.accent}
                    active={!patriotOn}
                    disabled={patriotPending}
                    busy={patriotPending}
                    onClick={() => setPatriot(false)}
                    styleFn={inlineBtn}
                />
                <SegmentButton
                    label={l.t("UI_AA_PATRIOT_DRONE_ON")}
                    color={accents.crisis.accent}
                    active={patriotOn}
                    disabled={patriotPending}
                    busy={patriotPending}
                    onClick={() => setPatriot(true)}
                    styleFn={inlineBtn}
                />
            </PolicyRow>

            <PolicyRow
                label={l.t("UI_AA_AUTO_RESUPPLY_TOGGLE")}
                color={accents.operations.accent}
                help={l.t("UI_AA_AUTO_RESUPPLY_HELP")}
                controlWidth="104rem"
                styles={s}
            >
                <SegmentButton
                    label={l.t("UI_AA_AUTO_RESUPPLY_OFF")}
                    color={accents.crisis.accent}
                    active={!aa.AutoResupplyEnabled}
                    disabled={false}
                    onClick={() => { if (aa.AutoResupplyEnabled) actions.toggleAutoResupply(false); }}
                    styleFn={inlineBtn}
                />
                <SegmentButton
                    label={l.t("UI_AA_AUTO_RESUPPLY_ON")}
                    color={accents.operations.accent}
                    active={aa.AutoResupplyEnabled}
                    disabled={false}
                    onClick={() => { if (!aa.AutoResupplyEnabled) actions.toggleAutoResupply(true); }}
                    styleFn={inlineBtn}
                />
            </PolicyRow>
        </Column>
    );
};
