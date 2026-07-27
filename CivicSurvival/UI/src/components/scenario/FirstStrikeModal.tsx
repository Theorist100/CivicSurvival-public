/**
 * FirstStrikeModal - Shown after first infrastructure damage
 *
 * "Your city is now a target."
 *
 * This modal appears after the intro sequence when the first
 * building takes damage, driving home that this is not a drill.
 *
 * Uses shared modal components for consistent styling.
 */

import React, { useMemo } from "react";
import { createBaseModalStyles, useModalPalette } from "../../themes";
import { StatCard, StatCardRow, defineModal } from "../shared/modal";
import { dismissFirstStrike } from "../../hooks/bindings/shockActBindings";
import { useLocale } from "../../locales";
import { IconTarget } from "../shared/common/Icons";

const FirstStrikeModalView: React.FC = () => {
    const l = useLocale();
    const m = useModalPalette();
    const base = useMemo(() => createBaseModalStyles(m, {
        accentColor: m.accents.warning,
        overlayOpacity: 0.95,
        width: "420rem",    }), [m]);

    // C-2: pre-compute merged style (avoid combineStyles in render)
    const textHighlightLast = useMemo(() => ({
        ...base.text,
        ...base.highlight,
        marginBottom: 0,
    }), [base]);

    return (
        <div style={base.overlay}>
            <div style={base.modal}>
                <div style={base.header}>
                    <span style={base.headerIcon}><IconTarget /></span>
                    <h2 style={base.title}>{l.t("MODAL_FIRST_STRIKE_TITLE")}</h2>
                </div>

                <div style={base.body}>
                    <p style={base.text}>
                        {l.t("MODAL_FIRST_STRIKE_TEXT_1")}
                    </p>

                    <p style={base.text}>
                        {l.t("MODAL_FIRST_STRIKE_TEXT_2", l.t("MODAL_FIRST_STRIKE_MILITARY_TARGET"))}
                    </p>

                    <StatCardRow>
                        <StatCard
                            label={l.t("MODAL_FIRST_STRIKE_TAX")}
                            value={l.t("MODAL_FIRST_STRIKE_TAX_VALUE")}
                            valueColor={m.accents.warning}
                        />
                        <StatCard
                            label={l.t("MODAL_FIRST_STRIKE_LOANS")}
                            value={l.t("MODAL_FIRST_STRIKE_FROZEN")}
                            valueColor={m.accents.warning}
                        />
                        <StatCard
                            label={l.t("MODAL_FIRST_STRIKE_EXODUS")}
                            value={l.t("MODAL_FIRST_STRIKE_EXODUS_VALUE")}
                            valueColor={m.accents.warning}
                        />
                    </StatCardRow>

                    <p style={base.text}>
                        {l.t("MODAL_FIRST_STRIKE_TEXT_3")}
                    </p>
                    <p style={textHighlightLast}>
                        {l.t("MODAL_FIRST_STRIKE_SURVIVE")}
                    </p>

                    <div style={base.buttonContainer}>
                        <button style={base.primaryButton} onClick={dismissFirstStrike}>
                            {l.t("MODAL_FIRST_STRIKE_BUTTON")}
                        </button>
                    </div>
                </div>
            </div>
        </div>
    );
};

export const FirstStrikeModalDef = defineModal({
    id: "FirstStrike",
    render: () => <FirstStrikeModalView />,
});
