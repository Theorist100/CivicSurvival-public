/**
 * ContentPanel - Main panel container
 * Contains ViewMenu and ContentZone
 */

import React, { memo, useMemo, useState, useCallback, useRef, useEffect } from "react";
import { useTheme, useAccents } from "../../../themes";
import { type DomainId } from "../DomainTabs/DomainTabs";
import {
    createContentPanelStyles,
    type ViewConfig,
    type ViewId,
    GRID_VIEWS,
    WAR_VIEWS,
    WAR_ROOM_VIEW,
    SHADOW_VIEWS,
    NEWS_VIEWS,
    OPS_VIEWS,
} from "./ContentPanel.styles";
import { getIconComponent } from "../../shared/common/Icons";
import { useBetaWave } from "../../../hooks/useBetaWave";
import {
    WarContent,
    WarRoomContent,
    DefenseContent,
    IntelContent,
    CognitiveWarfareContent,
    DonorsContent,
    ShadowSplitContent,
    InfrastructureContent,
    NewsMainContent,
    GridMainContent,
    GlobalOperationsContent,
    RoadmapContent,
} from "./views";
import { GridWarfarePanel } from "../../GridWarfare";
import { ArenaContent } from "../../arena";
import { ErrorBoundary } from "../../ErrorBoundary";
import { Profiled } from "../../../utils/uiProfiler";

// ============================================================================
// ViewMenu Component
// ============================================================================

interface ViewMenuProps {
    views: ReadonlyArray<ViewConfig>;
    activeView: ViewId;
    onViewChange: (viewId: ViewId) => void;
    accentColor: string;
    styles: ReturnType<typeof createContentPanelStyles>;
}

const ViewMenu: React.FC<ViewMenuProps> = memo(({
    views,
    activeView,
    onViewChange,
    accentColor,
    styles: s,
}) => {
    return (
        <div style={s.viewMenu}>
            {views.map((view) => {
                const IconComponent = getIconComponent(view.icon);
                return (
                    <div
                        key={view.id}
                        style={s.viewButton(activeView === view.id, accentColor)}
                        onClick={() => onViewChange(view.id)}
                        onKeyDown={(e) => {
                            if (e.key === "Enter" || e.key === " ") {
                                e.preventDefault();
                                onViewChange(view.id);
                            }
                        }}
                        role="button"
                        tabIndex={0}
                    >
                        {IconComponent && (
                            <span style={s.viewButtonIcon(activeView === view.id, accentColor)}>
                                <IconComponent />
                            </span>
                        )}
                        <span style={s.viewButtonLabel(activeView === view.id, accentColor)}>
                            {view.label}
                        </span>
                    </div>
                );
            })}
        </div>
    );
});
ViewMenu.displayName = "ViewMenu";

// ============================================================================
// ContentZone Component
// ============================================================================

interface ContentZoneProps {
    children: React.ReactNode;
    styles: ReturnType<typeof createContentPanelStyles>;
}

const ContentZone: React.FC<ContentZoneProps> = memo(({ children, styles: s }) => {
    return (
        <div style={s.contentZone}>
            {children}
        </div>
    );
});
ContentZone.displayName = "ContentZone";

// ============================================================================
// View component registry — maps domain+viewId to React component
// ============================================================================

interface DomainViewEntry {
    id: ViewId;
    Component: React.ComponentType;
}

// War Room is appended only when its gate is open (warRoomOpen), so a closed gate
// leaves it unmounted — the same single gate that hides the menu tab.
const buildDomainViewComponents = (warRoomOpen: boolean): Record<DomainId, DomainViewEntry[]> => ({
    grid: [
        { id: "main", Component: GridMainContent },
        { id: "infra", Component: InfrastructureContent },
    ],
    war: [
        { id: "radar", Component: WarContent },
        ...(warRoomOpen ? [{ id: "warroom" as const, Component: WarRoomContent }] : []),
        { id: "defense", Component: DefenseContent },
        { id: "psyops", Component: CognitiveWarfareContent },
        { id: "intel", Component: IntelContent },
        { id: "allies", Component: DonorsContent },
    ],
    shadow: [
        { id: "overview", Component: ShadowSplitContent },
    ],
    news: [
        { id: "herald", Component: NewsMainContent },
    ],
    ops: [
        { id: "roadmap", Component: RoadmapContent },
        { id: "arena", Component: ArenaContent },
        { id: "operations", Component: GlobalOperationsContent },
        { id: "counterattack", Component: GridWarfarePanel },
    ],
});

