/**
 * MapView — the "where" lens (CROWD ⇄ MAP). A simplified inset of the district
 * coverage picture; the full geo/threat map lands on the RADAR tab later
 * (RadarTelecomLayer, Phase 07). District integrity + the LIVE per-district
 * ISOLATE toggle are reused verbatim from DistrictRow (toggleDistrictInternet),
 * so the demoted district switch keeps working. Broadcast coverage masts +
 * rings are sample data (awaiting Phase 07 telecom read).
 */

import React, { memo } from "react";
import { Column, Row } from "../../../../coherent";
import { useTheme, useAccents, hexToRgba } from "../../../../../themes";
import { useLocale } from "../../../../../locales";
import { cognitiveDistricts$, isCognitiveDistrictDto } from "@hooks/bindings/coreBindings";
import { bindingDataOrDefault, useCognitive } from "@hooks/domain";
import { useSafeJsonArray } from "@hooks/useSafeBinding";
import { DEFAULT_COGNITIVE_DTO } from "../../../../../types/domainDtos";
import { IconSatellite } from "../../../../shared/common/Icons";
import { DistrictRow } from "../ops/DistrictRow";
import { createBoardStyles } from "./opinionStyles";
import { Sep } from "./Sep";

interface MapViewProps {
    disabled: boolean;
}

// AWAITING BACKEND (Phase 07): mast reach is sample data.
const SAMPLE_MASTS = [
    { id: "tower", labelKey: "UI_OB_MAST_TOWER" as const, reach: 62 },
    { id: "relay", labelKey: "UI_OB_MAST_RELAY" as const, reach: 40 },
];

export const MapView = memo(({ disabled }: MapViewProps) => {
    const theme = useTheme();
    const accents = useAccents();
    const l = useLocale();
    const b = createBoardStyles(theme, accents);

    const rawDistricts = useSafeJsonArray(cognitiveDistricts$, [], "cognitiveDistricts");
    const districts = Array.isArray(rawDistricts) ? rawDistricts.filter(isCognitiveDistrictDto) : [];
    const cw = bindingDataOrDefault(useCognitive(), DEFAULT_COGNITIVE_DTO);

    return (
        <Column gap={theme.spacing.sm} style={{ width: "100%", minHeight: "200rem" }}>
            <div style={{ ...b.region, padding: theme.spacing.sm }}>
                <Row align="center" justify="space-between" style={{ marginBottom: theme.spacing.xs }}>
                    <span style={b.eyebrow}>{l.t("UI_OB_COVERAGE")}</span>
                    <span style={b.sampleNote}>{l.t("UI_OB_AWAITING_DATA")}</span>
                </Row>
                <Row gap={theme.spacing.md} wrap="wrap">
                    {SAMPLE_MASTS.map((mast) => (
                        <Row key={mast.id} align="center">
                            <span style={{ color: theme.colors.success, marginRight: "5rem", display: "flex" }}>
                                <IconSatellite />
                            </span>
                            <span style={{
                                fontSize: theme.typography.sizeXS, fontFamily: theme.typography.fontFamilyMono,
                                color: theme.colors.textSecondary, textTransform: "uppercase", letterSpacing: "0.4rem",
                            }}>
                                {l.t(mast.labelKey)}<Sep />{mast.reach}%
                            </span>
                        </Row>
                    ))}
                    <Row align="center">
                        <div style={{
                            width: "10rem", height: "10rem", borderRadius: "5rem",
                            border: `2rem solid ${theme.colors.success}`,
                            backgroundColor: hexToRgba(theme.colors.success, 0.18), marginRight: "5rem",
                        }} />
                        <span style={{ fontSize: theme.typography.sizeXS, color: theme.colors.textMuted }}>
                            {l.t("UI_OB_COVERAGE_RING")}
                        </span>
                    </Row>
                </Row>
            </div>

            <div style={b.region}>
                <div style={b.headerStrip}>
                    <span style={b.eyebrow}>{`${l.t("UI_CW_DISTRICT_INTEGRITY")} (${districts.length})`}</span>
                </div>
                <div style={{ maxHeight: "240rem", overflow: "auto" }}>
                    {districts.length === 0 ? (
                        <div style={{ padding: theme.spacing.md, textAlign: "center", color: theme.colors.textMuted, fontSize: theme.typography.sizeXS }}>
                            {l.t("UI_CW_NO_DISTRICTS")}
                        </div>
                    ) : (
                        districts.map((district) => (
                            <DistrictRow
                                key={district.DistrictIndex}
                                district={district}
                                globalMode={cw.InternetMode}
                                districtIndex={district.DistrictIndex}
                                disabled={disabled}
                            />
                        ))
                    )}
                </div>
            </div>
        </Column>
    );
});

MapView.displayName = "MapView";
