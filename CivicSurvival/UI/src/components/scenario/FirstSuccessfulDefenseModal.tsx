/**
 * FirstSuccessfulDefenseModal - First wave where interceptions > hits
 *
 * Appears when WaveEndedEvent fires with Intercepted > Hits.
 * Positive reinforcement — your defense works!
 */

import React, { useMemo } from "react";
import { createBaseModalStyles, useModalPalette } from "../../themes";
import { InfoList, InfoListItem, defineModal } from "../shared/modal";
import { dismissFirstSuccessfulDefense } from "../../hooks/bindings/milestoneTutorialBindings";
import { useLocale } from "../../locales";
import { IconShield, IconTarget, IconLightning } from "../shared/common/Icons";

const FirstSuccessfulDefenseModalView: React.FC = () => {
    const l = useLocale();
    const m = useModalPalette();

    const base = useMemo(() => createBaseModalStyles(m, {
        accentColor: m.accents.success,
        overlayOpacity: 0.85,
        width: "420rem",    }), [m]);

    return (
        <div style={base.overlay}>
            <div style={base.modal}>
                <div style={base.header}>
                    <span style={base.headerIcon}><IconShield /></span>
                    <h2 style={base.title}>{l.t("MODAL_DEFENSE_TITLE")}</h2>
                </div>

                <div style={base.body}>
                    <p style={base.text}>
                        {l.t("MODAL_DEFENSE_TEXT_1")}
                    </p>

                    <p style={base.text}>
                        {l.t("MODAL_DEFENSE_TEXT_2")}
                    </p>

                    <InfoList title={l.t("MODAL_DEFENSE_TIPS_TITLE")}>
                        <InfoListItem
                            icon={<IconTarget />}
                            text={l.t("MODAL_DEFENSE_TIP_AMMO")}
                        />
                        <InfoListItem
                            icon={<IconLightning />}
                            text={l.t("MODAL_DEFENSE_TIP_UPGRADE")}
                        />
                    </InfoList>

                    <div style={base.buttonContainer}>
                        <button style={base.primaryButton} onClick={dismissFirstSuccessfulDefense}>
                            {l.t("MODAL_DEFENSE_BUTTON")}
                        </button>
                    </div>
                </div>
            </div>
        </div>
    );
};

export const FirstSuccessfulDefenseModalDef = defineModal({
    id: "FirstSuccessfulDefense",
    render: () => <FirstSuccessfulDefenseModalView />,
});
