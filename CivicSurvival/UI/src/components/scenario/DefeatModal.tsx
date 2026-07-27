/**
 * DefeatModal - Game Over: City Lost
 *
 * "Your city didn't survive."
 *
 * Shows when population collapses or cognitive integrity is lost.
 * Local dismiss via useState (same pattern as ArrestedModal).
 *
 * z-index: inherits Z_INDEX.modal from createBaseModalStyles (single scale,
 * below vanilla system menus — see themes/zIndex.ts).
 */

import React, { useMemo } from "react";
import { createBaseModalStyles, useModalPalette } from "../../themes";
import { StatRow, StatSection, Quote, defineModal } from "../shared/modal";
import { useEndgameData } from "../../hooks/scenario";
import { dismissDefeat } from "../../hooks/bindings/scenarioDirectorBindings";
import { useLocale } from "../../locales";
import { IconAlert } from "../shared/common/Icons";

const DefeatModalView: React.FC = () => {
    const l = useLocale();
    const m = useModalPalette();
    const ACCENT = m.accents.crisis;
    const base = useMemo(() => createBaseModalStyles(m, {
        accentColor: m.accents.crisis,
        overlayOpacity: 0.92,
        width: "400rem",    }), [m]);

    const data = useEndgameData();
    const { raw } = data;

    return (
        <div style={base.overlay}>
            <div style={base.modal}>
                <div style={base.header}>
                    <span style={base.headerIcon}><IconAlert /></span>
                    <h2 style={base.title}>{l.t("MODAL_DEFEAT_TITLE")}</h2>
                    <div style={base.subtitle}>{l.t("MODAL_DEFEAT_SUBTITLE")}</div>
                </div>

                <div style={base.body}>
                    <p style={base.text}>{data.defeatCause.title}</p>
                    <p style={base.textItalicDimmed}>{data.defeatCause.description}</p>

                    <StatSection title={l.t("UI_DEFEAT_RECORD")}>
                        <StatRow label={l.t("MODAL_DEFEAT_STAT_DAYS")} value={raw.daysSurvived} valueColor={ACCENT} />
                        <StatRow label={l.t("MODAL_DEFEAT_STAT_WAVES")} value={raw.wavesDefended} />
                        {data.showBuildingsDamaged && <StatRow label={l.t("MODAL_STAT_BUILDINGS_DAMAGED")} value={raw.buildingsDamaged} />}
                        <StatRow label={l.t("MODAL_DEFEAT_STAT_POPULATION")} value={data.populationPercentDisplay} />
                    </StatSection>

                    <Quote accentColor={ACCENT}>
                        {data.defeatCause.quote}
                    </Quote>

                    <div style={base.buttonContainer}>
                        <button style={base.primaryButton} onClick={dismissDefeat}>
                            {l.t("MODAL_DEFEAT_BUTTON")}
                        </button>
                    </div>
                </div>
            </div>
        </div>
    );
};

export const DefeatModalDef = defineModal({
    id: "Defeat",
    render: () => <DefeatModalView />,
});
