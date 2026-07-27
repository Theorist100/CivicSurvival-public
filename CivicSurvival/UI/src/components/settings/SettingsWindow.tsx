/**
 * SettingsWindow — the mod's settings, as a draggable window.
 *
 * Its own module-registry root, mounted beside the Dashboard (see index.tsx), exactly like
 * WarRoomOverlay, ProcurementChoicePanel and the toasts. That placement is the whole point:
 *
 *  - it is NOT inside the Dashboard, so the 50rem GlobalStatus bar (overflow: hidden) and
 *    the dashboard's own stacking context can't clip or bury it — no portal needed;
 *  - it IS inside the game UI layer, so leaving a city hides it along with everything else.
 *
 * The previous version was a portal into `div#civic-mod-root` on <body>. That escaped the
 * clipping but also escaped the layer, so nothing hid it when the city went away, and its
 * open flag lived in C# (SettingsUISystem.IsSettingsExpanded) where it survived the trip —
 * the window came back by itself on top of the main menu. Open state is now a plain JS
 * store shared with the gear in GlobalStatus (useSettingsWindow).
 */

import React, { memo, useCallback, useEffect, useMemo, useState } from "react";
import { Z_INDEX, useAccents, useTheme } from "../../themes";
import { useLocale, type TranslationKey } from "../../locales";
import { useDraggable } from "../../hooks/useDraggable";
import { useSettingsActions } from "@hooks/actions";
import { closeSettingsWindow, useSettingsWindowOpen } from "../../hooks/useSettingsWindow";
import { SettingsInterfaceContent } from "./SettingsInterfaceContent";
import { SettingsScenarioContent } from "./SettingsScenarioContent";
import { SettingsOnlineContent } from "./SettingsOnlineContent";
import { SettingsDebugContent } from "./SettingsDebugContent";

type SettingsTab = "interface" | "scenario" | "online" | "report";

const SETTINGS_TABS: Array<{ id: SettingsTab; labelKey: TranslationKey }> = [
    { id: "interface", labelKey: "UI_SETTINGS_TAB_INTERFACE" },
    { id: "scenario", labelKey: "UI_SETTINGS_TAB_SCENARIO" },
    { id: "online", labelKey: "UI_SETTINGS_TAB_ONLINE" },
    { id: "report", labelKey: "UI_SETTINGS_TAB_BUG" },
];

