/**
 * LeaderboardPanel - Arena Leaderboard with Rank Tiers
 *
 * Features:
 * - Shows all rank tiers (Entropy Lord → Blackout Survivor)
 * - "VACANT" status when no player has achieved a rank
 * - All-time and Weekly tabs
 * - SVG rank icons
 */

import React, { memo, useState, useMemo, useEffect } from "react";
import { Column } from "@coherent";
import { useTheme, useAccents, hexToRgba } from "@themes";
import { type ArenaLeaderboardState } from "@hooks/state/useArenaLeaderboard";
import { useLocale } from "../../locales";
import { LeaderboardContent } from "./leaderboard/LeaderboardContent";
import {
    LeaderboardOptInOverlay,
    LeaderboardPositionFooter,
    LeaderboardTabs,
    type LeaderboardTabType,
} from "./leaderboard/LeaderboardChrome";

// ============ Component ============

interface LeaderboardPanelProps {
    leaderboard: ArenaLeaderboardState;
    onRefreshLeaderboard: () => void;
    onlineEnabled: boolean;
    onlineConsentRecorded: boolean;
}

export const LeaderboardPanel = memo(({ leaderboard, onRefreshLeaderboard, onlineEnabled, onlineConsentRecorded }: LeaderboardPanelProps) => {
    const theme = useTheme();
    const accents = useAccents();
    const l = useLocale();
    const [activeTab, setActiveTab] = useState<LeaderboardTabType>("ranks");

    // Refresh leaderboard on mount and Online enable transition. The board is an Online
    // feature (backend gated on OnlineEnabled), so Online — not diagnostics — gates it.
    useEffect(() => {
        if (onlineEnabled) onRefreshLeaderboard();
    }, [onRefreshLeaderboard, onlineEnabled]);

    // ========== Memoized Styles ==========

    const containerStyle = useMemo((): React.CSSProperties => ({
        padding: "16rem",
        minHeight: "400rem",
        position: "relative" as const,
    }), []);

    const dimmedStyle = useMemo((): React.CSSProperties => onlineEnabled ? {} : {
        opacity: 0.4,
    }, [onlineEnabled]);

    const headerStyle = useMemo((): React.CSSProperties => ({
        fontSize: "14rem",
        fontWeight: 700,
        color: accents.operations.accent,
        textTransform: "uppercase" as const,
        letterSpacing: "0.5rem",
        marginBottom: "12rem",
        borderBottom: `2rem solid ${hexToRgba(accents.operations.accent, 0.25)}`,
        paddingBottom: "8rem",
    }), [accents.operations.accent]);

    const sectionStyle = useMemo((): React.CSSProperties => ({
        background: theme.colors.paper,
        borderRadius: theme.layout.borderRadius,
        border: `2rem solid ${theme.colors.border}`,
        overflow: "hidden" as const,
    }), [theme.colors.paper, theme.layout.borderRadius, theme.colors.border]);

    const tierRowStyles = useMemo(() => ({
        even: {
            padding: "12rem 16rem",
            borderBottom: `2rem solid ${theme.colors.border}`,
            background: "transparent",
        } as React.CSSProperties,
        odd: {
            padding: "12rem 16rem",
            borderBottom: `2rem solid ${theme.colors.border}`,
            background: hexToRgba(theme.colors.border, 0.12),
        } as React.CSSProperties,
    }), [theme.colors.border]);

    const requirementStyle = useMemo((): React.CSSProperties => ({
        fontSize: "10rem",
        color: theme.colors.textMuted,
        fontFamily: theme.typography.fontFamilyMono,
    }), [theme.colors.textMuted, theme.typography.fontFamilyMono]);

    const entryRowStyles = useMemo(() => ({
        even: {
            padding: "10rem 16rem",
            borderBottom: `2rem solid ${theme.colors.border}`,
            background: "transparent",
        } as React.CSSProperties,
        odd: {
            padding: "10rem 16rem",
            borderBottom: `2rem solid ${theme.colors.border}`,
            background: hexToRgba(theme.colors.border, 0.12),
        } as React.CSSProperties,
    }), [theme.colors.border]);

    // Pre-memoized position styles (2 variants: top-3 vs rest)
    const positionStyleTop = useMemo((): React.CSSProperties => ({
        fontSize: "12rem",
        fontWeight: 700,
        color: accents.resilience.accent,
        fontFamily: theme.typography.fontFamilyMono,
        minWidth: "40rem",
    }), [accents.resilience.accent, theme.typography.fontFamilyMono]);

    const positionStyleRest = useMemo((): React.CSSProperties => ({
        fontSize: "12rem",
        fontWeight: 700,
        color: theme.colors.textMuted,
        fontFamily: theme.typography.fontFamilyMono,
        minWidth: "40rem",
    }), [theme.colors.textMuted, theme.typography.fontFamilyMono]);

    // Pre-memoized holder styles (2 variants: vacant vs active)
    const holderStyleVacant = useMemo((): React.CSSProperties => ({
        fontSize: "12rem",
        color: theme.colors.textMuted,
        fontStyle: "italic" as const,
        flex: 1,
    }), [theme.colors.textMuted]);

    const holderStyleActive = useMemo((): React.CSSProperties => ({
        fontSize: "12rem",
        color: accents.schemes.accent,
        fontStyle: "normal" as const,
        flex: 1,
    }), [accents.schemes.accent]);

    const nicknameStyle = useMemo((): React.CSSProperties => ({
        fontSize: "12rem",
        fontWeight: 600,
        color: theme.colors.textPrimary,
        flex: 1,
    }), [theme.colors.textPrimary]);

    const statsStyle = useMemo((): React.CSSProperties => ({
        fontSize: "11rem",
        color: theme.colors.textSecondary,
        fontFamily: theme.typography.fontFamilyMono,
        marginLeft: "12rem",
    }), [theme.colors.textSecondary, theme.typography.fontFamilyMono]);

    const rankBadgeStyle = useMemo((): React.CSSProperties => ({
        fontSize: "10rem",
        color: accents.schemes.accentDim,
        textTransform: "uppercase" as const,
        marginLeft: "12rem",
    }), [accents.schemes.accentDim]);

    const emptyStyle = useMemo((): React.CSSProperties => ({
        padding: "32rem",
        textAlign: "center" as const,
        color: theme.colors.textMuted,
        fontSize: "12rem",
        fontStyle: "italic" as const,
    }), [theme.colors.textMuted]);

    // ========== Main Render ==========

    return (
        <Column style={containerStyle}>
            {/* Online opt-in overlay */}
            <LeaderboardOptInOverlay
                onlineEnabled={onlineEnabled}
                onlineConsentRecorded={onlineConsentRecorded}
            />

            {/* Dimmed content when Online disabled */}
            <div style={dimmedStyle}>
                {/* Header */}
                <div style={headerStyle}>{l.t("UI_ARENA_GLOBAL_COMMANDERS")}</div>

                {/* Tabs */}
                <LeaderboardTabs activeTab={activeTab} onTabChange={setActiveTab} disabled={!onlineEnabled} />

                {leaderboard.lastRefreshResult.Status === "failed" && leaderboard.lastRefreshResult.ReasonId && (
                    <div style={emptyStyle}>
                        {l.tDynamic(leaderboard.lastRefreshResult.ReasonId)}
                    </div>
                )}

                <LeaderboardContent
                    activeTab={activeTab}
                    rankTiers={leaderboard.rankTiers}
                    leaderboard={leaderboard.leaderboard}
                    weeklyLeaderboard={leaderboard.weeklyLeaderboard}
                    styles={{
                        section: sectionStyle,
                        tierRows: tierRowStyles,
                        requirement: requirementStyle,
                        holderVacant: holderStyleVacant,
                        holderActive: holderStyleActive,
                        entryRows: entryRowStyles,
                        positionTop: positionStyleTop,
                        positionRest: positionStyleRest,
                        nickname: nicknameStyle,
                        stats: statsStyle,
                        rankBadge: rankBadgeStyle,
                        empty: emptyStyle,
                    }}
                />

                <LeaderboardPositionFooter
                    yourPosition={leaderboard.yourPosition}
                    yourWeeklyPosition={leaderboard.yourWeeklyPosition}
                />
            </div>
        </Column>
    );
});
LeaderboardPanel.displayName = "LeaderboardPanel";
