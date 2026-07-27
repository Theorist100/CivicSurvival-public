/**
 * NetworkPanel — the right rail. The internet mode switch lives in
 * GlobalNetSection (reused verbatim). The Propaganda Center card is a
 * fixed-height summary: a BUILD button for an absent Center; for a built one
 * the WHOLE card is the clickable entry into ManageCenterSubView (the same
 * idiom as the countermeasure launcher rows) — the rail's height is whatever
 * the crowd board leaves over, so no operable control block lives here.
 */

import React, { memo, useMemo } from "react";
import { Column, Row } from "../../../../coherent";
import { useTheme, useAccents, hexToRgba, formatMoney } from "../../../../../themes";
import { useLocale } from "../../../../../locales";
import { bindingDataOrDefault, useCognitive } from "@hooks/domain";
import { DEFAULT_COGNITIVE_DTO } from "../../../../../types/domainDtos";
import { useRequestAction, type useCognitiveActions } from "@hooks/actions";
import { ProgressBar } from "../../../../shared/ui";
import { IconSatellite } from "../../../../shared/common/Icons";
import { GlobalNetSection } from "../GlobalNetSection";
import { createBoardStyles } from "./opinionStyles";

interface NetworkPanelProps {
    actions: ReturnType<typeof useCognitiveActions>;
    disabled: boolean;
    /** Open the Center's manage drill-down (ManageCenterSubView) — shown for a built Center. */
    onManage: () => void;
}

