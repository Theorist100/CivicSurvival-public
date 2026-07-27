import React from "react";
import { t } from "@coherent";
import { useAccents, useTheme } from "@themes";
import { useLocale } from "@locales";
import { MetricTile } from "@shared/ui";

interface PowerPlantSummaryProps {
    activeCount: number;
    currentOutput: number;
    totalCapacity: number;
    atRiskCount: number;
    destroyedCount: number;
    destroyedCapacityMW: number;
    headerStyle: React.CSSProperties;
    childStyle: React.CSSProperties;
}

interface CivilianSummaryProps {
    damagedCount: number;
    repairingCount: number;
    destroyedTotal: number;
    headerStyle: React.CSSProperties;
    childStyle: React.CSSProperties;
}

export const PowerPlantSummary: React.FC<PowerPlantSummaryProps> = ({
    activeCount,
    currentOutput,
    totalCapacity,
    atRiskCount,
    destroyedCount,
    destroyedCapacityMW,
    headerStyle,
    childStyle,
}) => {
    const theme = useTheme();
    const accents = useAccents();
    const l = useLocale();

    return (
        <div style={{ ...headerStyle, ...childStyle, display: "flex", alignItems: "center" }}>
            <MetricTile label={l.t("PLANTS_COUNT")} value={activeCount} color={theme.colors.textPrimary} />
            <MetricTile label={l.t("PLANTS_OUTPUT")} value={t`${currentOutput} MW`} color={currentOutput < totalCapacity ? accents.crisis.accent : accents.operations.accent} />
            <MetricTile label={l.t("PLANTS_CAPACITY")} value={t`${totalCapacity} MW`} color={theme.colors.textPrimary} />
            <MetricTile label={l.t("PLANTS_AT_RISK")} value={atRiskCount} color={atRiskCount > 0 ? accents.crisis.accent : theme.colors.success} />
            <MetricTile label={l.t("PLANTS_DESTROYED")} value={destroyedCount > 0 ? t`${destroyedCount} (${destroyedCapacityMW} MW)` : "0"} color={destroyedCount > 0 ? accents.crisis.accent : theme.colors.textMuted} />
        </div>
    );
};

export const CivilianSummary: React.FC<CivilianSummaryProps> = ({
    damagedCount,
    repairingCount,
    destroyedTotal,
    headerStyle,
    childStyle,
}) => {
    const theme = useTheme();
    const accents = useAccents();
    const l = useLocale();

    return (
        <div style={{ ...headerStyle, ...childStyle, display: "flex", alignItems: "center" }}>
            <MetricTile label={l.t("INFRA_CIV_DAMAGED")} value={damagedCount} color={damagedCount > 0 ? accents.crisis.accent : theme.colors.textMuted} />
            <MetricTile label={l.t("INFRA_CIV_REPAIRING")} value={repairingCount} color={repairingCount > 0 ? accents.operations.accent : theme.colors.textMuted} />
            <MetricTile label={l.t("INFRA_CIV_DESTROYED_TOTAL")} value={destroyedTotal} color={destroyedTotal > 0 ? accents.crisis.accent : theme.colors.textMuted} />
        </div>
    );
};
