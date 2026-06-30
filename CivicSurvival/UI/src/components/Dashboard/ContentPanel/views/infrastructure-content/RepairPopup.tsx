import React from "react";
import { Z_INDEX, formatMoney, getButtonStyles, useAccents, useTheme } from "@themes";

interface RepairPopupProps {
    embedded?: boolean;
    children: React.ReactNode;
}

interface RepairOptionProps {
    variant: "municipal" | "shadow";
    label: string;
    note: string;
    cost: number;
    duration: string;
    disabled?: boolean;
    onSelect: () => void;
    addon?: React.ReactNode;
    separated?: boolean;
}

interface KickbackToggleProps {
    enabled: boolean;
    label: string;
    onToggle: () => void;
}

export const RepairPopup: React.FC<RepairPopupProps> = ({ embedded = false, children }) => {
    const theme = useTheme();

    const popupPositionStyle: React.CSSProperties = embedded
        ? { position: "relative", marginTop: 0 }
        : { position: "absolute", top: "100%", right: 0, marginTop: "4rem" };

    return (
        <div
            style={{
                ...popupPositionStyle,
                background: theme.colors.paper,
                border: embedded ? "none" : `2rem solid ${theme.colors.border}`,
                borderRadius: theme.layout.borderRadius,
                boxShadow: embedded ? "none" : "0 4rem 16rem rgba(0,0,0,0.6)",
                zIndex: Z_INDEX.dropdown,
                width: "280rem",
                padding: "10rem",
                boxSizing: "border-box",
            }}
        >
            {children}
        </div>
    );
};

export const RepairOption: React.FC<RepairOptionProps> = ({
    variant,
    label,
    note,
    cost,
    duration,
    disabled = false,
    onSelect,
    addon,
    separated = false,
}) => {
    const theme = useTheme();
    const accents = useAccents();
    const buttonStyles = getButtonStyles(theme, accents);
    const color = variant === "municipal" ? accents.operations.accent : accents.schemes.accent;

    const buttonStyle: React.CSSProperties = {
        ...buttonStyles.action(color),
        width: "100%",
        boxSizing: "border-box",
        padding: "10rem 14rem",
        fontSize: "12rem",
        whiteSpace: "nowrap",
        cursor: disabled ? "not-allowed" : "pointer",
        opacity: disabled ? 0.5 : 1,
        background: disabled ? theme.colors.border : color,
        color: disabled ? theme.colors.textMuted : theme.colors.white,
        textAlign: "center",
    };

    const noteStyle: React.CSSProperties = {
        fontSize: "12rem",
        color: theme.colors.textMuted,
        marginTop: "4rem",
        marginBottom: "10rem",
        textAlign: "center",
    };

    const content = (
        <>
            <button style={buttonStyle} onClick={() => !disabled && onSelect()}>
                {`${label} - ${formatMoney(cost)} (${duration})`}
            </button>
            <div style={noteStyle}>{note}</div>
            {addon}
        </>
    );

    if (!separated) return content;

    return (
        <div style={{ borderTop: `2rem solid ${theme.colors.border}`, marginTop: "2rem", paddingTop: "10rem" }}>
            {content}
        </div>
    );
};

export const KickbackToggle: React.FC<KickbackToggleProps> = ({ enabled, label, onToggle }) => {
    const theme = useTheme();
    const accents = useAccents();

    return (
        <div
            style={{
                display: "flex",
                alignItems: "center",
                fontSize: "11rem",
                cursor: "pointer",
                marginTop: "4rem",
                marginBottom: "10rem",
            }}
            onClick={onToggle}
            onKeyDown={(e) => {
                if (e.key === "Enter" || e.key === " ") {
                    e.preventDefault();
                    onToggle();
                }
            }}
            role="button"
            tabIndex={0}
        >
            <span
                style={{
                    display: "flex",
                    width: "14rem",
                    height: "14rem",
                    border: `2rem solid ${enabled ? accents.schemes.accent : theme.colors.textMuted}`,
                    borderRadius: "3rem",
                    background: enabled ? accents.schemes.accent : "transparent",
                    marginRight: "6rem",
                    flexShrink: 0,
                    textAlign: "center",
                    lineHeight: "10rem",
                    fontSize: "10rem",
                    color: theme.colors.white,
                    fontWeight: 700,
                }}
            >
                {enabled ? "\u2713" : ""}
            </span>
            <span style={{ color: accents.schemes.accent }}>{label}</span>
        </div>
    );
};