const SettingsWindowComponent: React.FC = () => {
    const open = useSettingsWindowOpen();
    const theme = useTheme();
    const accents = useAccents();
    const l = useLocale();
    const [activeTab, setActiveTab] = useState<SettingsTab>("interface");
    const { position, isDragging, handleMouseDown, dragRef, hasMoved } = useDraggable();
    const { setCrashDumpScan } = useSettingsActions();

    // ESC closes while open — listener only mounted for the open window. There is
    // deliberately no close-on-outside-click: a document-level listener cannot tell the
    // player dismissing the window from the player clicking the map, and it used to shut
    // this window on its own within a second of opening.
    useEffect(() => {
        if (!open) return;
        const onKeyDown = (e: KeyboardEvent) => {
            if (e.key === "Escape") closeSettingsWindow();
        };
        window.addEventListener("keydown", onKeyDown);
        return () => window.removeEventListener("keydown", onKeyDown);
    }, [open]);

    // The crash-dump list is a directory stat C# rebuilds on its 500ms tick. Tell it when
    // the REPORT tab is on screen so nothing pays for the scan otherwise. This is the only
    // fact about the window C# still needs; it replaced the old open/closed flag.
    useEffect(() => {
        const scanning = open && activeTab === "report";
        setCrashDumpScan(scanning);
        return () => setCrashDumpScan(false);
    }, [open, activeTab, setCrashDumpScan]);

    const overlayStyle = useMemo((): React.CSSProperties => ({
        position: "fixed" as const,
        top: 0,
        left: 0,
        right: 0,
        bottom: 0,
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        padding: "20rem 0",
        zIndex: Z_INDEX.modal,
        // allow-pointer-events-none: decoration only
        // The overlay only positions the window; it must not swallow input, or settings
        // would lock the whole screen like a blocking modal. The window sets its own auto.
        pointerEvents: "none" as const,
    }), []);

    const windowStyle: React.CSSProperties = {
        // Until dragged, the flex overlay above centres it — the same way every modal in
        // this project is placed. Hand-computed coordinates can't: the size is unknown on
        // the first frame and changes with the tab. Dragging switches to coordinates from
        // wherever layout had it (useDraggable.hasMoved), so nothing jumps.
        ...(hasMoved
            ? { position: "fixed" as const, left: position.x, top: position.y }
            : { position: "relative" as const }),
        width: "390rem",
        maxHeight: "100%",
        display: "flex",
        flexDirection: "column",
        minHeight: 0,
        background: theme.colors.paper,
        border: `2rem solid ${theme.colors.border}`,
        borderRadius: theme.layout.borderRadius,
        boxShadow: theme.effects.shadowLg,
        overflow: "hidden",
        pointerEvents: "auto",
    };

    const headerStyle: React.CSSProperties = {
        display: "flex",
        alignItems: "center",
        justifyContent: "space-between",
        padding: "12rem",
        borderBottom: `2rem solid ${theme.colors.border}`,
        cursor: isDragging ? "grabbing" : "grab",
        userSelect: "none",
        WebkitUserSelect: "none",
    };

    const titleStyle: React.CSSProperties = {
        fontSize: theme.typography.sizeSM,
        fontWeight: 700,
        color: theme.colors.textPrimary,
        textTransform: "uppercase",
        letterSpacing: "0.8rem",
    };

    const closeButtonStyle: React.CSSProperties = {
        width: "26rem",
        height: "26rem",
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        background: "transparent",
        border: `2rem solid ${theme.colors.border}`,
        borderRadius: theme.layout.borderRadius,
        color: theme.colors.textSecondary,
        cursor: "pointer",
        fontSize: theme.typography.sizeSM,
        lineHeight: 1,
    };

    const tabBarStyle: React.CSSProperties = {
        display: "flex",
        padding: "8rem 8rem 0",
        borderBottom: `2rem solid ${theme.colors.border}`,
    };

    const tabButtonStyle = (active: boolean): React.CSSProperties => ({
        flex: 1,
        padding: "8rem 4rem",
        background: active ? theme.colors.surface : "transparent",
        border: "none",
        borderBottom: active ? `3rem solid ${accents.operations.accent}` : "3rem solid transparent",
        color: active ? accents.operations.accent : theme.colors.textSecondary,
        cursor: "pointer",
        fontSize: theme.typography.sizeXS,
        fontWeight: active ? 700 : 600,
        textTransform: "uppercase",
    });

    const bodyStyle: React.CSSProperties = {
        display: "flex",
        flexDirection: "column",
        minHeight: 0,
        padding: "12rem",
        overflowY: "auto",
        overflowX: "hidden",
    };

    const renderActiveTab = useCallback(() => {
        switch (activeTab) {
            case "interface": return <SettingsInterfaceContent />;
            case "scenario": return <SettingsScenarioContent />;
            case "online": return <SettingsOnlineContent />;
            case "report": return <SettingsDebugContent />;
        }
    }, [activeTab]);

    if (!open) return null;

    return (
        <div style={overlayStyle}>
            <div
                ref={dragRef}
                style={windowStyle}
                data-civic-delegated-click
                onClick={(e) => e.stopPropagation()}
                onMouseDown={(e) => e.stopPropagation()}
            >
                <div style={headerStyle} onMouseDown={handleMouseDown}>
                    <div style={titleStyle}>{l.t("UI_TAB_SETTINGS")}</div>
                    <button type="button" style={closeButtonStyle} onClick={closeSettingsWindow}>x</button>
                </div>
                <div style={tabBarStyle}>
                    {SETTINGS_TABS.map((tab, index) => (
                        <button
                            key={tab.id}
                            type="button"
                            style={{
                                ...tabButtonStyle(activeTab === tab.id),
                                marginRight: index === SETTINGS_TABS.length - 1 ? 0 : "4rem",
                            }}
                            onClick={() => setActiveTab(tab.id)}
                        >
                            {l.t(tab.labelKey)}
                        </button>
                    ))}
                </div>
                <div style={bodyStyle}>{renderActiveTab()}</div>
            </div>
        </div>
    );
};

export const SettingsWindow = memo(SettingsWindowComponent);
SettingsWindow.displayName = "SettingsWindow";
