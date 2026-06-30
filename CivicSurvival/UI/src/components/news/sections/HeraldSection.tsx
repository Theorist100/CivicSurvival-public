/**
 * HeraldSection - The Resistor (Official News Feed)
 * NEWS domain → HERALD feed (right column)
 */

import React, { memo, useMemo } from "react";
import { Column, Row } from "../../coherent";
import { useTheme, useModalPalette } from "../../../themes";
import { useGlobalNewsData } from "../../../hooks/state";
import { type SocialMood, formatTimeAgo } from "./newsUtils";
import { useLocale } from "../../../locales";
import { useNowMinute } from "../../../hooks/useNowMinute";
import { GlassCase } from "../../shared/ui";
import { SocialMoodValues } from "../../../types/sharedEnums.generated";

// Inject @keyframes for pulse animation (with duplicate guard)
if (typeof document !== "undefined" && !document.querySelector("[data-civicsurvival-cs-pulse]")) {
    const style = document.createElement("style");
    style.setAttribute("data-civicsurvival-cs-pulse", "");
    style.textContent = `
@keyframes cs-pulse {
    0%, 100% { opacity: 1; transform: scale(1); }
    50% { opacity: 0.7; transform: scale(1.05); }
}`;
    document.head.appendChild(style);
}

// Herald-specific layout colors (mood→category palette lives in useGlobalNewsData).
const HERALD_BG = "#0a0a08";
const HERALD_TITLE = "#706050";
const HERALD_ONLINE = "#4a7c4a";
const HERALD_BREAKING_BORDER = "#8B0000";
const HERALD_CARD_SHADOW = "2rem 2rem 8rem rgba(0,0,0,0.4)";

export const HeraldSection = memo(({ showHeader = true }: { showHeader?: boolean }) => {
    const data = useGlobalNewsData();
    return (
        <GlassCase
            feature="Narrative"
            name="The Resistor"
            description="Official news feed driven by narrative events: breaking alerts, satirical posts, war-time bulletins. Citizens read this stream; tone shapes morale."
        >
            <HeraldSectionReady data={data} showHeader={showHeader} />
        </GlassCase>
    );
});

type HeraldData = ReturnType<typeof useGlobalNewsData>;

