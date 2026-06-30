/**
 * RefugeeArrivingModal - Village scenario: refugees flood in
 *
 * "They're coming. Thousands of them."
 *
 * This modal appears when refugees start arriving at the village,
 * explaining the population surge and resource strain.
 *
 * Uses shared modal components for consistent styling.
 */

import React, { useMemo } from "react";
import { createBaseModalStyles, useModalPalette } from "../../themes";
import { Quote, StatCard, StatCardRow, AlertBox, defineModal } from "../shared/modal";
import { dismissRefugeeModal } from "../../hooks/bindings/refugeeBindings";
import { useLocale } from "../../locales";
import { IconHome, IconAlert } from "../shared/common/Icons";

const RefugeeArrivingModalView: React.FC = () => {
    const l = useLocale();
    const m = useModalPalette();
    const ACCENT = m.accents.info;

    const base = useMemo(() => createBaseModalStyles(m, {
        accentColor: m.accents.info,
        overlayOpacity: 0.85,
        width: "420rem",    }), [m]);

    return (
        <div style={base.overlay}>
            <div style={base.modal}>
                <div style={base.header}>
                    <span style={base.headerIcon}><IconHome /></span>
                    <h2 style={base.title}>{l.t("MODAL_REFUGEE_TITLE")}</h2>
                </div>

                <div style={base.body}>
                    <Quote author={l.t("MODAL_REFUGEE_QUOTE_AUTHOR")} accentColor={ACCENT}>
                        {l.t("MODAL_REFUGEE_QUOTE")}
                    </Quote>

                    <p style={base.text}>
                        {l.t("MODAL_REFUGEE_TEXT_1", l.t("MODAL_REFUGEE_SAFE_HAVEN"))}
                    </p>

                    <StatCardRow>
                        <StatCard
                            label={l.t("MODAL_REFUGEE_ARRIVALS")}
                            value={l.t("MODAL_REFUGEE_ARRIVALS_VALUE")}
                            valueColor={ACCENT}
                        />
                        <StatCard
                            label={l.t("MODAL_REFUGEE_DURATION")}
                            value={l.t("MODAL_REFUGEE_DURATION_VALUE")}
                            valueColor={ACCENT}
                        />
                        <StatCard
                            label={l.t("MODAL_REFUGEE_RATE")}
                            value={l.t("MODAL_REFUGEE_RATE_VALUE")}
                            valueColor={ACCENT}
                        />
                    </StatCardRow>

                    <AlertBox
                        variant="warning"
                        title={l.t("MODAL_REFUGEE_WARNING_TITLE")}
                        icon={<IconAlert />}
                    >
                        {l.t("MODAL_REFUGEE_WARNING")}
                    </AlertBox>

                    <AlertBox
                        variant="info"
                        title={l.t("MODAL_REFUGEE_PARK_TITLE")}
                        icon={<IconHome />}
                    >
                        {l.t("MODAL_REFUGEE_PARK_HINT")}
                    </AlertBox>

                    <p style={base.text}>
                        {l.t("MODAL_REFUGEE_TEXT_2")}
                    </p>
                    <p style={{ ...base.text, ...base.highlight }}>
                        {l.t("MODAL_REFUGEE_HELP")}
                    </p>

                    <div style={base.buttonContainer}>
                        <button style={base.primaryButton} onClick={dismissRefugeeModal}>
                            {l.t("MODAL_REFUGEE_BUTTON")}
                        </button>
                    </div>
                </div>
            </div>
        </div>
    );
};

export const RefugeeArrivingModalDef = defineModal({
    id: "Refugee",
    render: () => <RefugeeArrivingModalView />,
});
