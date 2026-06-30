/**
 * FloatingChirperPanel - standalone news feed in our style, two display modes
 * mirroring vanilla Chirper:
 *   Mode 1 (bubble): newest citizen post pops up beside the bird button for a
 *                    few seconds, then auto-hides. Click opens the feed.
 *   Mode 2 (feed):   clicking the bird button opens the scrollable list with
 *                    Chipper (citizen posts) / Resistor (official news) tabs.
 *
 * Reuses ChipperSection / HeraldSection verbatim (they read their own bindings).
 * Pause-safe: mounted via moduleRegistry.append("Game", ...) → lives in
 * UIUpdate, ticks in pause. Read-only — sends nothing to the simulation.
 *
 * Backgrounds are opaque (not glass) so the feed stays legible over terrain.
 */

import React, { memo, useEffect, useMemo, useState } from "react";
import { Column, Row } from "../coherent";
import { useTheme, useAccents, hexToRgba } from "../../themes";
import { Z_INDEX } from "../../themes/zIndex";
import { useLocale } from "../../locales";
import { IconBird, IconNews, IconChevronRight } from "../shared/common/Icons";
import { Profiled } from "../../utils/uiProfiler";
import { useSafeString } from "../../hooks/useSafeBinding";
import { socialFeed$ } from "../../hooks/bindings";
import { useNowMinute } from "../../hooks/useNowMinute";
import { ChipperSection, HeraldSection } from "./sections";
import { parseSocialFeed, formatTimeAgo, type SocialPost } from "./sections/newsUtils";

const BUBBLE_DURATION_MS = 8000;
type FeedTab = "chipper" | "herald";

