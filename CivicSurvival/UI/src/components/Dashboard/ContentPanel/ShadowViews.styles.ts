/**
 * SHADOW Domain Views styles
 * DonorsContent (Schemes/Counter views removed in 335939ada)
 */

import { type Theme, type Accents, createDomainStyles, hexToRgba } from "@themes";

export const createShadowViewsStyles = (theme: Theme, accents: Accents) => {
    const base = createDomainStyles({
        theme,
        accent: accents.schemes,
        fillContainer: true,
        titleFontSize: "11rem",
        titleMarginBottom: "8rem",
        titleDisplay: "flex",
        titleAlignItems: "center",
    });

    return {
        container: {
            display: "flex",
            flexDirection: "column" as const,
            alignItems: "stretch" as const,
            width: "100%",
            padding: "8rem",
            boxSizing: "border-box" as const,
            flex: 1,
        } as React.CSSProperties,

        section: base.section,

        buttonDisabled: (color: string, disabled: boolean) => ({
            width: "100%",
            padding: "10rem 12rem",
            background: hexToRgba(color, 0.08),
            border: `2rem solid ${color}`,
            borderRadius: theme.layout.borderRadius,
            color: color,
            cursor: disabled ? "not-allowed" : "pointer",
            fontSize: "11rem",
            fontWeight: 700,
            textTransform: "uppercase" as const,
            marginTop: "8rem",
            opacity: disabled ? 0.5 : 1,
        } as React.CSSProperties),
    };
};

