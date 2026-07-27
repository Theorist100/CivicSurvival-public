/**
 * CorruptionOfferModal - First corruption suspicion
 *
 * Appears when CorruptionNarrativeEvent(SuspicionRising) fires.
 * Introduces the corruption/countermeasures system.
 */

import React, { useMemo } from "react";
import { createBaseModalStyles, useModalPalette } from "../../themes";
import { AlertBox, InfoList, InfoListItem, defineModal } from "../shared/modal";
import { dismissCorruptionOffer } from "../../hooks/bindings/milestoneTutorialBindings";
import { useLocale } from "../../locales";
import { IconSchemes, IconScales, IconEye } from "../shared/common/Icons";

const CorruptionOfferModalView: React.FC = () => {
    const l = useLocale();
    const m = useModalPalette();

    const base = useMemo(() => createBaseModalStyles(m, {
        accentColor: m.accents.warning,
        overlayOpacity: 0.85,
        width: "420rem",    }), [m]);

    return (
        <div style={base.overlay}>
            <div style={base.modal}>
                <div style={base.header}>
                    <span style={base.headerIcon}><IconSchemes /></span>
                    <h2 style={base.title}>{l.t("MODAL_CORRUPTION_TITLE")}</h2>
                </div>

                <div style={base.body}>
                    <AlertBox variant="warning" title={l.t("MODAL_CORRUPTION_ALERT")}>
                        {l.t("MODAL_CORRUPTION_ALERT_TEXT")}
                    </AlertBox>

                    <p style={base.text}>
                        {l.t("MODAL_CORRUPTION_TEXT_1")}
                    </p>

                    <p style={base.text}>
                        {l.t("MODAL_CORRUPTION_TEXT_2")}
                    </p>

                    <InfoList title={l.t("MODAL_CORRUPTION_OPTIONS_TITLE")}>
                        <InfoListItem
                            icon={<IconScales />}
                            text={l.t("MODAL_CORRUPTION_OPTION_CLEAN")}
                        />
                        <InfoListItem
                            icon={<IconEye />}
                            text={l.t("MODAL_CORRUPTION_OPTION_CAREFUL")}
                        />
                    </InfoList>

                    <div style={base.buttonContainer}>
                        <button style={base.primaryButton} onClick={dismissCorruptionOffer}>
                            {l.t("MODAL_CORRUPTION_BUTTON")}
                        </button>
                    </div>
                </div>
            </div>
        </div>
    );
};

export const CorruptionOfferModalDef = defineModal({
    id: "CorruptionOffer",
    render: () => <CorruptionOfferModalView />,
});
