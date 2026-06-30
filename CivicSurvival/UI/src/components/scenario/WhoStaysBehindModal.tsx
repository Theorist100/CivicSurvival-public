/**
 * WhoStaysBehindModal - Exodus act begins
 *
 * Appears when ActChangedEvent(NewAct == Exodus) fires.
 * Emotional transition to the Exodus phase.
 */

import React, { useMemo } from "react";
import { createBaseModalStyles, useModalPalette } from "../../themes";
import { InfoList, InfoListItem, defineModal } from "../shared/modal";
import { dismissWhoStaysBehind } from "../../hooks/bindings/milestoneTutorialBindings";
import { useLocale } from "../../locales";
import { IconHome, IconGlobe, IconShield } from "../shared/common/Icons";

const WhoStaysBehindModalView: React.FC = () => {
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
                    <span style={base.headerIcon}><IconHome /></span>
                    <h2 style={base.title}>{l.t("MODAL_WHO_STAYS_TITLE")}</h2>
                </div>

                <div style={base.body}>
                    <p style={base.text}>
                        {l.t("MODAL_WHO_STAYS_TEXT_1")}
                    </p>

                    <p style={base.text}>
                        {l.t("MODAL_WHO_STAYS_TEXT_2")}
                    </p>

                    <InfoList title={l.t("MODAL_WHO_STAYS_PRIORITIES")}>
                        <InfoListItem
                            icon={<IconGlobe />}
                            text={l.t("MODAL_WHO_STAYS_PRIORITY_AID")}
                        />
                        <InfoListItem
                            icon={<IconShield />}
                            text={l.t("MODAL_WHO_STAYS_PRIORITY_DEFEND")}
                        />
                    </InfoList>

                    <p style={base.text}>
                        <span style={base.highlight}>{l.t("MODAL_WHO_STAYS_COURAGE")}</span>
                    </p>

                    <div style={base.buttonContainer}>
                        <button style={base.primaryButton} onClick={dismissWhoStaysBehind}>
                            {l.t("MODAL_WHO_STAYS_BUTTON")}
                        </button>
                    </div>
                </div>
            </div>
        </div>
    );
};

export const WhoStaysBehindModalDef = defineModal({
    id: "WhoStaysBehind",
    render: () => <WhoStaysBehindModalView />,
});
