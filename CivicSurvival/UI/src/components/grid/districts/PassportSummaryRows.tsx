import React from "react";
import { t } from "../../coherent";
import { useTheme, useAccents } from "../../../themes";
import { type DistrictViewModel } from "../../../types/models/PowerTypes";
import { ModeDropdown, type DistrictMode } from "../../shared/common/ModeDropdown";
import { MetricTile } from "../../shared/ui";
import { useLocale } from "../../../locales";

interface CityStats {
    totalMW: number;
    districtCount: number;
    blackoutPercent: number;
}

interface CitySummaryRowProps {
    stats: CityStats;
}

interface DistrictSummaryRowProps {
    district: DistrictViewModel;
    currentMode: DistrictMode;
    onModeChange: (mode: DistrictMode) => void;
}

const summaryRowStyle = (theme: ReturnType<typeof useTheme>): React.CSSProperties => ({
    display: "flex",
    alignItems: "center",
    justifyContent: "space-around",
    padding: theme.spacing.md,
    borderBottom: `2rem solid ${theme.colors.border}`,
    backgroundColor: theme.colors.borderLight,
});

export const CitySummaryRow: React.FC<CitySummaryRowProps> = ({ stats }) => {
    const theme = useTheme();
    const accents = useAccents();
    const l = useLocale();

    return (
        <div style={summaryRowStyle(theme)}>
            <MetricTile label={l.t("UI_CP_TOTAL_LOAD")} value={t`${stats.totalMW} MW`} />
            <MetricTile label={l.t("UI_CP_DISTRICTS")} value={stats.districtCount} />
            <MetricTile
                label={l.t("UI_CP_BLACKOUT")}
                value={t`${stats.blackoutPercent}%`}
                color={stats.blackoutPercent > 0 ? accents.crisis.accent : theme.colors.success}
            />
        </div>
    );
};

export const DistrictSummaryRow: React.FC<DistrictSummaryRowProps> = ({
    district,
    currentMode,
    onModeChange,
}) => {
    const theme = useTheme();
    const l = useLocale();

    return (
        <div style={summaryRowStyle(theme)}>
            <MetricTile label={l.t("UI_DP_LOAD")} value={t`${district.totalMW} MW`} />
            <div style={{ flex: 1, textAlign: "center" }}>
                <div style={{ fontSize: "10rem", color: theme.colors.textMuted, textTransform: "uppercase", marginBottom: "4rem" }}>
                    {l.t("UI_DP_MODE")}
                </div>
                <ModeDropdown mode={currentMode} onChange={onModeChange} />
            </div>
            <MetricTile label={l.t("UI_DP_SCHEDULE")} value={district.scheduleName} />
        </div>
    );
};
