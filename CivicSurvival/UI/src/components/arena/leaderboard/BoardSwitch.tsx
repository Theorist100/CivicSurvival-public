import React, { useMemo } from "react";
import { Row } from "@coherent";
import { useTheme, useAccents, hexToRgba } from "@themes";
import { useLocale } from "../../../locales";
import { type LadderBoard } from "./ladderView";

interface BoardSwitchProps {
    board: LadderBoard;
    onBoardChange: (board: LadderBoard) => void;
    disabled?: boolean;
}

/**
 * Defense / Shadow / Prosperity board switch above the ladder tabs. Three
 * ladders, one panel: the city you defended, the money you skimmed, and the
 * city that stayed livable. The active board carries its own accent
 * (defense = operations, shadow = schemes, prosperity = resilience) so which
 * game you are looking at reads before the numbers do.
 */
export const BoardSwitch: React.FC<BoardSwitchProps> = ({ board, onBoardChange, disabled = false }) => {
    const theme = useTheme();
    const accents = useAccents();
    const l = useLocale();

    const styles = useMemo(() => {
        const base: React.CSSProperties = {
            appearance: "none" as const,
            border: "none",
            fontSize: "11rem",
            fontWeight: 700,
            textTransform: "uppercase" as const,
            letterSpacing: "1rem",
            padding: "7rem 20rem",
            cursor: disabled ? "default" : "pointer",
        };
        const inactive: React.CSSProperties = {
            ...base,
            background: "transparent",
            color: theme.colors.textSecondary,
        };
        return {
            container: {
                marginBottom: "12rem",
                border: `2rem solid ${theme.colors.border}`,
                borderRadius: theme.layout.borderRadius,
                overflow: "hidden" as const,
                alignSelf: "flex-start" as const,
                opacity: disabled ? 0.5 : 1,
            } as React.CSSProperties,
            defenseActive: {
                ...base,
                background: accents.operations.accent,
                color: theme.colors.background,
            } as React.CSSProperties,
            shadowActive: {
                ...base,
                background: accents.schemes.accent,
                color: theme.colors.background,
            } as React.CSSProperties,
            prosperityActive: {
                ...base,
                background: accents.resilience.accent,
                color: theme.colors.background,
            } as React.CSSProperties,
            inactive,
            inactiveHoverDefense: { ...inactive, background: hexToRgba(accents.operations.accent, 0.12) },
            inactiveHoverShadow: { ...inactive, background: hexToRgba(accents.schemes.accent, 0.12) },
            inactiveHoverProsperity: { ...inactive, background: hexToRgba(accents.resilience.accent, 0.12) },
        };
    }, [theme.colors.border, theme.colors.textSecondary, theme.colors.background, theme.layout.borderRadius, accents.operations.accent, accents.schemes.accent, accents.resilience.accent, disabled]);

    return (
        <Row style={styles.container}>
            <button
                type="button"
                style={board === "defense" ? styles.defenseActive : styles.inactive}
                onClick={() => !disabled && onBoardChange("defense")}
                disabled={disabled}
            >
                {l.t("UI_ARENA_BOARD_DEFENSE")}
            </button>
            <button
                type="button"
                style={board === "shadow" ? styles.shadowActive : styles.inactive}
                onClick={() => !disabled && onBoardChange("shadow")}
                disabled={disabled}
            >
                {l.t("UI_ARENA_BOARD_SHADOW")}
            </button>
            <button
                type="button"
                style={board === "prosperity" ? styles.prosperityActive : styles.inactive}
                onClick={() => !disabled && onBoardChange("prosperity")}
                disabled={disabled}
            >
                {l.t("UI_ARENA_BOARD_PROSPERITY")}
            </button>
        </Row>
    );
};
