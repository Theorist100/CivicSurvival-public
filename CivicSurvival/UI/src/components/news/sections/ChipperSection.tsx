/**
 * ChipperSection - Citizen Posts Feed (Social Media)
 * NEWS domain → CHIPPER feed (left column)
 */

import React, { memo, useMemo, useState } from "react";
import { Column, Row } from "../../coherent";
import { useTheme, useAccents, type Accents, hexToRgba } from "../../../themes";
import { useSafeString } from "../../../hooks/useSafeBinding";
import { socialFeed$ } from "../../../hooks/bindings";
import { IconBird } from "../../shared/common/Icons";
import { type SocialMood, parseSocialFeed, formatTimeAgo } from "./newsUtils";
import { useLocale } from "../../../locales";
import { useNowMinute } from "../../../hooks/useNowMinute";
import { SocialMoodValues } from "../../../types/sharedEnums.generated";

const getMoodColor = (mood: SocialMood, accents: Accents): string => {
    switch (mood) {
        case "Angry":
        case "Warning":
            return accents.crisis.accent;
        case "Suffering":
        case "Paranoid":
            return accents.resilience.accent;
        case "Suspicious":
        case "Smug":
            return accents.schemes.accent;
        default:
            return "#888888";
    }
};

const getMoodIcon = (mood: SocialMood): string | null => {
    switch (mood) {
        case "Angry":
        case "Warning":
            return "!";
        case "Suffering":
        case "Paranoid":
            return "?";
        case "Suspicious":
            return "~";
        case "Smug":
            return "*";
        default:
            return null;
    }
};

