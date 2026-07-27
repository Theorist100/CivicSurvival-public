/**
 * Sep — a font-safe inline separator. The in-game Coherent mono font has no
 * middle-dot glyph (a literal "·" renders as tofu), so token separators are drawn
 * as a thin, dim vertical hairline instead of a Unicode character. A hairline
 * aligns to the text height and reads as a clean divider, without the floating
 * "bullet" look a small circle gives when it repeats across the board.
 */

import React, { memo } from "react";
import { useTheme } from "../../../../../themes";

export const Sep = memo(() => {
    const theme = useTheme();
    return (
        <span
            style={{
                display: "flex",
                alignSelf: "center",
                width: "2rem",
                height: "9rem",
                backgroundColor: theme.colors.border,
                margin: "0 7rem",
                flexShrink: 0,
            }}
        />
    );
});

Sep.displayName = "Sep";
