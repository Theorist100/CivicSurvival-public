/**
 * RankUpCelebration - global top-center banner shown when the player's
 * displayed ladder score crosses a rank threshold upward (Duolingo-style
 * celebration). Mounted at root (like RejectToast), so it fires no matter
 * which panel is open.
 *
 * Silent until the first server confirmation of the session (progress.known):
 * the initial jump from "unknown" to the real score is a sync, not a rank-up.
 */

import React, { useEffect, useRef, useState } from "react";
import { Z_INDEX, hexToRgba, useTheme } from "themes";
import { type Theme } from "../../themes/types";
import { bindingDataOrDefault } from "@hooks/domain";
import { DEFAULT_ARENA_PROGRESS, useArenaProgress, type RankTierDef } from "@hooks/state";
import { useLocale } from "../../locales";
import { RANK_COLORS, RankIcon } from "./leaderboard/RankIcon";

const VISIBLE_MS = 6000;
const FADE_OUT_MS = 600;

export const RankUpCelebration: React.FC = () => {
    const theme = useTheme();
    const l = useLocale();
    const progress = bindingDataOrDefault(useArenaProgress(), DEFAULT_ARENA_PROGRESS);

    // null = no confirmed baseline yet this session.
    const prevMinScoreRef = useRef<number | null>(null);
    const [celebrated, setCelebrated] = useState<RankTierDef | null>(null);
    const [fadingOut, setFadingOut] = useState(false);
    const hideTimerRef = useRef<ReturnType<typeof setTimeout>>();
    const fadeTimerRef = useRef<ReturnType<typeof setTimeout>>();

    useEffect(() => {
        if (!progress.known) return;

        const prev = prevMinScoreRef.current;
        prevMinScoreRef.current = progress.tier.minScore;

        // First confirmed value of the session sets the baseline silently;
        // downward moves (config retune) also pass without fanfare.
        if (prev === null || progress.tier.minScore <= prev) return;

        setCelebrated(progress.tier);
        setFadingOut(false);
        if (hideTimerRef.current) clearTimeout(hideTimerRef.current);
        if (fadeTimerRef.current) clearTimeout(fadeTimerRef.current);
        hideTimerRef.current = setTimeout(() => {
            setFadingOut(true);
            fadeTimerRef.current = setTimeout(() => {
                setCelebrated(null);
                setFadingOut(false);
            }, FADE_OUT_MS);
        }, VISIBLE_MS);
    }, [progress.known, progress.tier]);

    useEffect(() => () => {
        if (hideTimerRef.current) clearTimeout(hideTimerRef.current);
        if (fadeTimerRef.current) clearTimeout(fadeTimerRef.current);
    }, []);

    if (celebrated === null) return null;

    const rankColor = RANK_COLORS[celebrated.icon] || RANK_COLORS.rank1;

    return (
        <div style={containerStyle(fadingOut)}>
            <div style={bannerStyle(theme, rankColor)}>
                <span style={iconStyle}><RankIcon icon={celebrated.icon} isVacant={false} /></span>
                <div>
                    <div style={kickerStyle(theme)}>{l.t("UI_ARENA_NEW_RANK")}</div>
                    <div style={rankNameStyle(rankColor)}>{celebrated.name}</div>
                </div>
            </div>
        </div>
    );
};

function containerStyle(fadingOut: boolean): React.CSSProperties {
    return {
        position: "absolute",
        top: "120rem",
        left: "50%",
        transform: "translateX(-50%)",
        zIndex: Z_INDEX.toast,
        // allow-pointer-events-none: decoration only
        pointerEvents: "none",
        opacity: fadingOut ? 0 : 1,
        transition: `opacity ${fadingOut ? FADE_OUT_MS : 200}ms ease-out`,
    };
}

function bannerStyle(theme: Theme, rankColor: string): React.CSSProperties {
    return {
        display: "flex",
        alignItems: "center",
        background: theme.colors.paper,
        border: `3rem solid ${rankColor}`,
        borderRadius: "10rem",
        padding: "14rem 22rem",
        boxShadow: `0 0 24rem ${hexToRgba(rankColor, 0.45)}`,
    };
}

const iconStyle: React.CSSProperties = {
    fontSize: "26rem",
    marginRight: "14rem",
};

function kickerStyle(theme: Theme): React.CSSProperties {
    return {
        fontSize: "10rem",
        fontWeight: 700,
        color: theme.colors.textMuted,
        textTransform: "uppercase" as const,
        letterSpacing: "1.5rem",
    };
}

function rankNameStyle(rankColor: string): React.CSSProperties {
    return {
        fontSize: "18rem",
        fontWeight: 700,
        color: rankColor,
        textTransform: "uppercase" as const,
        letterSpacing: "0.5rem",
    };
}
