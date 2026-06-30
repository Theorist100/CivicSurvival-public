/**
 * FirstDonorAidModal - First international aid received
 *
 * Appears when DonorEvent(FundsReceived) fires for the first time.
 * Explains the donor/diplomacy system.
 */

import React, { useMemo } from "react";
import { createBaseModalStyles, useModalPalette } from "../../themes";
import { InfoList, InfoListItem, defineModal } from "../shared/modal";
import { dismissFirstDonorAid } from "../../hooks/bindings/milestoneTutorialBindings";
import { useLocale } from "../../locales";
import { IconGlobe, IconHandshake, IconShield } from "../shared/common/Icons";

const FirstDonorAidModalView: React.FC = () => {
    const l = useLocale();
    const m = useModalPalette();

    const base = useMemo(() => createBaseModalStyles(m, {
        accentColor: m.accents.info,
        overlayOpacity: 0.85,
        width: "420rem",    }), [m]);

    return (
        <div style={base.overlay}>
            <div style={base.modal}>
                <div style={base.header}>
                    <span style={base.headerIcon}><IconGlobe /></span>
                    <h2 style={base.title}>{l.t("MODAL_FIRST_DONOR_TITLE")}</h2>
                </div>

                <div style={base.body}>
                    <p style={base.text}>
                        {l.t("MODAL_FIRST_DONOR_TEXT_1")}
                    </p>

                    <p style={base.text}>
                        {l.t("MODAL_FIRST_DONOR_TEXT_2")}
                    </p>

                    <InfoList title={l.t("MODAL_FIRST_DONOR_ADVICE_TITLE")}>
                        <InfoListItem
                            icon={<IconHandshake />}
                            text={l.t("MODAL_FIRST_DONOR_ADVICE_TRUST")}
                        />
                        <InfoListItem
                            icon={<IconShield />}
                            text={l.t("MODAL_FIRST_DONOR_ADVICE_SPEND")}
                        />
                    </InfoList>

                    <div style={base.buttonContainer}>
                        <button style={base.primaryButton} onClick={dismissFirstDonorAid}>
                            {l.t("MODAL_FIRST_DONOR_BUTTON")}
                        </button>
                    </div>
                </div>
            </div>
        </div>
    );
};

export const FirstDonorAidModalDef = defineModal({
    id: "FirstDonorAid",
    render: () => <FirstDonorAidModalView />,
});
