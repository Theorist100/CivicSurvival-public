/**
 * BackButton — the drill-down "‹ BACK" control shared by the PSYOPS sub-views
 * (ToolSubView, ManageCenterSubView). The arrow is the SVG chevron icon, not a
 * "←" text glyph: the glyph sits below the label's optical centre in Noto Sans,
 * while a flex-centered icon lines up with the text exactly.
 */

import React from "react";
import { useTheme } from "../../../../../themes";
import { useLocale } from "../../../../../locales";
import { IconChevronLeft } from "../../../../shared/common/Icons";

interface BackButtonProps {
    onBack: () => void;
    /** Merged last — REQUIRED for Row/Flex gap emulation, which injects the gap
        margin via cloneElement(child, {style}); a component that drops the style
        prop silently loses the gap and the next sibling glues to the button. */
    style?: React.CSSProperties;
}

export const BackButton: React.FC<BackButtonProps> = ({ onBack, style: styleOverride }) => {
    const theme = useTheme();
    const l = useLocale();

    const style: React.CSSProperties = {
        display: "flex",
        alignItems: "center",
        padding: "6rem 12rem",
        fontSize: theme.typography.sizeXS,
        fontWeight: 700,
        fontFamily: theme.typography.fontFamilyMono,
        letterSpacing: "0.4rem",
        textTransform: "uppercase",
        color: theme.colors.textSecondary,
        backgroundColor: theme.colors.surface,
        border: `2rem solid ${theme.colors.border}`,
        borderRadius: theme.layout.borderRadius,
        cursor: "pointer",
        flexShrink: 0,
    };

    return (
        <button style={{ ...style, ...styleOverride }} onClick={onBack}>
            <span aria-hidden="true" style={{ display: "flex", marginRight: "6rem" }}>
                <IconChevronLeft />
            </span>
            {l.t("UI_OB_BACK")}
        </button>
    );
};
