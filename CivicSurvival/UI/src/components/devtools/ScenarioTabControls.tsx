import React from "react";
import { SegmentedTabs } from "@shared/ui";
import { type Accents, type Theme } from "../../themes/types";
import { type createStyles } from "./debugPanelShared";
import { PRESETS, SCENARIO_SUBTABS, type ScenarioSubTab } from "./ScenarioTab.config";
import { DebugSectionTitle } from "./DevtoolsPrimitives";

type DebugStyles = ReturnType<typeof createStyles>;

interface ScenarioSubTabBarProps {
    activeTab: ScenarioSubTab;
    theme: Theme;
    accents: Accents;
    onTabChange: (tab: ScenarioSubTab) => void;
}

interface ScenarioPresetsPanelProps {
    styles: DebugStyles;
    onPreset: (id: number) => void;
}

export const ScenarioSubTabBar: React.FC<ScenarioSubTabBarProps> = ({
    activeTab,
    theme,
    accents,
    onTabChange,
}) => {
    return (
        <div style={{ display: "flex", marginBottom: "10rem", paddingBottom: "6rem", borderBottom: `1rem solid ${theme.colors.borderLight}` }}>
            <SegmentedTabs
                options={SCENARIO_SUBTABS.map((st) => ({ value: st.id, label: st.label }))}
                value={activeTab}
                onChange={onTabChange}
                color={accents.resilience.accent}
                style={{ width: "100%" }}
            />
        </div>
    );
};

export const ScenarioPresetsPanel: React.FC<ScenarioPresetsPanelProps> = ({ styles, onPreset }) => (
    <div style={styles.sectionNoBorder}>
        <DebugSectionTitle>Scenario Presets</DebugSectionTitle>
        <div style={{ display: "flex", flexDirection: "column", minHeight: "20rem" }}>
            {PRESETS.map((p) => (
                <button
                    key={p.id}
                    onClick={() => onPreset(p.id)}
                    style={{
                        display: "flex",
                        justifyContent: "space-between",
                        alignItems: "center",
                        padding: "8rem 12rem",
                        fontSize: "12rem",
                        backgroundColor: "transparent",
                        color: p.color,
                        border: `1rem solid ${p.color}`,
                        borderRadius: "4rem",
                        cursor: "pointer",
                        pointerEvents: "auto",
                        marginBottom: "4rem",
                    }}
                >
                    <span style={{ fontWeight: "bold" }}>{p.label}</span>
                    <span style={{ fontSize: "11rem", opacity: 0.7 }}>{p.desc}</span>
                </button>
            ))}
        </div>
    </div>
);
