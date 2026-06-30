/**
 * WarBeginsModal - War has started
 *
 * Appears when WarStartedEvent fires.
 * Sets the tone for the entire game.
 */

import React, { useMemo } from "react";
import { createBaseModalStyles, useModalPalette } from "../../themes";
import { AlertBox, InfoList, InfoListItem, defineModal } from "../shared/modal";
import { dismissWarBegins } from "../../hooks/bindings/milestoneTutorialBindings";
import { useLocale } from "../../locales";
import { IconTarget, IconShield, IconLightning } from "../shared/common/Icons";

const WarBeginsModalView: React.FC = () => {
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
                    <span style={base.headerIcon}><IconTarget /></span>
                    <h2 style={base.title}>{l.t("MODAL_WAR_BEGINS_TITLE")}</h2>
                </div>

                <div style={base.body}>
                    <AlertBox variant="danger" title={l.t("MODAL_WAR_BEGINS_ALERT")}>
                        {l.t("MODAL_WAR_BEGINS_ALERT_TEXT")}
                    </AlertBox>

                    <p style={base.text}>
                        {l.t("MODAL_WAR_BEGINS_TEXT_1")}
                    </p>

                    <p style={base.text}>
                        {l.t("MODAL_WAR_BEGINS_TEXT_2")}
                    </p>

                    <InfoList title={l.t("MODAL_WAR_BEGINS_PRIORITIES")}>
                        <InfoListItem
                            icon={<IconLightning />}
                            text={l.t("MODAL_WAR_BEGINS_PRIORITY_POWER")}
                        />
                        <InfoListItem
                            icon={<IconShield />}
                            text={l.t("MODAL_WAR_BEGINS_PRIORITY_DEFENSE")}
                        />
                    </InfoList>

                    <div style={base.buttonContainer}>
                        <button style={base.primaryButton} onClick={dismissWarBegins}>
                            {l.t("MODAL_WAR_BEGINS_BUTTON")}
                        </button>
                    </div>
                </div>
            </div>
        </div>
    );
};

export const WarBeginsModalDef = defineModal({
    id: "WarBegins",
    render: () => <WarBeginsModalView />,
});
