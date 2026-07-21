/**
 * ExodusWarningModal - Population exodus warning
 *
 * "People are leaving. Your city is dying."
 *
 * This modal appears when 2% of the population has fled the city.
 * Warns the player about population loss and gives survival advice.
 *
 * Uses shared modal components for consistent styling.
 */

import React, { useMemo } from "react";
import { createBaseModalStyles, useModalPalette } from "../../themes";
import { AlertBox, InfoList, InfoListItem, defineModal } from "../shared/modal";
import { dismissExodusWarning } from "../../hooks/bindings/shockActBindings";
import { useLocale } from "../../locales";
import { IconAlert, IconLightning, IconShield, IconGlobe } from "../shared/common/Icons";

const ExodusWarningModalView: React.FC = () => {
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
                    <h2 style={base.title}>{l.t("MODAL_EXODUS_TITLE")}</h2>
                </div>

                <div style={base.body}>
                    <AlertBox variant="danger" title={l.t("MODAL_EXODUS_WARNING")}>
                        {l.t("MODAL_EXODUS_WARNING_TEXT")}
                    </AlertBox>

                    <p style={base.text}>
                        {l.t("MODAL_EXODUS_TEXT_1")}
                    </p>

                    <p style={base.text}>
                        {l.t("MODAL_EXODUS_TEXT_2")}
                    </p>

                    <InfoList title={l.t("MODAL_EXODUS_ADVICE_TITLE")}>
                        <InfoListItem
                            icon={<IconLightning />}
                            text={l.t("MODAL_EXODUS_ADVICE_POWER")}
                        />
                        <InfoListItem
                            icon={<IconShield />}
                            text={l.t("MODAL_EXODUS_ADVICE_SERVICES")}
                        />
                        <InfoListItem
                            icon={<IconGlobe />}
                            text={l.t("MODAL_EXODUS_ADVICE_AID")}
                        />
                    </InfoList>

                    <p style={base.text}>
                        <span style={base.highlight}>{l.t("MODAL_EXODUS_SURVIVE")}</span>
                    </p>

                    <div style={base.buttonContainer}>
                        <button style={base.primaryButton} onClick={dismissExodusWarning}>
                            {l.t("MODAL_EXODUS_BUTTON")}
                        </button>
                    </div>
                </div>
            </div>
        </div>
    );
};

export const ExodusWarningModalDef = defineModal({
    id: "ExodusWarning",
    render: () => <ExodusWarningModalView />,
});
