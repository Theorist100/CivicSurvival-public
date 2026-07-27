/**
 * ProcurementSection - Active procurement contracts display
 */

import React, { memo, useMemo } from "react";
import { Column, Row } from "../../coherent";
import { useTheme, useAccents, formatCostArg, hexToRgba } from "../../../themes";
import { bindingDataOrDefault, useMaintenance } from "@hooks/domain";
import { DEFAULT_MAINTENANCE_DTO } from "../../../types/domainDtos";
import { parseActiveContracts } from "../../../hooks/bindings/procurementBindings";
import { GlassCase, StatRow } from "../../shared/ui";
import { createSectionStyles } from "./SectionStyles";
import { useLocale } from "../../../locales";

export const ProcurementSection = memo(() => {
    const theme = useTheme();
    const accents = useAccents();
    const l = useLocale();
    const s = useMemo(() => createSectionStyles(theme, accents), [theme, accents]);

    // Procurement contracts from domain DTO
    const maintenanceState = useMaintenance();
    const maintenance = bindingDataOrDefault(maintenanceState, DEFAULT_MAINTENANCE_DTO);
    const shadyContractCount = maintenance?.ShadyContractCount ?? 0;
    const activeContracts = useMemo(
        () => parseActiveContracts(maintenance?.ActiveContractsJson),
        [maintenance?.ActiveContractsJson]
    );

    return (
        <GlassCase
            feature="Corruption"
            name="Shadow Procurement"
            description="Sign maintenance and supply contracts with official or shady vendors. Shady vendors pay kickbacks but lower reliability — increasing disaster risk per contract."
        >
            <div style={s.section}>
            <div style={s.sectionTitle}>{l.t("UI_PROCUREMENT_TITLE")}</div>
            <StatRow
                label={l.t("UI_PROCUREMENT_CONTRACTS")}
                value={l.t("UI_PROCUREMENT_SHADY_COUNT", activeContracts.length, shadyContractCount)}
                color={shadyContractCount > 0 ? accents.schemes.accent : theme.colors.textMuted}
                emphasis="title"
            />
            {/* No inner maxHeight/overflow: nested scroll containers steal the wheel
                from the column scroll — the list grows and the right column is the
                single scroll surface. */}
            <Column style={{
                marginTop: "6rem",
            }}>
                {activeContracts.length === 0 ? (
                    <div style={{
                        padding: "8rem 6rem",
                        color: theme.colors.textMuted,
                        fontSize: "11rem",
                        textAlign: "center" as const,
                    }}>
                        {l.t("UI_PROCUREMENT_NO_CONTRACTS")}
                    </div>
                ) : (
                    activeContracts.map((contract, idx) => (
                        <div
                            key={contract.entityIndex}
                            style={{
                                padding: "4rem 6rem",
                                // Same token as the border/text below — the old hardcoded red
                                // rgba under the schemes-green border rendered as a muddy
                                // brown-black slab on the dark panel.
                                backgroundColor: contract.isShady
                                    ? hexToRgba(accents.schemes.accent, 0.08)
                                    : hexToRgba(theme.colors.success, 0.05),
                                borderRadius: "3rem",
                                borderLeft: `2rem solid ${contract.isShady ? accents.schemes.accent : theme.colors.success}`,
                                fontSize: "12rem",
                                marginBottom: idx < activeContracts.length - 1 ? "4rem" : "0rem",
                            }}
                        >
                            <Row justify="space-between">
                                <span style={{ fontWeight: 600 }}>{contract.buildingName}</span>
                                <span style={{ color: theme.colors.textMuted }}>{l.t("UI_PROCUREMENT_DAYS", contract.daysRemaining)}</span>
                            </Row>
                            <div style={{
                                color: contract.isShady ? accents.schemes.accent : theme.colors.success,
                                marginTop: "2rem",
                            }}>
                                {`${contract.contractType} - ${Math.round(contract.quality * 100)}%${contract.isShady && contract.kickbackAmount > 0 ? ` ${l.t("UI_PROCUREMENT_KICKBACK", formatCostArg(contract.kickbackAmount))}` : ""}`}
                            </div>
                        </div>
                    ))
                )}
            </Column>
        </div>
        </GlassCase>
    );
});
ProcurementSection.displayName = "ProcurementSection";
