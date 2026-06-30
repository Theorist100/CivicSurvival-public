import React, { useRef } from "react";
import { Column, Row } from "@coherent";
import { useAccents, useTheme } from "@themes";
import type { AirDefenseDto, MobilizationDto } from "@hooks/domain";
import { useLocale } from "@locales";
import { ActionButton } from "@shared/common/ActionButton";
import { HoverTip, HoverTipTarget } from "@shared/common/HoverTip";
import { InlineWarning, ProgressBar, StatRow } from "@shared/ui";
import { useRequestAction } from "@hooks/actions";
import { type DefensePolicyId } from "../../../../../types/semantic";
import type { AsyncAction, CallToArmsValidator, WarViewStyles } from "./types";
import { type useDefenseActions } from "@hooks/actions";

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
            display: "inline-flex",
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

/** One highlighted-when-active segment in a two-button policy toggle. Holding the
 * opacity ternary here keeps the column body's cognitive complexity in check. */
const SegmentButton: React.FC<{
    label: string;
    color: string;
    active: boolean;
    pending: boolean;
    onClick: () => void;
    styleFn: (color: string, opacity: number) => React.CSSProperties;
}> = ({ label, color, active, pending, onClick, styleFn }) => (
    <button
        style={styleFn(color, pending ? 0.35 : active ? 1 : 0.5)}
        disabled={pending}
        onClick={onClick}
    >
        {label}
    </button>
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
    const defensePolicyRef = useRef(aa.DefensePolicyId);
    const defensePolicyAction = useRequestAction(() => {
        actions.setDefensePolicy(defensePolicyRef.current);
        return true;
    }, aa.DefensePolicyRequest);
    const defensePolicyPending = defensePolicyAction.isPending || aa.DefensePolicyRequest.Status === "pending";
    const defensePolicyError =
        aa.DefensePolicyRequest.Status === "failed" && aa.DefensePolicyRequest.ReasonId
            ? aa.DefensePolicyRequest.ReasonId
            : "";

    const setDefensePolicy = (policyId: DefensePolicyId) => {
        if (defensePolicyPending) return;
        defensePolicyRef.current = policyId;
        defensePolicyAction.execute();
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

            <div style={{ height: "2rem", background: theme.colors.border, margin: "10rem 0" }} />

            <Row align="center" style={{ marginBottom: "10rem" }}>
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
                    label={defensePolicyPending ? l.t("UI_PROCESSING") : l.t("POLICY_HUMANITARIAN_BTN")}
                    color={accents.operations.accent}
                    active={aa.DefensePolicyId === 0}
                    pending={defensePolicyPending}
                    onClick={() => setDefensePolicy(0)}
                    styleFn={s.buttonWithOpacity}
                />
                <SegmentButton
                    label={defensePolicyPending ? l.t("UI_PROCESSING") : l.t("POLICY_GRID_BTN")}
                    color={accents.resilience.accent}
                    active={aa.DefensePolicyId === 1}
                    pending={defensePolicyPending}
                    onClick={() => setDefensePolicy(1)}
                    styleFn={s.buttonWithOpacity}
                />
            </div>
            {defensePolicyError && (
                <div style={s.note}>{l.tDynamic(defensePolicyError)}</div>
            )}

            <div style={{ height: "2rem", background: theme.colors.border, margin: "10rem 0" }} />
            <Row align="center" style={{ marginBottom: "10rem" }}>
                <span style={{ ...s.subsectionTitle(accents.crisis.accent), marginBottom: 0 }}>
                    {l.t("UI_AA_PATRIOT_DRONE_TOGGLE")}
                </span>
                <InfoTip text={l.t("UI_AA_PATRIOT_DRONE_HELP")} color={accents.crisis.accent} />
            </Row>
            <div style={s.buttonGroup}>
                <SegmentButton
                    label={l.t("UI_AA_PATRIOT_DRONE_OFF")}
                    color={accents.operations.accent}
                    active={!patriotOn}
                    pending={patriotPending}
                    onClick={() => setPatriot(false)}
                    styleFn={s.buttonWithOpacity}
                />
                <SegmentButton
                    label={l.t("UI_AA_PATRIOT_DRONE_ON")}
                    color={accents.crisis.accent}
                    active={patriotOn}
                    pending={patriotPending}
                    onClick={() => setPatriot(true)}
                    styleFn={s.buttonWithOpacity}
                />
            </div>

            <div style={{ height: "2rem", background: theme.colors.border, margin: "10rem 0" }} />
            <Row align="center" style={{ marginBottom: "10rem" }}>
                <span style={{ ...s.subsectionTitle(accents.operations.accent), marginBottom: 0 }}>
                    {l.t("UI_AA_AUTO_RESUPPLY_TOGGLE")}
                </span>
                <InfoTip text={l.t("UI_AA_AUTO_RESUPPLY_HELP")} color={accents.operations.accent} />
            </Row>
            <div style={s.buttonGroup}>
                <SegmentButton
                    label={l.t("UI_AA_AUTO_RESUPPLY_OFF")}
                    color={accents.crisis.accent}
                    active={!aa.AutoResupplyEnabled}
                    pending={false}
                    onClick={() => { if (aa.AutoResupplyEnabled) actions.toggleAutoResupply(false); }}
                    styleFn={s.buttonWithOpacity}
                />
                <SegmentButton
                    label={l.t("UI_AA_AUTO_RESUPPLY_ON")}
                    color={accents.operations.accent}
                    active={aa.AutoResupplyEnabled}
                    pending={false}
                    onClick={() => { if (!aa.AutoResupplyEnabled) actions.toggleAutoResupply(true); }}
                    styleFn={s.buttonWithOpacity}
                />
            </div>
        </Column>
    );
};
