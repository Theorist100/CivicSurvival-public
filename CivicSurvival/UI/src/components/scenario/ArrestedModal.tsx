/**
 * ArrestedModal - Game Over: Arrested
 *
 * "The corruption caught up with you."
 *
 * This modal appears when countermeasures phase reaches "Arrested".
 * The game continues but the player has lost their corrupt empire.
 *
 * Uses shared modal components for consistent styling.
 */

import React, { useMemo } from "react";
import { createBaseModalStyles, useModalPalette, formatMoney } from "../../themes";
import { StatCard, StatCardRow, Quote, defineModal } from "../shared/modal";
import { dismissArrested } from "../../hooks/bindings/modalCoordinatorBindings";
import { useLocale } from "../../locales";
import { IconScales } from "../shared/common/Icons";
import {
    isArrestedModalPayloadDto,
    type ArrestedModalPayloadDto,
} from "../../types/domainDtos";

interface ArrestedModalViewProps {
    payload: ArrestedModalPayloadDto;
}

const ArrestedModalView: React.FC<ArrestedModalViewProps> = ({ payload }) => {
    const l = useLocale();
    const m = useModalPalette();
    const ACCENT = m.accents.crisis;
    const base = useMemo(() => createBaseModalStyles(m, {
        accentColor: m.accents.crisis,
        overlayOpacity: 0.92,
        width: "380rem",    }), [m]);

    return (
        <div style={base.overlay}>
            <div style={base.modal}>
                <div style={base.header}>
                    <span style={base.headerIcon}><IconScales /></span>
                    <h2 style={base.title}>{l.t("MODAL_ARRESTED_TITLE")}</h2>
                    <div style={base.subtitle}>{l.t("MODAL_ARRESTED_SUBTITLE")}</div>
                </div>

                <div style={base.body}>
                    <p style={base.text}>
                        {l.t("MODAL_ARRESTED_TEXT_1")}
                    </p>

                    {payload.LastChoiceResult && (
                        <Quote accentColor={ACCENT}>
                            {payload.LastChoiceResult}
                        </Quote>
                    )}

                    <StatCardRow>
                        <StatCard
                            label={l.t("MODAL_ARRESTED_CHARGES")}
                            value={payload.ChargesCount}
                            valueColor={ACCENT}
                        />
                        <StatCard
                            label={l.t("MODAL_ARRESTED_SEIZED")}
                            value={payload.AssetsSeizedSnapshot > 0 ? formatMoney(payload.AssetsSeizedSnapshot) : "\u2014"}
                            valueColor={ACCENT}
                        />
                    </StatCardRow>

                    <p style={base.text}>
                        {l.t("MODAL_ARRESTED_TEXT_2")}
                    </p>

                    <p style={base.textItalicDimmed}>
                        {l.t("MODAL_ARRESTED_TEXT_3")}
                    </p>

                    <div style={base.buttonContainer}>
                        <button style={base.primaryButton} onClick={dismissArrested}>
                            {l.t("MODAL_ARRESTED_BUTTON")}
                        </button>
                    </div>
                </div>
            </div>
        </div>
    );
};

// Sole payload modal: the registry erases the payload type to `unknown`, so the
// guard runs here at the render boundary (was modalRoot's payloadType check).
export const ArrestedModalDef = defineModal({
    id: "Arrested",
    render: (payload) => isArrestedModalPayloadDto(payload)
        ? <ArrestedModalView payload={payload} />
        : null,
});
