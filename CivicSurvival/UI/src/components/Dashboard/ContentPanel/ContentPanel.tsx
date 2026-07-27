/**
 * ContentPanel - Main panel container
 * Contains ViewMenu and ContentZone
 */

import React, { memo, useMemo, useState, useCallback, useRef, useEffect } from "react";
import { useTheme, useAccents } from "../../../themes";
import { useLocale } from "../../../locales";
import { type DomainId } from "../DomainTabs/DomainTabs";
import { notifyOpenRadarView, notifyOpenDefenseBuild } from "../../../hooks/bindings/shockActBindings";
import {
    createContentPanelStyles,
    type ViewConfig,
    type ViewId,
    GRID_VIEWS,
    WAR_VIEWS,
    WARROOM_VIEWS,
    SHADOW_VIEWS,
    NEWS_VIEWS,
    ARENA_VIEWS,
} from "./ContentPanel.styles";
import { getIconComponent } from "../../shared/common/Icons";
import {
    WarContent,
    DefenseContent,
    IntelContent,
    CognitiveWarfareContent,
    IpsoContent,
    DonorsContent,
    ShadowSplitContent,
    InfrastructureContent,
    NewsMainContent,
    GridMainContent,
    GlobalOperationsContent,
    WarRoomSummaryContent,
} from "./views";
import { ArenaContent } from "../../arena";
import { ErrorBoundary } from "../../ErrorBoundary";
import { Profiled } from "../../../utils/uiProfiler";
import { consumeDashboardNav, useDashboardNavRequest } from "../../../hooks/useDashboardNav";
import { scLog } from "../../../utils/logging";

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
    const l = useLocale();
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
                            {l.t(view.labelKey)}
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

const buildDomainViewComponents = (): Record<DomainId, DomainViewEntry[]> => ({
    grid: [
        { id: "main", Component: GridMainContent },
        { id: "infra", Component: InfrastructureContent },
    ],
    war: [
        { id: "radar", Component: WarContent },
        { id: "defense", Component: DefenseContent },
        { id: "psyops", Component: CognitiveWarfareContent },
        { id: "ipso", Component: IpsoContent },
        { id: "intel", Component: IntelContent },
        { id: "allies", Component: DonorsContent },
    ],
    warroom: [
        { id: "command", Component: WarRoomSummaryContent },
    ],
    shadow: [
        { id: "overview", Component: ShadowSplitContent },
    ],
    news: [
        { id: "herald", Component: NewsMainContent },
    ],
    arena: [
        { id: "arena", Component: ArenaContent },
        // Global Operations teaser parked under ARENA until the feature ships
        // (the former GLOBAL OPS top-level domain was retired).
        { id: "globalops", Component: GlobalOperationsContent },
    ],
});

// ============================================================================
// DomainViewContainer — keeps views mounted within domain, toggles via CSS
// ============================================================================

interface DomainViewContainerProps {
    domain: DomainId;
    activeView: ViewId;
}

const DomainViewContainer: React.FC<DomainViewContainerProps> = memo(({ domain, activeView }) => {
    const entries = useMemo(() => buildDomainViewComponents()[domain] ?? [], [domain]);

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

    // Get views for current domain
    const views = useMemo(() => {
        switch (domain) {
            case "grid": return GRID_VIEWS;
            case "news": return NEWS_VIEWS;
            case "war": return WAR_VIEWS;
            case "warroom": return WARROOM_VIEWS;
            case "shadow": return SHADOW_VIEWS;
            case "arena": return ARENA_VIEWS;
            default: return GRID_VIEWS;
        }
    }, [domain]);

    // Get accent color for current domain
    const accentColor = useMemo(() => {
        switch (domain) {
            case "grid": return accents.operations.accent;
            case "news": return accents.operations.accent;
            case "war": return accents.crisis.accent;
            case "warroom": return accents.crisis.accent;
            case "shadow": return accents.schemes.accent;
            case "arena": return accents.resilience.accent;
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

    // [PanelDiag] crash-triage trail (see Dashboard.tsx counterpart): which view's
    // subtree — and therefore which SVG surfaces — was mounted when a native crash cut
    // the log. Domain views stay mounted display:none-hidden (DomainViewContainer), so
    // the active view marks the one subtree cohtml actually lays out.
    useEffect(() => {
        scLog(`[PanelDiag] view domain=${domain} view=${activeView}`);
    }, [domain, activeView]);

    // Air-defense funnel analytics: within the WAR tab, signal the RADAR (coverage) and DEFENSE
    // (build) sub-view opens. CrisisTutorialSystem dedups to the first open in Crisis, so firing
    // on every view switch is safe — mirrors the grid/shadow tab-open signals.
    useEffect(() => {
        if (domain !== "war") return;
        if (activeView === "radar") notifyOpenRadarView();
        else if (activeView === "defense") notifyOpenDefenseBuild();
    }, [domain, activeView]);

    // External navigation tail: the Dashboard already applied the request's domain; once our
    // domain prop matches, apply the view and consume the request (see useDashboardNav).
    const navRequest = useDashboardNavRequest();
    useEffect(() => {
        if (!navRequest || navRequest.consumed || navRequest.domain !== domain) return;
        consumeDashboardNav();
        if (views.some((v) => v.id === navRequest.view)) {
            setViewOverride(navRequest.view);
        }
    }, [navRequest, domain, views]);

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
                <DomainViewContainer domain={domain} activeView={activeView} />
            </ContentZone>
        </div>
    );
};

export const ContentPanel = memo(ContentPanelComponent);
ContentPanel.displayName = "ContentPanel";

export type { ViewId };
