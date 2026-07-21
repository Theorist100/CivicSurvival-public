/**
 * SpotterAlertModal - Enemy spotters detected
 *
 * Appears when SpotterStatsSingleton.TotalCount > 0 (poll-based).
 * Warns about OSINT threats and introduces counter-measures.
 */

import React, { useMemo } from "react";
import { createBaseModalStyles, useModalPalette } from "../../themes";
import { AlertBox, InfoList, InfoListItem, defineModal } from "../shared/modal";
import { dismissSpotterAlert } from "../../hooks/bindings/milestoneTutorialBindings";
import { useLocale } from "../../locales";
import { IconSearch, IconEye, IconShield } from "../shared/common/Icons";

const SpotterAlertModalView: React.FC = () => {
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
                    <span style={base.headerIcon}><IconSearch /></span>
                    <h2 style={base.title}>{l.t("MODAL_SPOTTER_TITLE")}</h2>
                </div>

                <div style={base.body}>
                    <AlertBox variant="warning" title={l.t("MODAL_SPOTTER_ALERT")}>
                        {l.t("MODAL_SPOTTER_ALERT_TEXT")}
                    </AlertBox>

                    <p style={base.text}>
                        {l.t("MODAL_SPOTTER_TEXT_1")}
                    </p>

                    <InfoList title={l.t("MODAL_SPOTTER_OPTIONS_TITLE")}>
                        <InfoListItem
                            icon={<IconEye />}
                            text={l.t("MODAL_SPOTTER_OPTION_SBU")}
                        />
                        <InfoListItem
                            icon={<IconShield />}
                            text={l.t("MODAL_SPOTTER_OPTION_COSINT")}
                        />
                    </InfoList>

                    <div style={base.buttonContainer}>
                        <button style={base.primaryButton} onClick={dismissSpotterAlert}>
                            {l.t("MODAL_SPOTTER_BUTTON")}
                        </button>
                    </div>
                </div>
            </div>
        </div>
    );
};

export const SpotterAlertModalDef = defineModal({
    id: "SpotterAlert",
    render: () => <SpotterAlertModalView />,
});