export const FloatingChirperPanel = memo(() => {
    const theme = useTheme();
    const accents = useAccents();
    const l = useLocale();
    const nowMinute = useNowMinute();
    const [open, setOpen] = useState(false);
    const [tab, setTab] = useState<FeedTab>("chipper");
    const [bubble, setBubble] = useState<SocialPost | null>(null);

    const accent = accents.resilience.accent;

    // Mode 1: latest citizen post drives the transient pop-up bubble.
    const feedJson = useSafeString(socialFeed$, "[]");
    const latest = useMemo(() => {
        const posts = parseSocialFeed(feedJson).filter((p) => !p.isOfficial);
        return posts.length > 0 ? posts[0] : null;
    }, [feedJson]);

    // Show the newest post as a bubble for a few seconds, then auto-hide.
    useEffect(() => {
        if (latest == null) return;
        setBubble(latest);
        const timer = window.setTimeout(() => setBubble(null), BUBBLE_DURATION_MS);
        return () => window.clearTimeout(timer);
        // eslint-disable-next-line react-hooks/exhaustive-deps -- re-fire only on a genuinely new post
    }, [latest?.author, latest?.timestamp]);

    const triggerStyle: React.CSSProperties = {
        position: "fixed",
        top: "60rem",
        right: "10rem",
        width: "40rem",
        height: "40rem",
        borderRadius: "50%",
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        background: theme.colors.surface,
        border: `2rem solid ${accent}`,
        boxShadow: theme.effects.shadowLg,
        color: accent,
        fontSize: "18rem",
        cursor: "pointer",
        pointerEvents: "auto",
        zIndex: Z_INDEX.raised,
    };

    // Collapsed: bird button + optional transient bubble (mode 1).
    if (!open) {
        const bubbleStyle: React.CSSProperties = {
            position: "fixed",
            top: "56rem",
            right: "60rem",
            width: "300rem",
            minHeight: "48rem",
            boxSizing: "border-box",
            padding: "10rem 12rem",
            display: "flex",
            flexDirection: "column",
            alignItems: "stretch",
            textAlign: "left",
            background: theme.colors.surface,
            border: `2rem solid ${accent}`,
            borderLeft: `4rem solid ${accent}`,
            borderRadius: theme.layout.borderRadius,
            boxShadow: theme.effects.shadowLg,
            cursor: "pointer",
            pointerEvents: "auto",
            zIndex: Z_INDEX.raised,
        };
        const avatarStyle: React.CSSProperties = {
            width: "26rem",
            height: "26rem",
            borderRadius: "50%",
            display: "flex",
            alignItems: "center",
            justifyContent: "center",
            marginRight: "8rem",
            flexShrink: 0,
            background: hexToRgba(accent, 0.19),
            color: accent,
        };
        const nameStyle: React.CSSProperties = {
            fontSize: "12rem",
            fontWeight: 700,
            color: accent,
        };
        const timeStyle: React.CSSProperties = {
            marginLeft: "auto",
            paddingLeft: "8rem",
            flexShrink: 0,
            fontSize: "10rem",
            color: theme.colors.textMuted,
        };
        const msgStyle: React.CSSProperties = {
            width: "100%",
            fontSize: "12rem",
            lineHeight: 1.4,
            color: theme.colors.textPrimary,
            overflowWrap: "break-word",
            wordBreak: "break-word",
        };
        return (
            <>
                {bubble != null && (
                    <button type="button" onClick={() => setOpen(true)} style={bubbleStyle}>
                        <Row align="center" style={{ width: "100%", marginBottom: "6rem" }}>
                            <span style={avatarStyle}><IconBird /></span>
                            <span style={nameStyle}>{bubble.authorName}</span>
                            <span style={timeStyle}>{formatTimeAgo(bubble.timestamp, nowMinute, l.t)}</span>
                        </Row>
                        <div style={msgStyle}>{bubble.message}</div>
                    </button>
                )}
                <button
                    type="button"
                    aria-label={l.t("UI_NEWS_PANEL_OPEN")}
                    onClick={() => setOpen(true)}
                    style={triggerStyle}
                >
                    <IconBird />
                </button>
            </>
        );
    }

    // Mode 2: expanded feed panel.
    const panelStyle: React.CSSProperties = {
        position: "fixed",
        top: "60rem",
        right: "10rem",
        width: "340rem",
        minHeight: "120rem",
        maxHeight: "640rem",
        display: "flex",
        flexDirection: "column",
        background: theme.colors.surface,
        border: `2rem solid ${theme.colors.border}`,
        borderRadius: theme.layout.borderRadius,
        boxShadow: theme.effects.shadowLg,
        overflow: "hidden",
        pointerEvents: "auto",
        zIndex: Z_INDEX.raised,
    };

    const headerStyle: React.CSSProperties = {
        padding: "8rem 10rem",
        borderBottom: `2rem solid ${theme.colors.border}`,
        background: theme.colors.paper,
        flexShrink: 0,
    };

    const tabStyle = (active: boolean): React.CSSProperties => ({
        display: "flex",
        alignItems: "center",
        marginRight: "8rem",
        padding: "4rem 8rem",
        background: "none",
        border: "none",
        borderBottom: `2rem solid ${active ? accent : "transparent"}`,
        color: active ? accent : theme.colors.textMuted,
        fontSize: "12rem",
        fontWeight: 700,
        letterSpacing: "1rem",
        cursor: "pointer",
    });

    const tabIconStyle: React.CSSProperties = {
        display: "inline-flex",
        marginRight: "4rem",
    };

    const closeStyle: React.CSSProperties = {
        marginLeft: "auto",
        display: "flex",
        alignItems: "center",
        padding: "4rem",
        background: "none",
        border: "none",
        color: theme.colors.textMuted,
        cursor: "pointer",
    };

    // Section components own their height/scroll; give them a flexible slot.
    const bodyStyle: React.CSSProperties = {
        flex: 1,
        minHeight: 0,
        display: "flex",
        flexDirection: "column",
    };

    return (
        <Column style={panelStyle}>
            <Row align="center" style={headerStyle}>
                <button type="button" onClick={() => setTab("chipper")} style={tabStyle(tab === "chipper")}>
                    <span style={tabIconStyle}><IconBird /></span>
                    {l.t("UI_CHIPPER_TITLE")}
                </button>
                <button type="button" onClick={() => setTab("herald")} style={tabStyle(tab === "herald")}>
                    <span style={tabIconStyle}><IconNews /></span>
                    {l.t("UI_HERALD_TITLE")}
                </button>
                <button
                    type="button"
                    aria-label={l.t("UI_NEWS_PANEL_CLOSE")}
                    onClick={() => setOpen(false)}
                    style={closeStyle}
                >
                    <IconChevronRight />
                </button>
            </Row>
            <div style={bodyStyle}>
                {tab === "chipper"
                    ? <Profiled id="FloatingChipper"><ChipperSection showHeader={false} /></Profiled>
                    : <Profiled id="FloatingHerald"><HeraldSection showHeader={false} /></Profiled>}
            </div>
        </Column>
    );
});
FloatingChirperPanel.displayName = "FloatingChirperPanel";
