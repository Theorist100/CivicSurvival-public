/**
 * GridCriticalModal - Power grid is on the edge of collapse (Red zone).
 *
 * Shown once per stress episode by GridStressSystem (via the Core ModalCoordinator,
 * so it works regardless of the Narrative feature gate) when the grid first enters
 * the Red zone. Explains that collapse is imminent, what collapse would cost, and
 * what the player can do to avoid it. Superseded by GridCollapseModal if the grid
 * actually collapses.
 */

import React, { useMemo } from "react";
import { createBaseModalStyles, useModalPalette } from "../../themes";
import { InfoList, InfoListItem, defineModal } from "../shared/modal";
import { bindingDataOrDefault, usePowerGrid } from "@hooks/domain";
import { DEFAULT_POWER_GRID_DTO } from "../../types/domainDtos";
import { dismissGridCritical } from "../../hooks/bindings/scenarioDirectorBindings";
import { useLocale } from "../../locales";
import { IconAlert, IconLightning, IconWrench } from "../shared/common/Icons";

const GridCriticalView: React.FC = () => {
    const l = useLocale();
    const m = useModalPalette();
    const ACCENT = m.accents.crisis;

    const base = useMemo(() => createBaseModalStyles(m, {
        accentColor: ACCENT,
        overlayOpacity: 0.9,
        width: "420rem",    }), [m, ACCENT]);

    const grid = bindingDataOrDefault(usePowerGrid(), DEFAULT_POWER_GRID_DTO);
    const frequencyHz = (grid?.GridFrequency ?? 49).toFixed(1);

    return (
        <div style={base.overlay}>
            <div style={base.modal}>
                <div style={base.header}>
                    <span style={base.headerIcon}><IconAlert /></span>
                    <h2 style={base.title}>{l.t("MODAL_GRID_CRITICAL_TITLE")}</h2>
                </div>

                <div style={base.body}>
                    <p style={base.text}>{l.t("MODAL_GRID_CRITICAL_TEXT_1", frequencyHz)}</p>
                    <p style={base.text}>{l.t("MODAL_GRID_CRITICAL_TEXT_2")}</p>

                    <InfoList title={l.t("MODAL_GRID_CRITICAL_MEASURES")}>
                        <InfoListItem
                            icon={<IconLightning />}
                            title={l.t("MODAL_GRID_CRITICAL_REDUCE")}
                            desc={l.t("MODAL_GRID_CRITICAL_REDUCE_DESC")}
                        />
                        <InfoListItem
                            icon={<IconWrench />}
                            title={l.t("MODAL_GRID_CRITICAL_BUILD")}
                            desc={l.t("MODAL_GRID_CRITICAL_BUILD_DESC")}
                        />
                    </InfoList>

                    <div style={base.buttonContainer}>
                        <button style={base.primaryButton} onClick={dismissGridCritical}>
                            {l.t("MODAL_GRID_CRITICAL_BUTTON")}
                        </button>
                    </div>
                </div>
            </div>
        </div>
    );
};

export const GridCriticalModalDef = defineModal({
    id: "GridCritical",
    render: () => <GridCriticalView />,
});
