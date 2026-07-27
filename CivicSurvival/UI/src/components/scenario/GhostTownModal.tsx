/**
 * GhostTownModal - Population collapse warning
 *
 * Appears when current population < 30% of peak (poll-based).
 * Last warning before defeat by depopulation.
 */

import React, { useMemo } from "react";
import { createBaseModalStyles, useModalPalette } from "../../themes";
import { AlertBox, InfoList, InfoListItem, defineModal } from "../shared/modal";
import { dismissGhostTown } from "../../hooks/bindings/milestoneTutorialBindings";
import { useLocale } from "../../locales";
import { IconAlert, IconHome, IconLightning } from "../shared/common/Icons";

const GhostTownModalView: React.FC = () => {
    const l = useLocale();
    const m = useModalPalette();

    const base = useMemo(() => createBaseModalStyles(m, {
        accentColor: m.accents.crisis,
        overlayOpacity: 0.85,
        width: "420rem",    }), [m]);

    return (
        <div style={base.overlay}>
            <div style={base.modal}>
                <div style={base.header}>
                    <span style={base.headerIcon}><IconAlert /></span>
                    <h2 style={base.title}>{l.t("MODAL_GHOST_TOWN_TITLE")}</h2>
                </div>

                <div style={base.body}>
                    <AlertBox variant="danger" title={l.t("MODAL_GHOST_TOWN_ALERT")}>
                        {l.t("MODAL_GHOST_TOWN_ALERT_TEXT")}
                    </AlertBox>

                    <p style={base.text}>
                        {l.t("MODAL_GHOST_TOWN_TEXT_1")}
                    </p>

                    <p style={base.text}>
                        {l.t("MODAL_GHOST_TOWN_TEXT_2")}
                    </p>

                    <InfoList title={l.t("MODAL_GHOST_TOWN_LAST_CHANCE")}>
                        <InfoListItem
                            icon={<IconLightning />}
                            text={l.t("MODAL_GHOST_TOWN_TIP_POWER")}
                        />
                        <InfoListItem
                            icon={<IconHome />}
                            text={l.t("MODAL_GHOST_TOWN_TIP_SERVICES")}
                        />
                    </InfoList>

                    <div style={base.buttonContainer}>
                        <button style={base.primaryButton} onClick={dismissGhostTown}>
                            {l.t("MODAL_GHOST_TOWN_BUTTON")}
                        </button>
                    </div>
                </div>
            </div>
        </div>
    );
};

export const GhostTownModalDef = defineModal({
    id: "GhostTown",
    render: () => <GhostTownModalView />,
});
