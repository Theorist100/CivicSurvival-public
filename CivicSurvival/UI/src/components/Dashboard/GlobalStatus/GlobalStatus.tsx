/**
 * GlobalStatus - Fixed top bar showing critical metrics.
 * Always visible regardless of domain/view selection.
 */

import React, { memo, useMemo } from "react";
import { useTheme, useAccents } from "../../../themes";
import { createGlobalStatusStyles } from "./GlobalStatus.styles";
import {
    BalanceBadge,
    BatteryBadge,
    CrisisEconomyBadge,
    FrequencyGauge,
    ThreatBadge,
    ThresholdBadge,
} from "./StatusBadges";
import { SettingsButton } from "./SettingsButton";

const GlobalStatusComponent: React.FC = () => {
    const theme = useTheme();
    const accents = useAccents();
    const s = useMemo(() => createGlobalStatusStyles(theme, accents), [theme, accents]);

    return (
        <div style={s.container}>
            <div style={s.metricsGroupPrimary}>
                <FrequencyGauge styles={s} />
                <div style={s.separator} />
                <BalanceBadge styles={s} />
                <div style={s.separator} />
                <BatteryBadge styles={s} />
                <ThresholdBadge styles={s} />
            </div>

            <div style={s.metricsGroupSecondary}>
                <CrisisEconomyBadge styles={s} />
                <ThreatBadge styles={s} />
                <div style={s.separator} />
                <SettingsButton />
            </div>
        </div>
    );
};

export const GlobalStatus = memo(GlobalStatusComponent);
GlobalStatus.displayName = "GlobalStatus";
