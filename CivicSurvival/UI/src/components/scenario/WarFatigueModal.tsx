/**
 * WarFatigueModal - Six months of war
 *
 * "The world is getting tired. But the war continues."
 *
 * This modal appears at day 180, showing that the world's attention
 * is fading even as the war continues. Donor fatigue, news fatigue.
 *
 * Uses shared modal components for consistent styling.
 */

import React, { useMemo } from "react";
import { createBaseModalStyles, useModalPalette, useTheme } from "../../themes";
import { StatRow, StatSection, Quote, defineModal } from "../shared/modal";
import { useEndgameData } from "../../hooks/scenario";
import { dismissWarFatigue } from "../../hooks/bindings/scenarioDirectorBindings";
import { useLocale } from "../../locales";

// L-1: hover feedback for custom muted button (zero-cost CSS pseudo-class)
const FATIGUE_CSS = `.cs-fatigue-btn:hover { opacity: 0.8; }`;
// Module-level injection with duplicate guard (Coherent UI: <style> in JSX duplicates on every mount)
if (typeof document !== "undefined" && !document.querySelector("[data-cs-fatigue-css]")) {
    const el = document.createElement("style");
    el.setAttribute("data-cs-fatigue-css", "true");
    el.textContent = FATIGUE_CSS;
    document.head.appendChild(el);
}

const WarFatigueModalView: React.FC = () => {
    const l = useLocale();
    const m = useModalPalette();
    const theme = useTheme();
    const ACCENT = m.neutral.border;

    // NOTE: CS2 Coherent UI uses rem where 1rem ≈ 1px
    const customStyles = useMemo(() => ({
        dateBadge: {
            display: "flex",
            alignItems: "center",
            flexShrink: 0,
            padding: "6rem 16rem",
            backgroundColor: m.bgGray,
            borderRadius: "4rem",
            marginBottom: "12rem",
        } as React.CSSProperties,
        dateText: {
            color: m.textSecondary,
            fontSize: "14rem",
            letterSpacing: "2rem",
            fontWeight: "bold" as const,
        } as React.CSSProperties,
        button: {
            padding: "14rem 40rem",
            backgroundColor: m.borderGray,
            border: "none",
            borderRadius: "6rem",
            color: m.textPrimary,
            fontSize: "14rem",
            fontWeight: "bold" as const,
            fontFamily: theme.typography.fontFamilyMono,
            textTransform: "uppercase" as const,
            letterSpacing: "2rem",
            cursor: "pointer",
        } as React.CSSProperties,
    }), [m, theme.typography.fontFamilyMono]);

    const base = useMemo(() => createBaseModalStyles(m, {
        accentColor: ACCENT,
        overlayOpacity: 0.9,
        width: "400rem",    }), [m, ACCENT]);

    const data = useEndgameData();
    const { raw } = data;

    return (
        <div style={base.overlay}>
            <div style={base.modal}>
                <div style={base.header}>
                    <div style={customStyles.dateBadge}>
                        <span style={customStyles.dateText}>{l.t("MODAL_FATIGUE_DAY")}</span>
                    </div>
                    <h2 style={base.title}>{l.t("MODAL_FATIGUE_TITLE")}</h2>
                </div>

                <div style={base.body}>
                    <p style={base.text}>
                        {l.t("MODAL_FATIGUE_TEXT_1", l.t("MODAL_FATIGUE_180_DAYS"))}
                    </p>

                    {/* Statistics */}
                    <StatSection title={l.t("MODAL_FATIGUE_SECTION_TITLE")}>
                        <StatRow label={l.t("MODAL_FATIGUE_STAT_WAVES")} value={raw.wavesDefended} />
                        <StatRow label={l.t("MODAL_FATIGUE_STAT_MISSILES")} value={raw.missilesIntercepted} />
                        <StatRow label={l.t("MODAL_FATIGUE_STAT_BLACKOUTS")} value={raw.blackoutRecoveries} />
                        {data.showBuildingsDamaged && <StatRow label={l.t("MODAL_STAT_BUILDINGS_DAMAGED")} value={raw.buildingsDamaged} />}
                        <StatRow label={l.t("MODAL_FATIGUE_STAT_DAYS")} value={l.t("MODAL_FATIGUE_DAYS_VALUE")} />
                    </StatSection>

                    {/* War Fatigue effects */}
                    <StatSection title={l.t("MODAL_FATIGUE_ATTENTION")} showDivider>
                        <StatRow label={l.t("MODAL_FATIGUE_NEWS")} value={l.t("MODAL_FATIGUE_NEWS_VALUE")} valueColor={m.warning.text} />
                        <StatRow label={l.t("MODAL_FATIGUE_DONOR")} value={l.t("MODAL_FATIGUE_INCREASING")} valueColor={m.warning.text} />
                        <StatRow label={l.t("MODAL_FATIGUE_ATTENTION")} value={l.t("MODAL_FATIGUE_FADING")} valueColor={m.warning.text} />
                    </StatSection>

                    {/* Quote */}
                    <Quote author={l.t("MODAL_FATIGUE_QUOTE_AUTHOR")} accentColor={m.gray555}>
                        {l.t("MODAL_FATIGUE_QUOTE")}
                    </Quote>

                    <p style={base.text}>
                        {l.t("MODAL_FATIGUE_TEXT_2")}
                        <br />
                        <span style={base.highlight}>{l.t("MODAL_FATIGUE_TEXT_3")}</span>
                    </p>

                    {/* CSS injected at module level (data-cs-fatigue-css) */}
                    <div style={base.buttonContainer}>
                        <button className="cs-fatigue-btn" style={customStyles.button} onClick={dismissWarFatigue}>
                            {l.t("MODAL_FATIGUE_BUTTON")}
                        </button>
                    </div>
                </div>
            </div>
        </div>
    );
};

export const WarFatigueModalDef = defineModal({
    id: "WarFatigue",
    render: () => <WarFatigueModalView />,
});