// ============================================================================
// DomainViewContainer — keeps views mounted within domain, toggles via CSS
// ============================================================================

interface DomainViewContainerProps {
    domain: DomainId;
    activeView: ViewId;
    warRoomOpen: boolean;
}

const DomainViewContainer: React.FC<DomainViewContainerProps> = memo(({ domain, activeView, warRoomOpen }) => {
    const entries = useMemo(() => buildDomainViewComponents(warRoomOpen)[domain] ?? [], [warRoomOpen, domain]);

    return (
        <>
            {entries.map((entry) => (
                <div
                    key={entry.id}
                    style={{
                        // cohtml only accepts flex/none; "block" is rejected with a
                        // console warning and the element stays at its default (flex)
                        display: activeView === entry.id ? "flex" : "none",
                        flexDirection: "column",
                        height: activeView === entry.id ? "100%" : "auto",
                    }}
                >
                    <ErrorBoundary name={`view:${domain}:${entry.id}`} resetKey={activeView === entry.id ? entry.id : null}>
                        <Profiled id={`V:${entry.id}`}>
                            <entry.Component />
                        </Profiled>
                    </ErrorBoundary>
                </div>
            ))}
        </>
    );
});
DomainViewContainer.displayName = "DomainViewContainer";

// ============================================================================
// Main ContentPanel Component
// ============================================================================

interface ContentPanelProps {
    domain: DomainId;
    onViewChange?: (viewId: ViewId) => void;
}

const ContentPanelComponent: React.FC<ContentPanelProps> = ({
    domain,
    onViewChange,
}) => {
    const theme = useTheme();
    const accents = useAccents();
    const s = useMemo(() => createContentPanelStyles(theme, accents), [theme, accents]);

    // War Room shares the GridWarfare beta-wave gate (wave 3). Closed gate =>
    // absent tab AND unmounted view (not a GlassCase preview). The manifest
    // arrives async, so this recomputes the war menu/mount when it opens.
    const warRoomOpen = !useBetaWave("GridWarfare").isLocked;

    // Get views for current domain
    const views = useMemo(() => {
        switch (domain) {
            case "grid": return GRID_VIEWS;
            case "news": return NEWS_VIEWS;
            case "war": return warRoomOpen ? [...WAR_VIEWS.slice(0, 1), WAR_ROOM_VIEW, ...WAR_VIEWS.slice(1)] : WAR_VIEWS;
            case "shadow": return SHADOW_VIEWS;
            case "ops": return OPS_VIEWS;
            default: return GRID_VIEWS;
        }
    }, [domain, warRoomOpen]);

    // Get accent color for current domain
    const accentColor = useMemo(() => {
        switch (domain) {
            case "grid": return accents.operations.accent;
            case "news": return accents.operations.accent;
            case "war": return accents.crisis.accent;
            case "shadow": return accents.schemes.accent;
            case "ops": return accents.resilience.accent;
            default: return accents.operations.accent;
        }
    }, [domain, accents]);

    // Derived state: viewOverride is set by user clicks, reset synchronously on domain change
    const [viewOverride, setViewOverride] = useState<ViewId | null>(null);
    const prevDomainRef = useRef(domain);
    if (prevDomainRef.current !== domain) {
        prevDomainRef.current = domain;
        setViewOverride(null);
    }
    const activeView = viewOverride ?? views[0]?.id ?? "main";

    // Use ref to avoid onViewChange in useEffect deps (prevents re-render loops if parent doesn't memoize)
    const onViewChangeRef = useRef(onViewChange);
    useEffect(() => {
        onViewChangeRef.current = onViewChange;
    }, [onViewChange]);

    useEffect(() => {
        onViewChangeRef.current?.(activeView);
    }, [activeView]);

    const handleViewChange = useCallback((viewId: ViewId) => {
        setViewOverride(viewId);
    }, []);

    return (
        <div style={s.container}>
            <ViewMenu
                views={views}
                activeView={activeView}
                onViewChange={handleViewChange}
                accentColor={accentColor}
                styles={s}
            />
            <ContentZone styles={s}>
                <DomainViewContainer domain={domain} activeView={activeView} warRoomOpen={warRoomOpen} />
            </ContentZone>
        </div>
    );
};

export const ContentPanel = memo(ContentPanelComponent);
ContentPanel.displayName = "ContentPanel";

export type { ViewId };
