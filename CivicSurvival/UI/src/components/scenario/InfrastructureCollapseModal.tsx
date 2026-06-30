/**
 * InfrastructureCollapseModal - Systems at breaking point
 *
 * "Water pressure dropping. Sewage backing up."
 *
 * This modal appears when refugee influx overwhelms
 * the village's water and sewage infrastructure.
 *
 * Uses shared modal components for consistent styling.
 */

import React, { useMemo } from "react";
import { createBaseModalStyles, useModalPalette } from "../../themes";
import { InfoList, InfoListItem, Badge, defineModal } from "../shared/modal";
import { useRefugeeScenario } from "../../hooks/scenario/useRefugeeScenario";
import { dismissCollapseModal } from "../../hooks/bindings/refugeeBindings";
import { useLocale } from "../../locales";
import { IconAlert, IconResilience, IconLightning, IconWrench, IconGlobe } from "../shared/common/Icons";

const InfrastructureCollapseView: React.FC = () => {
    const l = useLocale();
    const m = useModalPalette();
    const ACCENT = m.accents.warning;

    // NOTE: CS2 Coherent UI uses rem where 1rem ≈ 1px
    const customStyles = useMemo(() => ({
        alertBanner: {
            backgroundColor: m.bgWarm,
            padding: "8rem",
            textAlign: "center" as const,
            borderBottom: `1rem solid ${m.accents.warning}`,
        } as React.CSSProperties,
        alertText: {
            color: m.warning.text,
            fontSize: "11rem",
            fontWeight: "bold" as const,
            letterSpacing: "3rem",
            textTransform: "uppercase" as const,
        } as React.CSSProperties,
    }), [m]);

    const base = useMemo(() => createBaseModalStyles(m, {
        accentColor: ACCENT,
        overlayOpacity: 0.85,
        width: "420rem",    }), [m, ACCENT]);

    const { refugeesReceived: refugees } = useRefugeeScenario();

    return (
        <div style={base.overlay}>
            <div style={base.modal}>
                <div style={customStyles.alertBanner}>
                    <span style={customStyles.alertText}>{l.t("MODAL_INFRA_BANNER")}</span>
                </div>

                <div style={base.header}>
                    <span style={base.headerIcon}><IconAlert /></span>
                    <h2 style={base.title}>{l.t("MODAL_INFRA_TITLE")}</h2>
                </div>

                <div style={base.body}>
                    {/* System status indicators */}
                    <InfoList>
                        <InfoListItem
                            icon={<IconResilience />}
                            text={l.t("MODAL_INFRA_WATER")}
                            badge={<Badge variant="danger">{l.t("MODAL_INFRA_CRITICAL")}</Badge>}
                        />
                        <InfoListItem
                            icon={<IconWrench />}
                            text={l.t("MODAL_INFRA_SEWAGE")}
                            badge={<Badge variant="warning">{l.t("MODAL_INFRA_STRAINED")}</Badge>}
                        />
                        <InfoListItem
                            icon={<IconLightning />}
                            text={l.t("MODAL_INFRA_POWER")}
                            badge={<Badge variant="warning">{l.t("MODAL_INFRA_LOAD")}</Badge>}
                        />
                    </InfoList>

                    <p style={base.text}>
                        {l.t("MODAL_INFRA_TEXT_1", refugees.toLocaleString("en-US"))}
                    </p>

                    <p style={base.text}>
                        {l.t("MODAL_INFRA_TEXT_2")}
                    </p>

                    <InfoList title={l.t("MODAL_INFRA_MEASURES")}>
                        <InfoListItem
                            icon={<IconResilience />}
                            title={l.t("MODAL_INFRA_RATIONING")}
                            desc={l.t("MODAL_INFRA_RATIONING_DESC")}
                        />
                        <InfoListItem
                            icon={<IconWrench />}
                            title={l.t("MODAL_INFRA_TRUCKS")}
                            desc={l.t("MODAL_INFRA_TRUCKS_DESC")}
                        />
                        <InfoListItem
                            icon={<IconGlobe />}
                            title={l.t("MODAL_INFRA_AID")}
                            desc={l.t("MODAL_INFRA_AID_DESC")}
                        />
                    </InfoList>

                    <div style={base.buttonContainer}>
                        <button style={base.primaryButton} onClick={dismissCollapseModal}>
                            {l.t("MODAL_INFRA_BUTTON")}
                        </button>
                    </div>
                </div>
            </div>
        </div>
    );
};

export const InfrastructureCollapseModalDef = defineModal({
    id: "Collapse",
    render: () => <InfrastructureCollapseView />,
});
