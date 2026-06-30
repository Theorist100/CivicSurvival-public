import React, { useState } from "react";
import { hexToRgba, useAccents, useTheme } from "@themes";
import { useLocale } from "@locales";
import { StatusBadge } from "@shared/ui";

/**
 * Collapsed "destroyed" group shared by the power-plant and civilian repair tables.
 * Vanilla-Destroyed buildings are kept in the snapshot (their sidecar drives the
 * DESTROYED counter) but are NOT mod-repairable — listing them as active repair rows
 * showed a broken REPAIR button (resolved server-side as NOT_FOUND). They are also not
 * silently dropped from the list, which would read as "the plant vanished". Instead they
 * collapse into one header row that expands to the lost buildings, each marked DESTROYED.
 */
export interface DestroyedRow {
    key: string;
    name: string;
    detail: string;
}

interface DestroyedGroupProps {
    rows: DestroyedRow[];
}

export const DestroyedGroup: React.FC<DestroyedGroupProps> = ({ rows }) => {
    const theme = useTheme();
    const accents = useAccents();
    const l = useLocale();
    const [expanded, setExpanded] = useState(false);

    if (rows.length === 0) return null;

    const headerStyle: React.CSSProperties = {
        display: "flex",
        alignItems: "center",
        padding: `${theme.spacing.xs} ${theme.spacing.sm}`,
        cursor: "pointer",
        color: theme.colors.textMuted,
        fontWeight: 600,
        fontSize: "12rem",
        background: hexToRgba(accents.crisis.accent, 0.06),
        borderBottom: `1rem solid ${theme.colors.border}`,
    };
    const rowStyle: React.CSSProperties = {
        display: "flex",
        alignItems: "center",
        padding: `${theme.spacing.xs} ${theme.spacing.sm}`,
        fontSize: "12rem",
        color: theme.colors.textMuted,
        background: hexToRgba(accents.crisis.accent, 0.03),
        borderBottom: `1rem solid ${theme.colors.border}`,
        opacity: 0.75,
    };

    return (
        <div style={{ overflow: "visible" }}>
            <div
                style={headerStyle}
                role="button"
                tabIndex={0}
                onClick={() => setExpanded((e) => !e)}
                onKeyDown={(e) => { if (e.key === "Enter" || e.key === " ") { e.preventDefault(); setExpanded((v) => !v); } }}
            >
                <span style={{ marginRight: "6rem", display: "inline-block", width: "10rem" }}>
                    {expanded ? "▾" : "▸"}
                </span>
                {l.t("INFRA_DESTROYED_GROUP", rows.length)}
            </div>
            {expanded && rows.map((r) => (
                <div key={r.key} style={rowStyle}>
                    <span style={{ flex: 1, fontWeight: 600 }}>{r.name}</span>
                    <span style={{ marginRight: "8rem", fontFamily: theme.typography.fontFamilyMono }}>{r.detail}</span>
                    <StatusBadge color={accents.crisis.accent} bgColor={hexToRgba(accents.crisis.accent, 0.15)}>
                        {l.t("INFRA_DESTROYED_BADGE")}
                    </StatusBadge>
                </div>
            ))}
        </div>
    );
};
