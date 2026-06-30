/**
 * VictoryModal - One Year of Resistance
 *
 * "365 days. You made it."
 *
 * This modal appears at day 365, celebrating one year of survival.
 * The war isn't over, but survival is victory.
 *
 * Uses shared modal components for consistent styling.
 */

import React, { useCallback, useMemo } from "react";
import { createBaseModalStyles, useModalPalette } from "../../themes";
import { StatRow, StatSection, Quote, defineModal } from "../shared/modal";
import { HoverTipTarget } from "../shared/common/HoverTip";
import { useEndgameData } from "../../hooks/scenario";
import { oneMoreYear, endlessMode, oneMoreYearRequest$, endlessModeRequest$ } from "../../hooks/bindings/scenarioDirectorBindings";
import { useRequestAction } from "../../hooks/actions";
import { useDtoBinding } from "../../hooks/domain/useDtoBinding";
import { isRequestResult } from "../../types/dtoSubTypes";
import { useLocale } from "../../locales";
import { IconTrophy } from "../shared/common/Icons";

const VictoryModalView: React.FC = () => {
    const l = useLocale();
    const m = useModalPalette();
    const ACCENT = m.accents.info;

    // NOTE: CS2 Coherent UI uses rem where 1rem ≈ 1px
    const customStyles = useMemo(() => ({
        sunIcon: {
            fontSize: "48rem",
            marginBottom: "12rem",
            display: "block",
        } as React.CSSProperties,
        dateBadge: {
            display: "flex",
            alignItems: "center",
            flexShrink: 0,
            padding: "6rem 16rem",
            backgroundColor: m.bgBlue,
            border: `1rem solid ${m.borderBlue}`,
            borderRadius: "4rem",
            marginBottom: "12rem",
        } as React.CSSProperties,
        dateText: {
            color: m.textBlue,
            fontSize: "14rem",
            letterSpacing: "3rem",
            fontWeight: "bold" as const,
        } as React.CSSProperties,
        achievementBox: {
            padding: "12rem 16rem",
            backgroundColor: m.bgDarkBlue,
            border: `1rem solid ${m.accents.info}`,
            borderRadius: "6rem",
            marginBottom: "16rem",
            textAlign: "center" as const,
        } as React.CSSProperties,
        achievementTitle: {
            color: m.accents.info,
            fontSize: "12rem",
            fontWeight: "bold" as const,
            textTransform: "uppercase" as const,
            marginBottom: "6rem",
            letterSpacing: "2rem",
        } as React.CSSProperties,
        achievementDesc: {
            color: m.textMuted,
            fontSize: "12rem",
        } as React.CSSProperties,
    }), [m]);

    const base = useMemo(() => createBaseModalStyles(m, {
        accentColor: ACCENT,
        overlayOpacity: 0.92,
        width: "400rem",    }), [m, ACCENT]);

    const data = useEndgameData();
    const { raw } = data;
    const oneMoreYearRequestState = useDtoBinding(oneMoreYearRequest$, isRequestResult, { debugName: "oneMoreYearRequest" });
    const oneMoreYearAction = useRequestAction(
        () => {
            oneMoreYear();
            return true;
        },
        oneMoreYearRequestState.status === "ready" ? oneMoreYearRequestState.data : undefined
    );
    const endlessModeRequestState = useDtoBinding(endlessModeRequest$, isRequestResult, { debugName: "endlessModeRequest" });
    const endlessModeAction = useRequestAction(
        () => {
            endlessMode();
            return true;
        },
        endlessModeRequestState.status === "ready" ? endlessModeRequestState.data : undefined
    );
    const controlsDisabled = oneMoreYearAction.isPending || endlessModeAction.isPending;

    const handleEndlessMode = useCallback(() => {
        if (controlsDisabled) return;
        endlessModeAction.execute();
    }, [controlsDisabled, endlessModeAction]);

    return (
        <div style={base.overlay}>
            <div style={base.modal}>
                <div style={base.header}>
                    <span style={customStyles.sunIcon}><IconTrophy /></span>
                    <div style={customStyles.dateBadge}>
                        <span style={customStyles.dateText}>{l.t("MODAL_VICTORY_DAY", raw.daysSurvived)}</span>
                    </div>
                    <h2 style={base.title}>{l.t("MODAL_VICTORY_TITLE")}</h2>
                    <div style={base.subtitle}>{l.t("MODAL_VICTORY_SUBTITLE")}</div>
                </div>

                <div style={base.body}>
                    {/* Statistics */}
                    <StatSection title={l.t("UI_VICTORY_RECORD")}>
                        <StatRow label={l.t("MODAL_VICTORY_STAT_WAVES")} value={raw.wavesDefended} valueColor={ACCENT} />
                        <StatRow label={l.t("MODAL_VICTORY_STAT_MISSILES")} value={raw.missilesIntercepted} valueColor={ACCENT} />
                        <StatRow label={l.t("MODAL_VICTORY_STAT_BLACKOUTS")} value={raw.blackoutRecoveries} />
                        {data.showBuildingsDamaged && <StatRow label={l.t("MODAL_STAT_BUILDINGS_DAMAGED")} value={raw.buildingsDamaged} />}
                        <StatRow label={l.t("MODAL_VICTORY_STAT_REFUGEES")} value={data.refugeesDisplay} />
                        <StatRow label={l.t("MODAL_VICTORY_STAT_POPULATION")} value={data.populationPercentDisplay} />
                        <StatRow label={l.t("MODAL_VICTORY_STAT_DAYS")} value={raw.daysSurvived} valueColor={ACCENT} />
                    </StatSection>

                    {/* Achievement */}
                    <div style={customStyles.achievementBox}>
                        <div style={customStyles.achievementTitle}>{l.t("MODAL_VICTORY_ACHIEVEMENT")}</div>
                        <div style={customStyles.achievementDesc}>
                            {l.t("MODAL_VICTORY_ACHIEVEMENT_DESC")}
                        </div>
                    </div>

                    {/* Quote */}
                    <Quote author={l.t("MODAL_VICTORY_QUOTE_AUTHOR")} accentColor={ACCENT}>
                        {l.t("MODAL_VICTORY_QUOTE")}
                    </Quote>

                    <div style={base.buttonContainer}>
                        <HoverTipTarget text={l.t("MODAL_VICTORY_ONE_MORE_YEAR_DESC")}>
                            <button style={base.primaryButton} onClick={oneMoreYearAction.execute} disabled={controlsDisabled}>
                                {l.t("MODAL_VICTORY_BUTTON_ONE_MORE_YEAR")}
                            </button>
                        </HoverTipTarget>
                        <HoverTipTarget text={l.t("MODAL_VICTORY_ENDLESS_DESC")}>
                            <button style={base.secondaryButton} onClick={handleEndlessMode} disabled={controlsDisabled}>
                                {l.t("MODAL_VICTORY_BUTTON_ENDLESS")}
                            </button>
                        </HoverTipTarget>
                    </div>
                </div>
            </div>
        </div>
    );
};

export const VictoryModalDef = defineModal({
    id: "Victory",
    render: () => <VictoryModalView />,
});
