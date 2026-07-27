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
            padding: "6rem",
            boxSizing: "border-box" as const,
            flex: 1,
        } as React.CSSProperties,

        // Trim vertical padding (12rem → 8rem) so the four stacked sections fit the
        // fixed-height ALLIES view without scrolling; horizontal padding stays for readability.
        section: { ...base.section, padding: "8rem 12rem" } as React.CSSProperties,

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