const HeraldSectionReady = memo(({ data, showHeader }: { data: HeraldData; showHeader: boolean }) => {
    const theme = useTheme();
    const mp = useModalPalette();
    const l = useLocale();
    const nowMinute = useNowMinute();
    const { news: globalNews, posts, hasBreaking, breakingMoodColor, categoryByMood } = data;

    // Styles
    const containerStyle: React.CSSProperties = {
        height: "100%",
        overflowY: "auto",
        padding: theme.spacing.md,
        background: HERALD_BG,
    };

    const mastheadStyle: React.CSSProperties = {
        textAlign: "center",
        padding: "16rem 0",
        borderBottom: `1rem solid ${mp.borderDark}`,
        marginBottom: theme.spacing.md,
    };

    const titleStyle: React.CSSProperties = {
        fontSize: "22rem",
        fontWeight: 700,
        color: HERALD_TITLE,
        letterSpacing: "4rem",
        textTransform: "uppercase",
        // The game ships no serif font — generic keeps the engine's serif mapping
        fontFamily: "serif",
    };

    const subtitleStyle: React.CSSProperties = {
        fontSize: "10rem",
        color: mp.heraldSource,
        letterSpacing: "3rem",
        marginTop: "4rem",
        textTransform: "uppercase",
    };

    const onlineIndicatorStyle: React.CSSProperties = {
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        marginTop: "10rem",
        fontSize: "11rem",
        color: globalNews.isConnected ? HERALD_ONLINE : mp.heraldSource,
    };

    const onlineDotStyle: React.CSSProperties = {
        width: "6rem",
        height: "6rem",
        borderRadius: "50%",
        background: globalNews.isConnected ? HERALD_ONLINE : mp.heraldSource,
        marginRight: "6rem",
        animation: globalNews.isConnected ? "cs-pulse 2s infinite" : "none",
    };

    const breakingBannerStyle: React.CSSProperties = {
        padding: "10rem 16rem",
        background: mp.newsCardDark,
        borderTop: `2rem solid ${HERALD_BREAKING_BORDER}`,
        borderBottom: `2rem solid ${HERALD_BREAKING_BORDER}`,
        textAlign: "center",
        marginBottom: theme.spacing.md,
    };

    const heraldMoodStyles = useMemo(() => {
        const cardBase: React.CSSProperties = {
            marginBottom: theme.spacing.md,
            border: `1rem solid ${mp.grayBorder}`,
            background: mp.heraldDark,
            boxShadow: HERALD_CARD_SHADOW,
        };
        const map: Record<SocialMood, { card: React.CSSProperties; header: React.CSSProperties }> = {} as Record<SocialMood, { card: React.CSSProperties; header: React.CSSProperties }>;
        for (const m of SocialMoodValues) {
            const cat = categoryByMood[m];
            const isDanger = m === "Warning" || m === "Angry";
            map[m] = {
                card: { ...cardBase, borderLeft: `3rem solid ${cat.color}` },
                header: {
                    padding: "8rem 12rem",
                    background: isDanger ? mp.heraldCardDanger : mp.heraldCardDark,
                    borderBottom: `1rem solid ${mp.grayBorder}`,
                },
            };
        }
        return map;
    }, [theme.spacing.md, mp.grayBorder, mp.heraldDark, mp.heraldCardDanger, mp.heraldCardDark, categoryByMood]);

    const cardHeaderChildStyle: React.CSSProperties = {
        marginRight: "8rem",
    };

    const aiBadgeStyle: React.CSSProperties = {
        marginLeft: "6rem",
        padding: "1rem 5rem",
        borderRadius: "3rem",
        border: `1rem solid ${mp.heraldSource}`,
        color: mp.heraldSource,
        fontSize: "10rem",
        fontWeight: 700,
        letterSpacing: "1rem",
        textTransform: "uppercase",
        flexShrink: 0,
    };

    const messageStyle: React.CSSProperties = {
        fontSize: "14rem",
        lineHeight: 1.6,
        color: mp.textHerald,
        fontFamily: "serif",
        textAlign: "justify",
        padding: "14rem 16rem",
    };

    const sourceStyle: React.CSSProperties = {
        padding: "10rem 16rem",
        borderTop: `1rem solid ${mp.grayBorder}`,
        fontSize: "10rem",
        color: mp.heraldSource,
    };

    const emptyStyle: React.CSSProperties = {
        textAlign: "center",
        padding: theme.spacing.xl,
        color: theme.colors.textMuted,
        fontStyle: "italic",
    };

    // Online indicator component (shows only when global news connection is enabled)
    const OnlineIndicator = globalNews.networkConnectionEnabled ? (
        <div style={onlineIndicatorStyle}>
            <div style={onlineDotStyle} />
            <span>
                {globalNews.isConnected
                    ? `${l.t("UI_HERALD_ONLINE", globalNews.onlineNow)} · ${l.t("UI_HERALD_ONLINE_TODAY", globalNews.onlineToday)}`
                    : globalNews.connectionStatus}
            </span>
        </div>
    ) : null;

    return (
        posts.length === 0 ? (
            <Column style={containerStyle}>
                {showHeader && (
                <div style={mastheadStyle}>
                    <div style={titleStyle}>{l.t("UI_HERALD_TITLE")}</div>
                    <div style={subtitleStyle}>{l.t("UI_HERALD_SUBTITLE")}</div>
                    {OnlineIndicator}
                </div>
                )}
                <div style={emptyStyle}>
                    <div style={{ fontSize: "24rem", marginBottom: "12rem" }}>{l.t("UI_HERALD_EMPTY_ICON")}</div>
                    <div>{l.t("UI_HERALD_EMPTY_TITLE")}</div>
                    <div style={{ fontSize: "11rem", marginTop: "4rem" }}>{l.t("UI_HERALD_EMPTY_SUBTITLE")}</div>
                </div>
            </Column>
            ) : (
            <Column style={containerStyle}>
            {/* Masthead */}
            {showHeader && (
            <div style={mastheadStyle}>
                <div style={titleStyle}>{l.t("UI_HERALD_TITLE")}</div>
                <div style={subtitleStyle}>{l.t("UI_HERALD_SUBTITLE")}</div>
                {OnlineIndicator}
            </div>
            )}

            {/* Breaking banner */}
            {hasBreaking && (
                <div style={breakingBannerStyle}>
                    <span style={{ color: breakingMoodColor, fontWeight: 700, letterSpacing: "3rem", fontSize: "12rem" }}>
                        ●{" "}{l.t("UI_HERALD_BREAKING")}
                    </span>
                </div>
            )}

            {/* News cards */}
            {posts.map((post) => {
                const cat = post.category;
                const moodStyle = heraldMoodStyles[post.mood] ?? heraldMoodStyles["Neutral"];
                return (
                    <div key={`${post.id}-${post.timestamp}`} style={moodStyle.card}>
                        <Row align="center" style={moodStyle.header}>
                            <span style={{ ...cardHeaderChildStyle, color: cat.color }}>{cat.icon}</span>
                            <span style={{
                                fontSize: "11rem",
                                fontWeight: 700,
                                color: cat.color,
                                letterSpacing: "2rem",
                                textTransform: "uppercase",
                            }}>
                                {l.t(cat.labelKey)}
                            </span>
                            {post.isAiGenerated && (
                                <span style={aiBadgeStyle} title={l.t("UI_HERALD_AI_BADGE_TITLE")}>
                                    {l.t("UI_HERALD_AI_BADGE")}
                                </span>
                            )}
                            <span style={{ marginLeft: "auto", fontSize: "10rem", color: mp.gray444, flexShrink: 0, paddingLeft: "8rem" }}>
                                {formatTimeAgo(post.timestamp, nowMinute, l.t)}
                            </span>
                        </Row>
                        <div style={messageStyle}>
                            {post.body.length > 0 ? `${post.title}: ${post.body}` : post.title}
                        </div>
                        <div style={sourceStyle}>
                            <span>
                                <span style={{ color: mp.gray444 }}>{l.t("UI_HERALD_SOURCE")}{" "}</span>
                                <span style={{ color: HERALD_TITLE, fontWeight: 700 }}>{post.source}</span>
                            </span>
                        </div>
                    </div>
                );
            })}
            </Column>
            )
    );
});
HeraldSectionReady.displayName = "HeraldSectionReady";
HeraldSection.displayName = "HeraldSection";
