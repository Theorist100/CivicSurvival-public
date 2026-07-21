import React from "react";
import { ProgressBar, StatRow } from "@shared/ui";
import { type TabProps } from "./debugPanelShared";
import { Sparkline, PowerChart } from "./Sparkline";
import { DebugSectionTitle } from "./DevtoolsPrimitives";

export type TabId = "current" | "history" | "city" | "economy" | "perf" | "toggle" | "scenario";

interface TabDef {
    id: TabId;
    label: string;
}

export const TABS: TabDef[] = [
    { id: "current", label: "Current" },
    { id: "history", label: "History" },
    { id: "city", label: "City" },
    { id: "economy", label: "Economy" },
    { id: "perf", label: "Testing" },
    { id: "toggle", label: "Toggle" },
    { id: "scenario", label: "Scenario" },
];

export const CurrentTab: React.FC<TabProps> = ({ debug, styles, theme, accents: _accents }) => {
    const raw = debug.raw;

    return (
        <>
            <div style={styles.section}>
                <StatRow
                    label="SEVERITY"
                    value={debug.severityDisplay}
                    color={debug.severityColor}
                    valueStyle={{ fontSize: "20rem" }}
                />
                <ProgressBar
                    value={raw.severityScore}
                    color={debug.severityColor}
                    height="6rem"
                    style={{ marginTop: "4rem", background: theme.colors.borderLight }}
                />
            </div>

            <div style={styles.section}>
                <DebugSectionTitle>Power Grid</DebugSectionTitle>
                <StatRow label="Balance" value={debug.powerBalanceDisplay} color={debug.powerBalanceColor} />
                <StatRow label="Prod / Cons" value={debug.productionConsumptionDisplay} color={theme.colors.textMuted} />
                <StatRow label="Blackouted" value={debug.blackoutedMWDisplay} color={debug.blackoutedMWColor} />
            </div>

            <div style={styles.sectionNoBorder}>
                <DebugSectionTitle>Penalties</DebugSectionTitle>
                <StatRow label="Blackout %" value={debug.blackoutPercentDisplay} color={debug.blackoutPercentColor} />
                <StatRow label="Happiness" value={debug.happinessPenaltyDisplay} color={debug.happinessPenaltyColor} />
                <StatRow label="Commerce" value={debug.commercePenaltyDisplay} color={debug.commercePenaltyColor} />
                <StatRow label="Districts" value={debug.affectedDistrictsDisplay} color={debug.affectedDistrictsColor} />
            </div>
        </>
    );
};

export const HistoryTab: React.FC<TabProps> = ({ debug, styles, accents }) => (
    <>
        <PowerChart data={debug.raw.powerHistory} height={50} styles={styles} />

        <Sparkline
            data={debug.raw.severityHistory}
            color={accents.resilience.accent}
            label="Severity Score"
            height={40}
            styles={styles}
        />

        <Sparkline
            data={debug.raw.corruptionHistory}
            color={accents.crisis.accentBright}
            label="Corruption"
            height={40}
            styles={styles}
        />
    </>
);

export const CityTab: React.FC<TabProps> = ({ debug, styles, theme, accents }) => {
    const raw = debug.raw;

    return (
        <>
            <div style={styles.section}>
                <DebugSectionTitle>City Type: {raw.cityType}</DebugSectionTitle>
                <StatRow label="Residential" value={debug.cityComposition.residentialDisplay} color={accents.operations.accentBright} />
                <StatRow label="Commercial" value={debug.cityComposition.commercialDisplay} color={theme.colors.success} />
                <StatRow label="Industrial" value={debug.cityComposition.industrialDisplay} color={accents.resilience.accentBright} />
                <StatRow label="Office" value={debug.cityComposition.officeDisplay} color={accents.vip.accentBright} />
            </div>

            <div style={styles.sectionNoBorder}>
                <DebugSectionTitle>Blackout Impact</DebugSectionTitle>
                <StatRow label="Buildings" value={raw.buildingsInBlackout} color={debug.buildingsInBlackoutColor} />
                <StatRow label="Duration" value={debug.blackoutDurationDisplay} color={debug.blackoutDurationColor} />
            </div>
        </>
    );
};
