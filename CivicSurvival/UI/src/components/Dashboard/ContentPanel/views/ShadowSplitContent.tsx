/**
 * ShadowSplitContent - Split layout for SHADOW domain
 * Left: Ledger (finances)
 * Right: Counter + Schemes + Procurement
 *
 * Uses reusable section components from components/shadow/sections
 */

import React, { memo, useMemo } from "react";
import { Column } from "@coherent";
import { useTheme, useAccents } from "@themes";
import {
    LedgerSection,
    TrustSection,
    SchemesSection,
    ProcurementSection,
} from "../../../shadow/sections";

// ============================================================================
// Styles
// ============================================================================

const createStyles = (_theme: ReturnType<typeof useTheme>, _accents: ReturnType<typeof useAccents>) => ({
    container: {
        display: "flex",
        width: "100%",
        height: "100%",
        padding: "8rem",
        boxSizing: "border-box" as const,
    } as React.CSSProperties,

    leftColumn: {
        width: "220rem",
        flexShrink: 0,
        overflowY: "auto" as const,
        marginRight: "12rem",
    } as React.CSSProperties,

    rightColumn: {
        flex: 1,
        minWidth: 0,
        overflowY: "auto" as const,
    } as React.CSSProperties,

    columnChild: {
        marginBottom: "8rem",
    } as React.CSSProperties,
});

// ============================================================================
// Component
// ============================================================================

export const ShadowSplitContent = memo(() => {
    const theme = useTheme();
    const accents = useAccents();
    const s = useMemo(() => createStyles(theme, accents), [theme, accents]);

    return (
        <div style={s.container}>
            {/* LEFT COLUMN: Ledger */}
            <Column style={s.leftColumn}>
                <LedgerSection />
            </Column>

            {/* RIGHT COLUMN: Trust + Schemes + Procurement */}
            <Column style={s.rightColumn}>
                <div style={s.columnChild}><TrustSection /></div>
                <div style={s.columnChild}><SchemesSection /></div>
                <div style={s.columnChild}><ProcurementSection /></div>
            </Column>
        </div>
    );
});
ShadowSplitContent.displayName = "ShadowSplitContent";
