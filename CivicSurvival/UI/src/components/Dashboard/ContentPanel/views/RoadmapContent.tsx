/**
 * @feature-free-static
 * RoadmapContent - Coming Soon Features & Support
 * OPS domain → ROADMAP view
 *
 * Shows planned features and ways to support development:
 * - PVP Arena (coming soon)
 * - Server Events (coming soon)
 * - Community suggestions
 * - Support links
 */

import React, { memo, useMemo } from "react";
import { Column, Row } from "@coherent";
import { useTheme, useAccents, type Theme, type Accents, hexToRgba } from "@themes";
import { IconSwords, IconGlobe, IconNews, IconRocket } from "@shared/common/Icons";
import { useSettingsActions } from "@hooks/actions";

interface RoadmapItem {
    id: string;
    Icon: React.FC;
    title: string;
    description: string;
    status: "coming" | "planned" | "community";
}

const createStyles = (t: Theme, accents: Accents) => ({
    container: {
        padding: "20rem",
        minHeight: "400rem",
    } as React.CSSProperties,

    header: {
        display: "flex",
        alignItems: "center",
        justifyContent: "space-between",
        marginBottom: "24rem",
        paddingBottom: "16rem",
        borderBottom: `2rem solid ${t.colors.border}`,
    } as React.CSSProperties,

    headerMain: {
        display: "flex",
        alignItems: "center",
    } as React.CSSProperties,

    headerIcon: {
        fontSize: "32rem",
        color: accents.operations.accent,
        marginRight: "14rem",
    } as React.CSSProperties,

    title: {
        fontSize: "20rem",
        fontWeight: 700,
        color: t.colors.textPrimary,
        letterSpacing: "0.5rem",
        textTransform: "uppercase" as const,
        marginBottom: "4rem",
    } as React.CSSProperties,

    subtitle: {
        fontSize: "12rem",
        color: t.colors.textSecondary,
        fontStyle: "italic" as const,
    } as React.CSSProperties,

    versionBadge: {
        fontSize: "11rem",
        fontWeight: 800,
        color: accents.operations.accent,
        background: hexToRgba(accents.operations.accent, 0.12),
        border: `2rem solid ${accents.operations.accent}`,
        borderRadius: "4rem",
        padding: "6rem 10rem",
        textTransform: "uppercase" as const,
        whiteSpace: "nowrap" as const,
    } as React.CSSProperties,

    section: {
        marginBottom: "20rem",
    } as React.CSSProperties,

    sectionTitle: {
        fontSize: "11rem",
        fontWeight: 700,
        color: t.colors.textMuted,
        textTransform: "uppercase" as const,
        letterSpacing: "0.5rem",
        marginBottom: "12rem",
    } as React.CSSProperties,

    card: {
        background: t.colors.paper,
        border: `2rem solid ${t.colors.border}`,
        borderRadius: t.layout.borderRadius,
        padding: "16rem",
        marginBottom: "12rem",
    } as React.CSSProperties,

    cardIcon: {
        fontSize: "24rem",
        color: accents.operations.accent,
        marginRight: "16rem",
        minWidth: "32rem",
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
    } as React.CSSProperties,

    cardContent: {
        flex: 1,
    } as React.CSSProperties,

    cardTitle: {
        fontSize: "14rem",
        fontWeight: 700,
        color: t.colors.textPrimary,
        marginBottom: "4rem",
    } as React.CSSProperties,

    cardDesc: {
        fontSize: "12rem",
        color: t.colors.textSecondary,
        lineHeight: 1.4,
    } as React.CSSProperties,

    badge: (status: "coming" | "planned" | "community") => {
        const colors = {
            coming: accents.resilience.accent,
            planned: accents.operations.accent,
            community: accents.schemes.accent,
        };
        return {
            fontSize: "10rem",
            fontWeight: 700,
            color: colors[status],
            background: hexToRgba(colors[status], 0.12),
            padding: "4rem 8rem",
            borderRadius: "4rem",
            textTransform: "uppercase" as const,
            marginLeft: "8rem",
        } as React.CSSProperties;
    },

    supportSection: {
        background: hexToRgba(accents.operations.accent, 0.06),
        border: `3rem solid ${accents.operations.accent}`,
        borderRadius: t.layout.borderRadius,
        padding: "20rem",
        textAlign: "center" as const,
    } as React.CSSProperties,

    supportTitle: {
        fontSize: "16rem",
        fontWeight: 700,
        color: accents.operations.accent,
        marginBottom: "12rem",
    } as React.CSSProperties,

    supportText: {
        fontSize: "12rem",
        color: t.colors.textSecondary,
        lineHeight: 1.6,
        marginBottom: "16rem",
    } as React.CSSProperties,

    supportLinks: {
        display: "flex",
        justifyContent: "center",
        flexWrap: "wrap" as const,
    } as React.CSSProperties,

    supportLink: {
        fontSize: "11rem",
        fontWeight: 600,
        color: accents.operations.accent,
        background: hexToRgba(accents.operations.accent, 0.12),
        border: "none",
        cursor: "pointer",
        padding: "8rem 16rem",
        borderRadius: t.layout.borderRadius,
        marginRight: "8rem",
        marginBottom: "8rem",
        textTransform: "uppercase" as const,
    } as React.CSSProperties,

    version: {
        fontSize: "10rem",
        color: t.colors.textMuted,
        textAlign: "center" as const,
        marginTop: "16rem",
        fontStyle: "italic" as const,
    } as React.CSSProperties,
});

