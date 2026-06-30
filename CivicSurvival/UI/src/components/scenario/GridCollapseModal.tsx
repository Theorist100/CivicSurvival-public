/**
 * GridCollapseModal - Power grid has collapsed.
 *
 * Shown once per collapse episode by GridStressSystem (via the Core
 * ModalCoordinator, so it works regardless of the Narrative feature gate).
 * Explains what happened, how long the emergency recovery takes, and what the
 * player can do. The persistent recovery countdown also lives in InfoSection.
 */

import React, { useMemo } from "react";
import { createBaseModalStyles, useModalPalette } from "../../themes";
import { InfoList, InfoListItem, defineModal } from "../shared/modal";
import { bindingDataOrDefault, usePowerGrid } from "@hooks/domain";
import { DEFAULT_POWER_GRID_DTO } from "../../types/domainDtos";
import { dismissGridCollapse } from "../../hooks/bindings/scenarioDirectorBindings";
import { useLocale } from "../../locales";
import { IconAlert, IconLightning, IconWrench } from "../shared/common/Icons";

const GridCollapseView: React.FC = () => {
    const l = useLocale();
    const m = useModalPalette();
    const ACCENT = m.accents.crisis;

    const base = useMemo(() => createBaseModalStyles(m, {
        accentColor: ACCENT,
        overlayOpacity: 0.9,
        width: "420rem",    }), [m, ACCENT]);

    const grid = bindingDataOrDefault(usePowerGrid(), DEFAULT_POWER_GRID_DTO);
    const recoveryHours = Math.max(0, Math.ceil(grid?.RecoveryHours ?? 0));

    return (
        <div style={base.overlay}>
            <div style={base.modal}>
                <div style={base.header}>
                    <span style={base.headerIcon}><IconAlert /></span>
                    <h2 style={base.title}>{l.t("MODAL_GRID_COLLAPSE_TITLE")}</h2>
                </div>

                <div style={base.body}>
                    <p style={base.text}>{l.t("MODAL_GRID_COLLAPSE_TEXT_1")}</p>
                    <p style={base.text}>{l.t("MODAL_GRID_COLLAPSE_RECOVERY", recoveryHours.toString())}</p>

                    <InfoList title={l.t("MODAL_GRID_COLLAPSE_MEASURES")}>
                        <InfoListItem
                            icon={<IconLightning />}
                            title={l.t("MODAL_GRID_COLLAPSE_REDUCE")}
                            desc={l.t("MODAL_GRID_COLLAPSE_REDUCE_DESC")}
                        />
                        <InfoListItem
                            icon={<IconWrench />}
                            title={l.t("MODAL_GRID_COLLAPSE_BUILD")}
                            desc={l.t("MODAL_GRID_COLLAPSE_BUILD_DESC")}
                        />
                    </InfoList>

                    <div style={base.buttonContainer}>
                        <button style={base.primaryButton} onClick={dismissGridCollapse}>
                            {l.t("MODAL_GRID_COLLAPSE_BUTTON")}
                        </button>
                    </div>
                </div>
            </div>
        </div>
    );
};

export const GridCollapseModalDef = defineModal({
    id: "GridCollapse",
    render: () => <GridCollapseView />,
});