export const NetworkPanel = memo(({ actions, disabled, onManage }: NetworkPanelProps) => {
    const theme = useTheme();
    const accents = useAccents();
    const l = useLocale();
    const b = createBoardStyles(theme, accents);
    const control = accents.crisis.accent;

    const cw = bindingDataOrDefault(useCognitive(), DEFAULT_COGNITIVE_DTO);

    // Build runs through the existing request-result lifecycle so the button
    // reflects pending + the failure reason (e.g. can't afford). The upgrade
    // lifecycle lives in ManageCenterSubView with the rest of the controls.
    const placeAction = useRequestAction(() => {
        actions.placePropagandaCenter();
        return true;
    }, cw.PropagandaCenterPlacementRequest);
    const placePending = placeAction.isPending;
    const placeError = placeAction.failureReasonId;

    const capacity = cw.BroadcastCapacity;
    const free = cw.BroadcastCapacityFree;
    const usedPct = capacity > 0 ? Math.round(((capacity - free) / capacity) * 100) : 0;
    // Derive FREE from the rounded USED so the pair the player reads side by side always sums to
    // exactly 100 (two independent Math.round of the same capacity can otherwise read 99 or 101).
    const freePct = capacity > 0 ? Math.max(0, 100 - usedPct) : 0;

    const status = cw.CenterOnline
        ? { key: "UI_OB_CENTER_STATUS_ONLINE" as const, color: theme.colors.success }
        : cw.CenterBuilt
            ? { key: "UI_OB_CENTER_STATUS_OFFLINE" as const, color: accents.resilience.accent }
            : { key: "UI_OB_CENTER_STATUS_ABSENT" as const, color: theme.colors.textMuted };

    const s = useMemo(() => ({
        actionBtn: (color: string, off: boolean): React.CSSProperties => ({
            width: "100%",
            display: "flex",
            alignItems: "center",
            justifyContent: "space-between",
            padding: "8rem 10rem",
            fontSize: theme.typography.sizeXS,
            fontWeight: 700,
            fontFamily: theme.typography.fontFamilyMono,
            textTransform: "uppercase",
            letterSpacing: "0.4rem",
            color: off ? theme.colors.textMuted : color,
            backgroundColor: off ? "transparent" : hexToRgba(color, 0.08),
            border: `2rem solid ${off ? theme.colors.border : color}`,
            borderRadius: theme.layout.borderRadius,
            cursor: off ? "not-allowed" : "pointer",
            opacity: off ? 0.55 : 1,
        }),
        stat: {
            fontSize: theme.typography.sizeXS,
            fontFamily: theme.typography.fontFamilyMono,
            color: theme.colors.textSecondary,
        } as React.CSSProperties,
        hint: {
            marginTop: "4rem",
            fontSize: theme.typography.sizeXS,
            color: theme.colors.textMuted,
            textAlign: "center" as const,
        } as React.CSSProperties,
        chev: {
            fontSize: theme.typography.sizeSM,
            fontWeight: 700,
            fontFamily: theme.typography.fontFamilyMono,
            color: theme.colors.textSecondary,
        } as React.CSSProperties,
    }), [theme]);

    return (
        // NO percentage height here — in cohtml a % height that cannot resolve
        // against a definite parent falls back to the SCROLL VIEWPORT, which
        // inflated this rail (and with it the whole context row) to full panel
        // height. The rail is natural-height; the owner row sizes to content.
        <Column gap={theme.spacing.sm} style={{ width: "100%" }}>
            <div style={{ ...b.region, flexShrink: 0 }}>
                <GlobalNetSection actions={actions} disabled={disabled} />
            </div>

            {/* Propaganda Center card is natural-height (no flex stretch — the rail
                column has no definite height to stretch into; see the root note).

                Built Center: the WHOLE card is the entry into the manage
                drill-down (same idiom as the countermeasure launcher rows on the
                left — chevron in the corner, control-tinted border as the click
                affordance). No separate MANAGE button: the rail's height budget
                after the crowd board is ~200rem and a button row doesn't fit.
                Absent Center: BUILD stays a real button — a paid action is never
                hung on a whole card. */}
            <div
                role="button"
                tabIndex={0}
                style={{
                    ...b.region,
                    padding: theme.spacing.sm,
                    ...(cw.CenterBuilt ? {
                        border: `2rem solid ${hexToRgba(control, 0.5)}`,
                        cursor: disabled ? "not-allowed" : "pointer",
                    } : {}),
                }}
                onClick={() => { if (cw.CenterBuilt && !disabled) onManage(); }}
                onKeyDown={(e) => {
                    if ((e.key === "Enter" || e.key === " ") && cw.CenterBuilt && !disabled) onManage();
                }}
            >
                <Row align="center" justify="space-between" style={{ marginBottom: theme.spacing.xs }}>
                    <Row align="center">
                        <span style={{ color: control, marginRight: "5rem", display: "flex" }}>
                            <IconSatellite />
                        </span>
                        <span style={b.eyebrow}>{l.t("UI_OB_CENTER")}</span>
                    </Row>
                    <Row align="center" gap={theme.spacing.xs}>
                        <span style={b.chip(status.color)}>{l.t(status.key)}</span>
                        {cw.CenterBuilt && (
                            /* ASCII ">" — the DOS pixel font has no "›" (renders as a cent sign). */
                            <span aria-hidden="true" style={s.chev}>{">"}</span>
                        )}
                    </Row>
                </Row>

                {!cw.CenterBuilt ? (
                    <>
                        <button
                            style={s.actionBtn(control, disabled || placePending)}
                            disabled={disabled || placePending}
                            onClick={() => { if (!disabled && !placePending) placeAction.execute(); }}
                        >
                            <span>{l.t("UI_OB_CENTER_BUILD")}</span>
                            <span>{placePending ? l.t("UI_PROCESSING") : formatMoney(cw.CenterCost)}</span>
                        </button>
                        {placeError && <div style={s.hint}>{l.tDynamic(placeError)}</div>}
                    </>
                ) : (
                    <>
                        <Row align="center" justify="space-between" style={{ marginBottom: "4rem" }}>
                            <span style={s.stat}>{l.t("UI_OB_CENTER_TIER", cw.CenterTier, cw.CenterMaxTier)}</span>
                            <Row align="center" gap={theme.spacing.sm}>
                                <span style={{ fontSize: theme.typography.sizeXS, color: theme.colors.textSecondary }}>
                                    {l.t("UI_OB_CAPACITY_USED", usedPct)}
                                </span>
                                <span style={{ fontSize: theme.typography.sizeXS, color: theme.colors.success }}>
                                    {l.t("UI_OB_CAPACITY_FREE", freePct)}
                                </span>
                            </Row>
                        </Row>
                        <ProgressBar value={usedPct} color={control} height="8rem" />
                    </>
                )}
            </div>
        </Column>
    );
});

NetworkPanel.displayName = "NetworkPanel";
