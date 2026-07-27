/**
 * WarRoomAaRoster — the left-column AA ROSTER table: every deployed air-defense emplacement grouped
 * by type, with its count, engagement range, and per-threat intercept chances (shahed / ballistic).
 *
 * Reads the already-grouped roster from useDefenseData() (buildRoster groups AaRosterJson by prefab
 * and carries the deployed count); rows with nothing fielded are dropped so this shows the live
 * order of battle, not the catalogue. Nothing here names an AA — add one to the C# catalogue and it
 * appears. Flex-grows to fill the bottom of the column so there is no empty gap.
 */

import React, { memo, useMemo } from "react";
import { useAccents, useTheme, hexToRgba } from "@themes";
import { useLocale } from "@locales";
import { useDefenseData } from "@hooks/domain";

export const WarRoomAaRoster: React.FC = memo(() => {
    const theme = useTheme();
    const accents = useAccents();
    const l = useLocale();
    const mono = theme.typography.fontFamilyMono;

    const { roster } = useDefenseData();
    const deployed = useMemo(() => roster.filter((t) => t.deployed > 0), [roster]);

    const s = useMemo(() => ({
        block: {
            display: "flex",
            flexDirection: "column" as const,
            flex: 1,
            minHeight: "80rem",
            marginTop: theme.spacing.md,
            paddingTop: theme.spacing.sm,
            borderTop: `1rem solid ${theme.colors.border}`,
        } as React.CSSProperties,
        title: {
            fontSize: "11rem",
            fontWeight: theme.typography.weightBold,
            textTransform: "uppercase" as const,
            letterSpacing: "2rem",
            color: accents.schemes.accent,
            marginBottom: theme.spacing.sm,
            flexShrink: 0,
        } as React.CSSProperties,
        headRow: {
            display: "flex",
            alignItems: "center",
            paddingBottom: "4rem",
            borderBottom: `1rem solid ${hexToRgba(theme.colors.textMuted, 0.25)}`,
            flexShrink: 0,
        } as React.CSSProperties,
        bodyWrap: {
            display: "flex",
            flexDirection: "column" as const,
            flex: 1,
            minHeight: 0,
            overflowY: "auto" as const,
            overflowX: "hidden" as const,
        } as React.CSSProperties,
        row: {
            display: "flex",
            alignItems: "center",
            padding: "5rem 0",
            borderBottom: `1rem solid ${hexToRgba(theme.colors.border, 0.5)}`,
        } as React.CSSProperties,
        cellType: {
            flex: 1,
            minWidth: 0,
            fontSize: "10rem",
            textTransform: "uppercase" as const,
            letterSpacing: "0.5rem",
            color: theme.colors.textSecondary,
            fontFamily: mono,
            overflow: "hidden" as const,
            whiteSpace: "nowrap" as const,
            textOverflow: "ellipsis" as const,
        } as React.CSSProperties,
        cellCount: {
            width: "34rem",
            flexShrink: 0,
            textAlign: "center" as const,
            fontSize: "11rem",
            fontWeight: theme.typography.weightBold,
            color: accents.schemes.accent,
            fontFamily: mono,
        } as React.CSSProperties,
        cellRange: {
            width: "56rem",
            flexShrink: 0,
            textAlign: "right" as const,
            fontSize: "10rem",
            color: theme.colors.textMuted,
            fontFamily: mono,
        } as React.CSSProperties,
        cellIntercept: {
            width: "68rem",
            flexShrink: 0,
            textAlign: "right" as const,
            fontSize: "10rem",
            fontWeight: theme.typography.weightBold,
            color: theme.colors.textPrimary,
            fontFamily: mono,
        } as React.CSSProperties,
        headCell: (base: React.CSSProperties): React.CSSProperties => ({
            ...base,
            color: theme.colors.textMuted,
            fontWeight: theme.typography.weightBold,
        }),
        empty: {
            fontSize: "10rem",
            color: theme.colors.textMuted,
            fontFamily: mono,
            marginTop: "4rem",
        } as React.CSSProperties,
    }), [theme, accents, mono]);

    return (
        <div style={s.block}>
            <div style={s.title}>{l.t("UI_WARROOM_AA_ROSTER")}</div>

            {deployed.length === 0 ? (
                <span style={s.empty}>{l.t("UI_WARROOM_AA_ROSTER_EMPTY")}</span>
            ) : (
                <>
                    <div style={s.headRow}>
                        <span style={s.headCell(s.cellType)}>{l.t("UI_WARROOM_AA_COL_TYPE")}</span>
                        <span style={s.headCell(s.cellCount)}>{l.t("UI_WARROOM_AA_COL_COUNT")}</span>
                        <span style={s.headCell(s.cellRange)}>{l.t("UI_WARROOM_AA_COL_RANGE")}</span>
                        <span style={s.headCell(s.cellIntercept)}>{l.t("UI_WARROOM_AA_COL_INTERCEPT")}</span>
                    </div>
                    <div style={s.bodyWrap}>
                        {deployed.map((tile) => {
                            const h = tile.headline;
                            const sPct = Math.round((h.InterceptShahed ?? 0) * 100);
                            const bPct = Math.round((h.InterceptBallistic ?? 0) * 100);
                            return (
                                <div key={tile.prefab} style={s.row}>
                                    <span style={s.cellType}>{l.tDynamic(h.NameKey)}</span>
                                    <span style={s.cellCount}>{`x${tile.deployed}`}</span>
                                    <span style={s.cellRange}>{`${Math.round(h.Range)}m`}</span>
                                    <span style={s.cellIntercept}>{`${sPct}/${bPct}%`}</span>
                                </div>
                            );
                        })}
                    </div>
                </>
            )}
        </div>
    );
});

WarRoomAaRoster.displayName = "WarRoomAaRoster";
