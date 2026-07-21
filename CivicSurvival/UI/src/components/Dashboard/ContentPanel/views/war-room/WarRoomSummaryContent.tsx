/**
 * WarRoomSummaryContent — WAR ROOM domain → COMMAND view.
 *
 * The native dashboard home of the War Room: a compact theatre summary that stays one
 * click away without the fullscreen overlay. Shows the three enemy-axis chips (live
 * axis health — strikes push them down, enemy repairs pull them back), the LAUNCH
 * CONTROL operation slots (prepare / execute / cancel fire the same pause-safe
 * triggers as the overlay), and the INTEL block (next-wave forecast + spend buttons).
 * The OPEN WAR ROOM button raises the existing fullscreen overlay for the full
 * theatre (radar, strike targeting, rosters, C2 bus).
 *
 * Gate mirrors the overlay entry (WarContent's expand button): a locked note until
 * the GridWarfare beta wave opens.
 */

import React, { memo, useMemo } from "react";
import { useAccents, useTheme, hexToRgba } from "@themes";
import { useLocale } from "@locales";
import { bindingDataOrDefault, useGridWarfareDomain } from "@hooks/domain";
import { useGridWarfareActions } from "@hooks/actions";
import { useBetaWave } from "@hooks/useBetaWave";
import { openWarRoom } from "@hooks/useWarRoomOverlay";
import { DEFAULT_GRID_WARFARE_DTO } from "../../../../../types/domainDtos";
import { getIconComponent } from "../../../../shared/common/Icons";
import { HelpSection } from "../../../../shared/common/HelpSection";
import { OperationSlots } from "../../../../GridWarfare";
import { WarRoomAxisChips } from "./WarRoomAxisChips";
import { WarRoomIntel } from "./WarRoomIntel";

export const WarRoomSummaryContent = memo(() => {
    const theme = useTheme();
    const accents = useAccents();
    const l = useLocale();
    const mono = theme.typography.fontFamilyMono;
    const gate = useBetaWave("GridWarfare");
    const gw = bindingDataOrDefault(useGridWarfareDomain(), DEFAULT_GRID_WARFARE_DTO);
    const actions = useGridWarfareActions();
    const Icon = getIconComponent("globe");

    const s = useMemo(() => ({
        container: {
            display: "flex",
            flexDirection: "column" as const,
            height: "100%",
            padding: theme.spacing.md,
            overflowY: "auto" as const,
            overflowX: "hidden" as const,
        } as React.CSSProperties,
        headerRow: {
            display: "flex",
            alignItems: "center",
            justifyContent: "space-between",
            marginBottom: theme.spacing.md,
        } as React.CSSProperties,
        title: {
            fontSize: "12rem",
            fontWeight: theme.typography.weightBold,
            textTransform: "uppercase" as const,
            letterSpacing: "2rem",
            color: accents.crisis.accent,
            fontFamily: mono,
        } as React.CSSProperties,
        expandButton: {
            display: "flex",
            alignItems: "center",
            padding: "6rem 12rem",
            borderRadius: theme.layout.borderRadiusLg,
            background: hexToRgba(accents.crisis.accent, 0.15),
            border: `2rem solid ${hexToRgba(accents.crisis.accent, 0.5)}`,
            cursor: "pointer",
        } as React.CSSProperties,
        expandIcon: {
            display: "flex",
            alignItems: "center",
            marginRight: "6rem",
            color: accents.crisis.accent,
        } as React.CSSProperties,
        expandLabel: {
            fontSize: "11rem",
            fontWeight: 700,
            textTransform: "uppercase" as const,
            letterSpacing: "0.5rem",
            color: accents.crisis.accent,
            fontFamily: mono,
        } as React.CSSProperties,
        // flex: 1 + stretch: the tab height is stable, so the body claims the space
        // below the chips and the operation rows grow into it instead of huddling
        // at the top of a half-empty tab.
        body: {
            display: "flex",
            alignItems: "stretch",
            flex: 1,
            marginTop: theme.spacing.md,
            minHeight: 0,
        } as React.CSSProperties,
        intelColumn: {
            display: "flex",
            flexDirection: "column" as const,
            width: "250rem",
            minHeight: "200rem",
            flexShrink: 0,
            marginRight: theme.spacing.md,
        } as React.CSSProperties,
        slotsColumn: {
            display: "flex",
            flexDirection: "column" as const,
            flex: 1,
            minWidth: 0,
            minHeight: "200rem",
        } as React.CSSProperties,
        locked: {
            display: "flex",
            alignItems: "center",
            justifyContent: "center",
            height: "100%",
            padding: theme.spacing.lg,
            fontSize: "12rem",
            textTransform: "uppercase" as const,
            letterSpacing: "1rem",
            color: theme.colors.textMuted,
            fontFamily: mono,
            textAlign: "center" as const,
        } as React.CSSProperties,
    }), [theme, accents, mono]);

    if (gate.isLocked) {
        return <div style={s.locked}>{l.t("UI_WARROOM_TAB_LOCKED")}</div>;
    }

    return (
        <div style={s.container}>
            <div style={s.headerRow}>
                {/* Flex wrapper so the "?" sits beside the title instead of drifting into
                    the space-between gap of headerRow. */}
                <div style={{ display: "flex", alignItems: "center" }}>
                    <span style={s.title}>{l.t("UI_WARROOM_SUMMARY_TITLE")}</span>
                    <HelpSection id="counterstrike" title={l.t("UI_WARROOM_HELP_TITLE")}>
                        {l.t("HELP_COUNTERSTRIKE")}
                    </HelpSection>
                </div>
                <div
                    style={s.expandButton}
                    role="button"
                    tabIndex={0}
                    onClick={openWarRoom}
                    onKeyDown={(e) => {
                        if (e.key === "Enter" || e.key === " ") {
                            e.preventDefault();
                            openWarRoom();
                        }
                    }}
                >
                    {Icon && <span style={s.expandIcon}><Icon /></span>}
                    <span style={s.expandLabel}>{l.t("UI_WARROOM_EXPAND")}</span>
                </div>
            </div>

            <WarRoomAxisChips gw={gw} intelLevel={gw.IntelLevel ?? 0} stretch />

            <div style={s.body}>
                {/* Info column on the left — matches the rest of the dashboard views
                    (threat list, Chirper): reading pane left, action pane right. */}
                <div style={s.intelColumn}>
                    <WarRoomIntel />
                </div>
                <div style={s.slotsColumn}>
                    <OperationSlots state={gw} actions={actions} layout="rows" />
                </div>
            </div>
        </div>
    );
});

WarRoomSummaryContent.displayName = "WarRoomSummaryContent";
