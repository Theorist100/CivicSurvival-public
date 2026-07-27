export type ScenarioSubTab = "presets" | "military" | "grid" | "sweep" | "social" | "intel";

export const PRESETS: { id: number; label: string; color: string; desc: string }[] = [
    { id: 0, label: "1.2 Crisis", color: "#ff6600", desc: "Instant Crisis act" },
    { id: 1, label: "1.3 Exodus", color: "#ff4444", desc: "Crisis + Shock + Exodus" },
    { id: 2, label: "1.6 Defeat", color: "#cc0000", desc: "Pop collapse scenario" },
    { id: 3, label: "4.2 Collapse", color: "#ff0066", desc: "Grid collapse" },
    { id: 4, label: "4.1 NearCol", color: "#ff9900", desc: "Stress at 90%" },
    { id: 5, label: "7.1 InfoWar", color: "#9966ff", desc: "Low integrity + corruption" },
    { id: 6, label: "8.1 Exodus+", color: "#ff3366", desc: "Mass exodus surge" },
    { id: 7, label: "X.1 Attack", color: "#ff6600", desc: "20 drones + shock" },
    { id: 8, label: "X.2 Corrupt", color: "#cc6600", desc: "Corruption spiral" },
    { id: 9, label: "X.4 Cascade", color: "#ff3300", desc: "Wave + near-collapse" },
    { id: 10, label: "X.5 Resilient", color: "#33cc33", desc: "High integrity stable city" },
    { id: 99, label: "Reset All", color: "#4fc3f7", desc: "All singletons to defaults" },
];

export const SCENARIO_SUBTABS: { id: ScenarioSubTab; label: string }[] = [
    { id: "presets", label: "Presets" },
    { id: "military", label: "Military" },
    { id: "grid", label: "Grid" },
    { id: "sweep", label: "Sweep" },
    { id: "social", label: "Social" },
    { id: "intel", label: "Intel" },
];
