/**
 * BalanceDebugPanel - Debug overlay with tabs for metrics
 *
 * Tabs:
 * - Current: Live metrics (severity, power, penalties)
 * - History: Sparkline charts (24h history)
 * - City: Composition breakdown
 * - Economy: Phase progress, shadow economy (EconomyTab.tsx)
 * - Testing: Drone spawn/explode/camera controls (PerfTab.tsx)
 * - Scenario: State injection with sub-tabs (ScenarioTab.tsx)
 */

import React, { useMemo, useState, useCallback, useEffect, useRef } from "react";
import { SegmentedTabs } from "@shared/ui";
import { useDebugData } from "../../hooks/state/useDebugData";
import { useNumberBinding } from "../../hooks/useSafeBinding";
import { useTheme, useAccents } from "../../themes";
import { createStyles } from "./debugPanelShared";
import { EconomyTab } from "./EconomyTab";
import { PerfTab } from "./PerfTab";
import { ScenarioTab } from "./ScenarioTab";
import { ToggleTab } from "./ToggleTab";
import { Profiled } from "../../utils/uiProfiler";
import { debugSeverityScore$ } from "../../hooks/bindings/debugBindings";
import { CityTab, CurrentTab, HistoryTab, TABS, type TabId } from "./BalanceDebugTabs";

// ============================================================================
// MAIN COMPONENT - Draggable Panel
// ============================================================================

export const BalanceDebugPanel: React.FC = () => {
    const [activeTab, setActiveTab] = useState<TabId>("current");
    const [position, setPosition] = useState({ x: 10, y: 10 });
    const draggingRef = useRef(false);
    const dragOffsetRef = useRef({ x: 0, y: 0 });
    const cleanupDragListenersRef = useRef<(() => void) | null>(null);

    // Game-loaded gate. Paradox Mods loads every mod assembly during
    // GameManager.Initialize, well before the player picks a save, so the "Game"
    // mount renders this component during splash + main menu. C# UISystems push
    // values only when a city is actually live — until then useValue returns the
    // empty-object sentinel `{}` and useSafeNumber yields the fallback 0. Probe
    // one raw binding so we can return null before any tab content renders, which
    // keeps the dev panel out of menus exactly the way the gameplay UI is gated by
    // the "Game" mount in vanilla CS2.
    const severity = useNumberBinding(debugSeverityScore$, "debugSeverityScore");
    const debug = useDebugData();
    const theme = useTheme();
    const accents = useAccents();

    const styles = useMemo(() => createStyles(theme, accents), [theme, accents]);

    // Drag handlers
    const handleMouseDown = useCallback((e: React.MouseEvent) => {
        e.preventDefault();
        cleanupDragListenersRef.current?.();
        draggingRef.current = true;
        dragOffsetRef.current = { x: e.clientX - position.x, y: e.clientY - position.y };

        const handleWindowMouseMove = (event: MouseEvent) => {
            if (!draggingRef.current) return;
            setPosition({
                x: event.clientX - dragOffsetRef.current.x,
                y: event.clientY - dragOffsetRef.current.y,
            });
        };

        const cleanup = () => {
            draggingRef.current = false;
            window.removeEventListener("mousemove", handleWindowMouseMove);
            window.removeEventListener("mouseup", cleanup);
            cleanupDragListenersRef.current = null;
        };

        cleanupDragListenersRef.current = cleanup;
        window.addEventListener("mousemove", handleWindowMouseMove);
        window.addEventListener("mouseup", cleanup);
    }, [position.x, position.y]);

    useEffect(() => {
        return () => cleanupDragListenersRef.current?.();
    }, []);

    // Bindings come up as `{}` during pre-game; render nothing until C# pushes a real number.
    if (severity.status !== "ready") return null;

    const containerStyle: React.CSSProperties = {
        ...styles.container,
        left: position.x,
        top: position.y,
        pointerEvents: "auto" as const,
    };

    const headerStyle: React.CSSProperties = {
        ...styles.header,
        cursor: "move",
        WebkitUserSelect: "none" as const,
        padding: "4rem 0 8rem 0",
    };
    const tabBarStyle: React.CSSProperties = {
        minHeight: "30rem",
        marginBottom: "10rem",
        borderBottom: `1rem solid ${theme.colors.borderLight}`,
        paddingBottom: "8rem",
    };

    return (
        <div
            style={containerStyle}
        >
            <div style={headerStyle} onMouseDown={handleMouseDown}>
                Balance Debug
            </div>

            {/* Tab Bar */}
            <SegmentedTabs
                options={TABS.map((tab) => ({ value: tab.id, label: tab.label }))}
                value={activeTab}
                onChange={setActiveTab}
                color={accents.resilience.accent}
                style={tabBarStyle}
            />

            {/* Tab Content */}
            {activeTab === "current" && <Profiled id="D:current"><CurrentTab debug={debug} styles={styles} theme={theme} accents={accents} /></Profiled>}
            {activeTab === "history" && <Profiled id="D:history"><HistoryTab debug={debug} styles={styles} theme={theme} accents={accents} /></Profiled>}
            {activeTab === "city" && <Profiled id="D:city"><CityTab debug={debug} styles={styles} theme={theme} accents={accents} /></Profiled>}
            {activeTab === "economy" && <Profiled id="D:economy"><EconomyTab debug={debug} styles={styles} theme={theme} accents={accents} /></Profiled>}
            {activeTab === "perf" && <Profiled id="D:perf"><PerfTab debug={debug} styles={styles} theme={theme} accents={accents} /></Profiled>}
            {activeTab === "toggle" && <Profiled id="D:toggle"><ToggleTab debug={debug} styles={styles} theme={theme} accents={accents} /></Profiled>}
            {activeTab === "scenario" && <Profiled id="D:scenario"><ScenarioTab debug={debug} styles={styles} theme={theme} accents={accents} /></Profiled>}
        </div>
    );
};
