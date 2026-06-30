import React from "react";
import { Column, Row } from "@coherent";
import { getPanelStyles, useTheme, useAccents, hexToRgba } from "@themes";
import { ProgressBar } from "@shared/ui";

const COPY = {
    comingSoon: "COMING SOON",
    title: "GRID WARFARE",
    subtitle: "\"Build Locally. Fight Globally.\"",
    battleActive: "GRID WARFARE PREVIEW",
    yourCity: "YOUR CITY",
    opponent: "OPPONENT",
    stability: "STABILITY",
    versus: "VS",
    budget: "Budget: $32M",
    defense: "Defense: 750",
    districts: "Districts: 8",
    arsenal: "ARSENAL",
    enter: "ENTER ARENA",
    footer: "Build your defenses first!",
};

const ATTACKS = [
    { icon: "D", name: "DRONES", cost: "$2M", accent: "crisis" },
    { icon: "B", name: "BLACKOUT", cost: "$500K", accent: "resilience" },
    { icon: "N", name: "DISINFO", cost: "$100K", accent: "schemes" },
    { icon: "S", name: "STRIKE", cost: "$10M", accent: "operations" },
] as const;

const useSectionStyle = () => {
    const theme = useTheme();
    return getPanelStyles(theme).card;
};

export const ArenaBattleStatusCard: React.FC = () => {
    const theme = useTheme();
    const accents = useAccents();
    const sectionStyle = useSectionStyle();

    return (
        <div style={{ ...sectionStyle, marginBottom: "12rem" }}>
            <Row justify="space-between" align="center">
                <span style={{
                    fontSize: "12rem",
                    fontWeight: 700,
                    color: accents.crisis.accent,
                    textTransform: "uppercase",
                }}>
                    {COPY.battleActive}
                </span>
                <span style={{
                    fontSize: "16rem",
                    fontWeight: 700,
                    color: theme.colors.textPrimary,
                    fontFamily: theme.typography.fontFamilyMono,
                }}>
                    V2
                </span>
            </Row>
        </div>
    );
};

export const ArenaStabilityComparison: React.FC = () => {
    const theme = useTheme();
    const accents = useAccents();
    const sectionStyle = useSectionStyle();

    const stabilityBoxStyle = (color: string): React.CSSProperties => ({
        background: hexToRgba(color, 0.12),
        border: `3rem solid ${color}`,
        borderRadius: theme.layout.borderRadius,
        padding: "12rem 16rem",
        minWidth: "180rem",
    });
    const stabilityLabelStyle: React.CSSProperties = {
        fontSize: "10rem",
        color: theme.colors.textMuted,
        textTransform: "uppercase",
        marginBottom: "4rem",
    };
    const stabilityValueStyle = (color: string): React.CSSProperties => ({
        fontSize: "24rem",
        fontWeight: 700,
        color,
        fontFamily: theme.typography.fontFamilyMono,
    });
    return (
        <Column gap="12rem" style={{ ...sectionStyle, marginBottom: "12rem" }}>
            <Row justify="space-between">
                <div style={stabilityBoxStyle(accents.schemes.accent)}>
                    <div style={stabilityLabelStyle}>{COPY.yourCity}</div>
                    <Row align="flex-end">
                        <span style={{ ...stabilityValueStyle(accents.schemes.accent), marginRight: "8rem" }}>78%</span>
                        <span style={{ fontSize: "11rem", color: theme.colors.textMuted }}>{COPY.stability}</span>
                    </Row>
                    <ProgressBar value={78} color={accents.schemes.accent} height="8rem" style={{ marginTop: "8rem" }} />
                </div>

                <Row align="center" style={{
                    padding: "0 16rem",
                    fontSize: "16rem",
                    fontWeight: 700,
                    color: theme.colors.textMuted,
                }}>
                    {COPY.versus}
                </Row>

                <div style={stabilityBoxStyle(accents.crisis.accent)}>
                    <div style={stabilityLabelStyle}>{COPY.opponent}</div>
                    <Row align="flex-end">
                        <span style={{ ...stabilityValueStyle(accents.crisis.accent), marginRight: "8rem" }}>52%</span>
                        <span style={{ fontSize: "11rem", color: theme.colors.textMuted }}>{COPY.stability}</span>
                    </Row>
                    <ProgressBar value={52} color={accents.crisis.accent} height="8rem" style={{ marginTop: "8rem" }} />
                </div>
            </Row>

            <Row justify="space-between" style={{
                fontSize: "11rem",
                color: theme.colors.textMuted,
                borderTop: `2rem solid ${theme.colors.border}`,
                paddingTop: "8rem",
            }}>
                <span>{COPY.budget}</span>
                <span>{COPY.defense}</span>
                <span>{COPY.districts}</span>
            </Row>
        </Column>
    );
};

