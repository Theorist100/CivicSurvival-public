/**
 * PreWarStatusSection - Pre-war onboarding explainer for the INTEL forecast column.
 *
 * Shown only while the scenario is in the Pre-War act (currentAct < Crisis), where the
 * Tension / Enemy Focus / Attack Forecast widgets carry no data yet. It replaces that
 * empty forecast with a plain "the war starts on its own" message so the silence reads
 * as an expected phase instead of "the mod isn't working".
 *
 * Two variants:
 * - Village (started below the Town threshold): no cold-open plays at all, so the city grows
 *   toward the war milestone through OminousSigns with no other on-screen cue. This is the main gap.
 * - Town / City: the cold-open already set the scene; pre-war here is momentary.
 */

import React, { memo, useMemo } from "react";
import { useTheme, useAccents } from "@themes";
import { useLocale } from "@locales";
import { createWarViewsStyles } from "../../../Dashboard/ContentPanel/WarViews.styles";

export interface PreWarStatusSectionProps {
    isVillage: boolean;
}

export const PreWarStatusSection: React.FC<PreWarStatusSectionProps> = memo(({ isVillage }) => {
    const theme = useTheme();
    const accents = useAccents();
    const l = useLocale();
    const s = useMemo(() => createWarViewsStyles(theme, accents), [theme, accents]);

    // Village = calm/neutral accent (not a target yet); Town/City pre-war = imminent.
    const color = isVillage ? accents.schemes.accent : accents.crisis.accent;
    const head = isVillage ? l.t("PREWAR_STATUS_VILLAGE_HEAD") : l.t("PREWAR_STATUS_PENDING_HEAD");
    const body = isVillage ? l.t("PREWAR_STATUS_VILLAGE_BODY") : l.t("PREWAR_STATUS_PENDING_BODY");

    return (
        <div style={s.section}>
            <div style={s.sectionTitleColored(color)}>{l.t("PREWAR_STATUS_TITLE")}</div>
            <div style={s.assessment(color)}>{head}</div>
            <div style={s.note}>{body}</div>
        </div>
    );
});

PreWarStatusSection.displayName = "PreWarStatusSection";
