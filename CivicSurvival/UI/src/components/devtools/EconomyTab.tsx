/**
 * Economy tab — phase progress, shadow economy, act controls.
 */

import React, { useMemo, useCallback } from "react";
import { triggerCivic } from "@hooks/typedTrigger";
import { Row } from "../coherent";
import { StatRow } from "@shared/ui";
import { B } from "../../hooks/bindingNames.generated";
import type { TabProps } from "./debugPanelShared";
import { DebugSectionTitle } from "./DevtoolsPrimitives";

const ACTS = ["PreWar", "Crisis", "Exodus", "Adaptation", "Routine"];

export const EconomyTab: React.FC<TabProps> = ({ debug, styles, theme, accents }) => {
    const raw = debug.raw;
    const currentActIndex = ACTS.indexOf(raw.currentAct);

    const handleSetAct = useCallback((actIndex: number) => {
        triggerCivic(B.Debug_SetAct, actIndex);
    }, []);

    const actButtonStyles = useMemo(() => {
        const base: React.CSSProperties = {
            padding: "6rem 10rem",
            fontSize: "10rem",
            border: `1rem solid ${theme.colors.borderLight}`,
            borderRadius: "3rem",
            cursor: "pointer",
            marginRight: "6rem",
            marginBottom: "6rem",
            pointerEvents: "auto" as const,
        };
        return {
            active: { ...base, backgroundColor: accents.resilience.accent, color: theme.colors.white } as React.CSSProperties,
            inactive: { ...base, backgroundColor: theme.colors.surface, color: theme.colors.textSecondary } as React.CSSProperties,
        };
    }, [accents.resilience.accent, theme.colors.surface, theme.colors.white, theme.colors.textSecondary, theme.colors.borderLight]);

    return (
        <>
            {/* Phase Progress */}
            <div style={styles.section}>
                <DebugSectionTitle>Phase Progress</DebugSectionTitle>
                <StatRow label="Current Act" value={raw.currentAct} color={debug.currentActColor} />
                <StatRow label="War Day" value={raw.warDay} color={theme.colors.textPrimary} />
                <StatRow label="Grid Warfare" value={debug.gridWarfareDisplay} color={debug.gridWarfareColor} />
            </div>

            {/* Shadow Economy */}
            <div style={styles.section}>
                <DebugSectionTitle>Shadow Economy</DebugSectionTitle>
                <StatRow label="Balance" value={debug.shadowBalanceDisplay} color={debug.colors.success} />
                <StatRow label="Daily Income" value={debug.shadowDailyIncomeDisplay} color={theme.colors.textPrimary} />
            </div>

            {/* Debug Controls */}
            <div style={styles.sectionNoBorder}>
                <DebugSectionTitle>Set Act (Debug)</DebugSectionTitle>
                <Row style={{
                    flexWrap: "wrap" as const,
                    marginTop: "4rem",
                }}>
                    {ACTS.map((act, index) => (
                        <button
                            key={act}
                            onClick={() => handleSetAct(index)}
                            style={index === currentActIndex ? actButtonStyles.active : actButtonStyles.inactive}
                        >
                            {act}
                        </button>
                    ))}
                </Row>
            </div>
        </>
    );
};