export const ArenaArsenalCard: React.FC = () => {
    const theme = useTheme();
    const accents = useAccents();
    const sectionStyle = useSectionStyle();

    const accentMap = {
        crisis: accents.crisis.accent,
        resilience: accents.resilience.accent,
        schemes: accents.schemes.accent,
        operations: accents.operations.accent,
    };
    const attackBtnStyle = (color: string): React.CSSProperties => ({
        padding: "10rem 14rem",
        background: hexToRgba(color, 0.08),
        border: `2rem solid ${hexToRgba(color, 0.31)}`,
        borderRadius: theme.layout.borderRadius,
        minWidth: "80rem",
        marginRight: "8rem",
        marginBottom: "8rem",
    });
    const attackIconStyle: React.CSSProperties = {
        fontSize: "18rem",
        marginBottom: "4rem",
    };
    const attackNameStyle: React.CSSProperties = {
        fontSize: "10rem",
        fontWeight: 600,
        color: theme.colors.textPrimary,
        textTransform: "uppercase",
    };
    const attackCostStyle: React.CSSProperties = {
        fontSize: "10rem",
        color: theme.colors.textMuted,
        fontFamily: theme.typography.fontFamilyMono,
    };

    return (
        <div style={{ ...sectionStyle, marginBottom: "12rem" }}>
            <div style={{
                fontSize: "11rem",
                fontWeight: 700,
                color: theme.colors.textSecondary,
                marginBottom: "10rem",
                textTransform: "uppercase",
            }}>
                {COPY.arsenal}
            </div>
            <Row style={{ flexWrap: "wrap" }}>
                {ATTACKS.map((attack) => (
                    <Column key={attack.name} align="center" style={attackBtnStyle(accentMap[attack.accent])}>
                        <span style={attackIconStyle}>{attack.icon}</span>
                        <span style={attackNameStyle}>{attack.name}</span>
                        <span style={attackCostStyle}>{attack.cost}</span>
                    </Column>
                ))}
            </Row>
        </div>
    );
};

export const ArenaEnterCard: React.FC = () => {
    const theme = useTheme();
    const accents = useAccents();
    const sectionStyle = useSectionStyle();

    const enterBtnStyle: React.CSSProperties = {
        width: "100%",
        padding: "14rem",
        background: hexToRgba(accents.resilience.accent, 0.19),
        border: `3rem solid ${accents.resilience.accent}`,
        borderRadius: theme.layout.borderRadius,
        fontSize: "14rem",
        fontWeight: 700,
        color: accents.resilience.accent,
        textAlign: "center",
        textTransform: "uppercase",
    };
    const footerTextStyle: React.CSSProperties = {
        fontSize: "12rem",
        color: theme.colors.textMuted,
        textAlign: "center",
        marginTop: "8rem",
        fontStyle: "italic",
    };

    return (
        <div style={sectionStyle}>
            <div style={enterBtnStyle}>
                {COPY.enter}
            </div>
            <div style={footerTextStyle}>
                {COPY.footer}
            </div>
        </div>
    );
};
