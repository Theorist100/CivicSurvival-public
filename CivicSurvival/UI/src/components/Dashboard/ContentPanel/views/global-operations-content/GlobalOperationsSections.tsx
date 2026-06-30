import React from "react";
import { Column, Row } from "@coherent";
import { getPanelStyles, useTheme, useAccents, hexToRgba } from "@themes";
import { getTypeLabel, type Operation, type OperationType } from "./operations";

const COPY = {
    comingSoon: "COMING SOON",
    title: "GLOBAL OPERATIONS",
    subtitle: "\"Unite. Survive. Prevail.\"",
    activeOperations: "ACTIVE OPERATIONS",
    season: "Season 1 - Week 3",
    objective: "Objective",
    reward: "Reward",
    mayorsJoined: "mayors joined",
    join: "JOIN",
};

interface OperationsHeaderProps {
    headerStyle: React.CSSProperties;
}

interface OperationCardProps {
    operation: Operation;
    getTypeColor: (type: OperationType) => string;
}

export const OperationsHeader: React.FC<OperationsHeaderProps> = ({ headerStyle }) => {
    const theme = useTheme();

    return (
        <Row justify="space-between" align="center" style={headerStyle}>
            <span>{COPY.activeOperations}</span>
            <span style={{ fontSize: "10rem", color: theme.colors.textMuted }}>
                {COPY.season}
            </span>
        </Row>
    );
};

export const OperationCard: React.FC<OperationCardProps> = ({ operation: op, getTypeColor }) => {
    const theme = useTheme();
    const accents = useAccents();
    const panel = getPanelStyles(theme);

    const cardStyle = (type: OperationType): React.CSSProperties => ({
        ...panel.card,
        padding: 0,
        border: `3rem solid ${hexToRgba(getTypeColor(type), 0.31)}`,
        marginBottom: "12rem",
        overflow: "hidden",
    });
    const cardHeaderStyle = (type: OperationType): React.CSSProperties => ({
        padding: "12rem 16rem",
        background: hexToRgba(getTypeColor(type), 0.08),
        borderBottom: `2rem solid ${hexToRgba(getTypeColor(type), 0.19)}`,
    });
    const opNameStyle = (type: OperationType): React.CSSProperties => ({
        fontSize: "16rem",
        fontWeight: 700,
        color: getTypeColor(type),
        letterSpacing: "0.3rem",
    });
    const typeBadgeStyle = (type: OperationType): React.CSSProperties => ({
        fontSize: "10rem",
        fontWeight: 700,
        color: getTypeColor(type),
        background: hexToRgba(getTypeColor(type), 0.12),
        padding: "4rem 8rem",
        borderRadius: "4rem",
        textTransform: "uppercase",
    });
    const timeStyle: React.CSSProperties = {
        fontSize: "12rem",
        fontWeight: 600,
        color: theme.colors.textSecondary,
        fontFamily: theme.typography.fontFamilyMono,
    };
    const cardBodyStyle: React.CSSProperties = {
        padding: "12rem 16rem",
    };
    const descStyle: React.CSSProperties = {
        fontSize: "12rem",
        color: theme.colors.textSecondary,
        marginBottom: "10rem",
        lineHeight: 1.4,
    };
    const modifierStyle: React.CSSProperties = {
        fontSize: "10rem",
        color: accents.crisis.accent,
        background: hexToRgba(accents.crisis.accent, 0.08),
        padding: "3rem 8rem",
        borderRadius: "4rem",
        marginRight: "6rem",
        marginBottom: "4rem",
    };
    const labelStyle: React.CSSProperties = {
        fontSize: "10rem",
        fontWeight: 700,
        color: theme.colors.textMuted,
        textTransform: "uppercase",
        marginBottom: "4rem",
    };
    const objectiveStyle: React.CSSProperties = {
        fontSize: "12rem",
        color: theme.colors.textPrimary,
        fontWeight: 600,
    };
    const rewardStyle: React.CSSProperties = {
        fontSize: "12rem",
        color: accents.schemes.accent,
        fontWeight: 600,
    };
    const cardFooterStyle: React.CSSProperties = {
        padding: "10rem 16rem",
        borderTop: `2rem solid ${theme.colors.border}`,
        background: hexToRgba(theme.colors.border, 0.12),
    };
    const mayorsStyle: React.CSSProperties = {
        fontSize: "11rem",
        color: theme.colors.textMuted,
    };
    const mayorsCountStyle: React.CSSProperties = {
        fontWeight: 700,
        color: theme.colors.textSecondary,
    };
    const joinBtnStyle = (type: OperationType): React.CSSProperties => ({
        padding: "8rem 20rem",
        background: hexToRgba(getTypeColor(type), 0.19),
        border: `3rem solid ${getTypeColor(type)}`,
        borderRadius: theme.layout.borderRadius,
        fontSize: "11rem",
        fontWeight: 700,
        color: getTypeColor(type),
        textTransform: "uppercase",
    });

    return (
        <div style={cardStyle(op.type)}>
            <Row justify="space-between" align="center" style={cardHeaderStyle(op.type)}>
                <Row align="center">
                    <span style={opNameStyle(op.type)}>{op.name}</span>
                    <span style={{ ...typeBadgeStyle(op.type), marginLeft: "12rem" }}>
                        {getTypeLabel(op.type)}
                    </span>
                </Row>
                <span style={timeStyle}>{op.timeLeft}</span>
            </Row>

            <div style={cardBodyStyle}>
                <div style={descStyle}>{op.description}</div>

                <Row style={{ flexWrap: "wrap", marginBottom: "12rem" }}>
                    {op.modifiers.map((mod) => (
                        <span key={`${op.id}-${mod}`} style={modifierStyle}>{mod}</span>
                    ))}
                </Row>

                <Row justify="space-between">
                    <Column>
                        <div style={labelStyle}>{COPY.objective}</div>
                        <div style={objectiveStyle}>{op.objective}</div>
                    </Column>
                    <Column align="flex-end">
                        <div style={labelStyle}>{COPY.reward}</div>
                        <div style={rewardStyle}>{op.reward}</div>
                    </Column>
                </Row>
            </div>

            <Row justify="space-between" align="center" style={cardFooterStyle}>
                <span style={mayorsStyle}>
                    <span style={mayorsCountStyle}>{op.mayorsJoined.toLocaleString()}</span>
                    {" "}{COPY.mayorsJoined}
                </span>
                <div style={joinBtnStyle(op.type)}>{COPY.join}</div>
            </Row>
        </div>
    );
};