const ROADMAP_ITEMS: RoadmapItem[] = [
    {
        id: "pvp",
        Icon: IconSwords,
        title: "PVP Arena",
        description: "Compete with other mayors in real-time grid warfare. Test your survival strategies against human opponents.",
        status: "coming",
    },
    {
        id: "events",
        Icon: IconGlobe,
        title: "Global Server Events",
        description: "Participate in seasonal challenges with mayors worldwide. Unite to survive large-scale crises together.",
        status: "coming",
    },
    {
        id: "community",
        Icon: IconNews,
        title: "Your Ideas Matter",
        description: "Have a feature suggestion? Join our Discord community and help shape the future of Civic Survival!",
        status: "community",
    },
];

const getBadgeText = (status: "coming" | "planned" | "community"): string => {
    switch (status) {
        case "coming": return "Coming Soon";
        case "planned": return "Planned";
        case "community": return "Community";
    }
};

export const RoadmapContent = memo(() => {
    const theme = useTheme();
    const accents = useAccents();
    const s = useMemo(() => createStyles(theme, accents), [theme, accents]);
    const settingsActions = useSettingsActions();

    return (
        <Column style={s.container}>
            {/* Header */}
            <div style={s.header}>
                <div style={s.headerMain}>
                    <div style={s.headerIcon}><IconRocket /></div>
                    <div>
                        <div style={s.title}>Mod Version 2 Roadmap</div>
                        <div style={s.subtitle}>Global Ops previews for the next major version</div>
                    </div>
                </div>
                <div style={s.versionBadge}>V2 Planned</div>
            </div>

            {/* Roadmap Items */}
            <div style={s.section}>
                <div style={s.sectionTitle}>Planned Features</div>
                {ROADMAP_ITEMS.map((item) => (
                    <Row key={item.id} align="flex-start" style={s.card}>
                        <div style={s.cardIcon}><item.Icon /></div>
                        <div style={s.cardContent}>
                            <Row align="center">
                                <span style={s.cardTitle}>{item.title}</span>
                                <span style={s.badge(item.status)}>{getBadgeText(item.status)}</span>
                            </Row>
                            <div style={s.cardDesc}>{item.description}</div>
                        </div>
                    </Row>
                ))}
            </div>

            {/* Support Section */}
            <div style={s.supportSection}>
                <div style={s.supportTitle}>Support Development</div>
                <div style={s.supportText}>
                    Civic Survival is a passion project born from real events.
                    Your support helps us continue development and bring new features to life.
                    Thank you for playing!
                </div>
                <div style={s.supportLinks}>
                    <button style={s.supportLink} onClick={settingsActions.openDiscord}>
                        Join our Discord
                    </button>
                </div>
            </div>

            {/* Version */}
            <div style={s.version}>Current beta: Civic Survival v1.0 Early Access. Global Ops systems are planned for Mod Version 2.</div>
        </Column>
    );
});
RoadmapContent.displayName = "RoadmapContent";