export const ChipperSection = memo(({ showHeader = true }: { showHeader?: boolean }) => {
    const theme = useTheme();
    const accents = useAccents();
    const l = useLocale();
    const nowMinute = useNowMinute();

    const socialFeedJson = useSafeString(socialFeed$, "[]");
    const allPosts = useMemo(() => parseSocialFeed(socialFeedJson), [socialFeedJson]);

    // Filter remains as a compatibility guard for legacy official social posts.
    const posts = useMemo(() =>
        allPosts.filter(p => !p.isOfficial),
        [allPosts]
    );

    // S8-05: Cap visible posts to prevent flood
    const MAX_VISIBLE = 8;
    const [expanded, setExpanded] = useState(false);
    const visiblePosts = expanded ? posts : posts.slice(0, MAX_VISIBLE);
    const hasMore = posts.length > MAX_VISIBLE;

    // Styles
    const logoStyle: React.CSSProperties = {
        fontSize: "20rem",
        color: accents.resilience.accent,
        marginRight: "8rem",
    };

    const titleStyle: React.CSSProperties = {
        fontSize: "16rem",
        fontWeight: 700,
        color: accents.resilience.accent,
        letterSpacing: "1rem",
    };

    const countStyle: React.CSSProperties = {
        marginLeft: "auto",
        paddingLeft: "8rem",
        flexShrink: 0,
        fontSize: "11rem",
        color: theme.colors.textMuted,
    };

    const moodStyles = useMemo(() => {
        const postBase: React.CSSProperties = {
            padding: "12rem",
            background: theme.colors.paper,
            borderRadius: theme.layout.borderRadius,
            border: `2rem solid ${theme.colors.border}`,
        };
        const avatarBase: React.CSSProperties = {
            width: "28rem",
            height: "28rem",
            borderRadius: "50%",
            display: "flex",
            alignItems: "center",
            justifyContent: "center",
            fontSize: "14rem",
            marginRight: "8rem",
        };
        const map: Record<SocialMood, { post: React.CSSProperties; avatar: React.CSSProperties }> = {} as Record<SocialMood, { post: React.CSSProperties; avatar: React.CSSProperties }>;
        for (const m of SocialMoodValues) {
            const color = getMoodColor(m, accents);
            map[m] = {
                post: { ...postBase, borderLeft: `3rem solid ${color}` },
                avatar: { ...avatarBase, background: hexToRgba(color, 0.19) },
            };
        }
        return map;
    }, [theme.colors.paper, theme.layout.borderRadius, theme.colors.border, accents]);

    const authorStyle: React.CSSProperties = {
        fontSize: "12rem",
        fontWeight: 700,
        color: theme.colors.textPrimary,
    };

    const handleStyle: React.CSSProperties = {
        fontSize: "10rem",
        color: theme.colors.textMuted,
    };

    const timeStyle: React.CSSProperties = {
        marginLeft: "auto",
        paddingLeft: "8rem",
        flexShrink: 0,
        alignSelf: "flex-start",
        fontSize: "10rem",
        color: theme.colors.textMuted,
    };

    const messageStyle: React.CSSProperties = {
        fontSize: "13rem",
        lineHeight: 1.5,
        color: theme.colors.textSecondary,
    };

    return (
        <Column style={{ height: "100%", overflowY: "auto" as const, background: theme.colors.surface }}>
            {/* Header */}
            {showHeader && (
            <Row align="center" style={{
                padding: "12rem 16rem",
                borderBottom: `2rem solid ${theme.colors.border}`,
                background: theme.colors.paper,
            }}>
                <span style={logoStyle}><IconBird /></span>
                <span style={titleStyle}>{l.t("UI_CHIPPER_TITLE")}</span>
                <span style={countStyle}>{l.t("UI_CHIPPER_POST_COUNT", posts.length)}</span>
            </Row>
            )}

            {/* Feed */}
            {posts.length === 0 ? (
                <Column align="center" justify="center" style={{
                    padding: theme.spacing.xl,
                    color: theme.colors.textMuted,
                    textAlign: "center" as const,
                }}>
                    <div style={{ fontSize: "32rem", marginBottom: "12rem", color: accents.resilience.accent }}>
                        <IconBird />
                    </div>
                    <div style={{ fontSize: "14rem", fontWeight: 600 }}>{l.t("UI_CHIPPER_EMPTY_TITLE")}</div>
                    <div style={{ fontSize: "11rem", marginTop: "4rem" }}>
                        {l.t("UI_CHIPPER_EMPTY_SUBTITLE")}
                    </div>
                </Column>
            ) : (
                <Column gap={theme.spacing.sm} style={{ padding: theme.spacing.sm }}>
                    {visiblePosts.map((post) => {
                        const icon = getMoodIcon(post.mood);
                        return (
                            <div key={`${post.author}-${post.timestamp}`} style={moodStyles[post.mood].post}>
                                <Row align="center" style={{ marginBottom: "8rem" }}>
                                    <div style={moodStyles[post.mood].avatar}>
                                        {icon != null ? icon : <IconBird />}
                                    </div>
                                    <div>
                                        <div style={authorStyle}>{post.authorName}</div>
                                        <div style={handleStyle}>{post.author}</div>
                                    </div>
                                    <span style={timeStyle}>{formatTimeAgo(post.timestamp, nowMinute, l.t)}</span>
                                </Row>
                                <div style={messageStyle}>{post.message}</div>
                            </div>
                        );
                    })}
                    {/* S8-05: Show more/less toggle */}
                    {hasMore && (
                        <button
                            onClick={() => setExpanded(!expanded)}
                            style={{
                                background: "none",
                                border: "none",
                                color: accents.resilience.accent,
                                fontSize: "11rem",
                                fontWeight: 600,
                                cursor: "pointer",
                                padding: "8rem",
                                textAlign: "center" as const,
                            }}
                        >
                            {expanded
                                ? l.t("UI_CHIPPER_SHOW_LESS")
                                : l.t("UI_CHIPPER_SHOW_MORE", posts.length - MAX_VISIBLE)}
                        </button>
                    )}
                </Column>
            )}
        </Column>
    );
});
ChipperSection.displayName = "ChipperSection";
