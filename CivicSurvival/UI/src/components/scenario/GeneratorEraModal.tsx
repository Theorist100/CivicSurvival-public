/**
 * GeneratorEraModal - Generators received from donors
 *
 * Appears when DonorEvent(GeneratorsReceived) fires.
 * Explains backup power system.
 */

import React, { useMemo } from "react";
import { createBaseModalStyles, useModalPalette } from "../../themes";
import { InfoList, InfoListItem, defineModal } from "../shared/modal";
import { dismissGeneratorEra } from "../../hooks/bindings/milestoneTutorialBindings";
import { useLocale } from "../../locales";
import { IconLightning, IconHome, IconGear } from "../shared/common/Icons";

const GeneratorEraModalView: React.FC = () => {
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
                    <span style={base.headerIcon}><IconLightning /></span>
                    <h2 style={base.title}>{l.t("MODAL_GENERATOR_TITLE")}</h2>
                </div>

                <div style={base.body}>
                    <p style={base.text}>
                        {l.t("MODAL_GENERATOR_TEXT_1")}
                    </p>

                    <p style={base.text}>
                        {l.t("MODAL_GENERATOR_TEXT_2")}
                    </p>

                    <InfoList title={l.t("MODAL_GENERATOR_TIPS_TITLE")}>
                        <InfoListItem
                            icon={<IconHome />}
                            text={l.t("MODAL_GENERATOR_TIP_PRIORITY")}
                        />
                        <InfoListItem
                            icon={<IconGear />}
                            text={l.t("MODAL_GENERATOR_TIP_POLICY")}
                        />
                    </InfoList>

                    <div style={base.buttonContainer}>
                        <button style={base.primaryButton} onClick={dismissGeneratorEra}>
                            {l.t("MODAL_GENERATOR_BUTTON")}
                        </button>
                    </div>
                </div>
            </div>
        </div>
    );
};

export const GeneratorEraModalDef = defineModal({
    id: "GeneratorEra",
    render: () => <GeneratorEraModalView />,
});
