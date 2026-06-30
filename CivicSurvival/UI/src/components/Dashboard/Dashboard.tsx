/**
 * Dashboard - Main UI container
 * Layout: GlobalStatus (top) + DomainTabs + ContentPanel
 */

import React, { memo, useMemo, useState, useCallback, useRef, useEffect } from "react";
import { useTheme, hexToRgba } from "../../themes";
import { createDashboardStyles } from "./Dashboard.styles";
import { GlobalStatus } from "./GlobalStatus/GlobalStatus";
import { DomainTabs, type DomainId } from "./DomainTabs/DomainTabs";
import { ContentPanel, type ViewId } from "./ContentPanel/ContentPanel";
import { StatRow } from "../shared/ui";
import { bindingDataOrDefault, useThreat, usePowerGrid } from "../../hooks/domain";
import { TutorialTabSignals } from "./TutorialTabSignals";
import { Profiled } from "../../utils/uiProfiler";
import { useLocale, type TranslationKey } from "../../locales";
import { DEFAULT_POWER_GRID_DTO, DEFAULT_THREAT_DTO, type WavePhase } from "../../types/domainDtos";

const MIN_VISIBLE_MARGIN = 60;
const PX_UNIT = "px";
const toPx = (value: number): string => `${value}${PX_UNIT}`;

// Pure clamp: takes the dashboard width as a value so the drag-mousemove handler
// never queries layout. getBoundingClientRect() from a mousemove handler races
// the cohtml layout thread — native null-deref crash class. The width is measured
// in the reclamp effect below (post-commit, tree settled) and cached.
function clampDashboardPosition(raw: { x: number; y: number }, width: number) {
    return {
        x: Math.max(MIN_VISIBLE_MARGIN - width, Math.min(raw.x, window.innerWidth - MIN_VISIBLE_MARGIN)),
        y: Math.max(0, Math.min(raw.y, window.innerHeight - MIN_VISIBLE_MARGIN)),
    };
}

// ============================================================================
// Minimized Widget Content
// ============================================================================

const MinimizedWidgetContent: React.FC = memo(() => {
    const theme = useTheme();
    const l = useLocale();
    const grid = bindingDataOrDefault(usePowerGrid(), DEFAULT_POWER_GRID_DTO);
    const threatData = bindingDataOrDefault(useThreat(), DEFAULT_THREAT_DTO);

    const frequency = grid.GridFrequency ?? 50;
    const stressZone = grid.StressZone ?? "normal";
    const phase = threatData.WavePhase || "calm";

    const freqColor = stressZone === "collapsed" ? "#ff4444"
        : stressZone === "red" ? "#ff6b35"
        : stressZone === "yellow" ? "#ffc107"
        : "#4caf50";

    const phaseColor = phase === "attack" ? "#ff4444"
        : phase === "alert" ? "#ffc107"
        : "#4caf50";

    const phaseLabelKeys: Record<WavePhase, TranslationKey> = {
        calm: "UI_STATUS_CALM",
        alert: "UI_STATUS_ALERT",
        attack: "UI_STATUS_ATTACK",
        recovery: "UI_STATUS_RECOVERY",
    };

    return (
        <StatRow
            label={l.t("UI_UNIT_HZ", frequency.toFixed(1))}
            value={l.t(phaseLabelKeys[phase])}
            style={{
            padding: "12rem 16rem",
            height: "100%",
            }}
            labelStyle={{
                color: freqColor,
                fontWeight: 700,
                fontSize: "18rem",
                fontFamily: theme.typography.fontFamilyMono,
            }}
            valueStyle={{
                color: phaseColor,
                fontWeight: 700,
                fontSize: "12rem",
                textTransform: "uppercase" as const,
                padding: "4rem 8rem",
                background: hexToRgba(phaseColor, 0.12),
                borderRadius: "4rem",
                fontFamily: theme.typography.fontFamily,
            }}
        />
    );
});
MinimizedWidgetContent.displayName = "MinimizedWidgetContent";

// ============================================================================
// Inner Dashboard Component (within ThemeProvider)
// ============================================================================

interface DashboardInnerProps {
    /** Initial domain to show */
    initialDomain?: DomainId;
}

const DashboardInner: React.FC<DashboardInnerProps> = memo(({
    initialDomain = "grid",
}) => {
    const theme = useTheme();
    const s = useMemo(() => createDashboardStyles(theme), [theme]);

    // Active domain state
    const [activeDomain, setActiveDomain] = useState<DomainId>(initialDomain);
    // Minimized state
    const [isMinimized, setIsMinimized] = useState(false);
    // View type for height: "radar" (770) | "war" (650) | "default" (700)
    const [viewType, setViewType] = useState<"radar" | "war" | "default">("default");
    const rootRef = useRef<HTMLDivElement>(null);
    const [defaultPos] = useState(() => ({ x: 10, y: 60 }));
    // Drag state (ref-based to avoid re-renders during drag)
    const positionRef = useRef({ ...defaultPos });
    const [isDragging, setIsDragging] = useState(false);
    const dragOffset = useRef({ x: 0, y: 0 });
    const hasDragged = useRef(false);
    // Dashboard width cache — written only in the reclamp effect (and its
    // window-resize re-measure); read by the drag handler instead of live layout.
    const widthRef = useRef(MIN_VISIBLE_MARGIN);

    const handleMinimize = useCallback(() => {
        setIsMinimized(true);
    }, []);

    const handleExpand = useCallback(() => {
        if (hasDragged.current) {
            hasDragged.current = false;
            return;
        }
        setIsMinimized(false);
    }, []);

    const handleDomainChange = useCallback((domain: DomainId) => {
        setActiveDomain(domain);
        // Reset to default when leaving WAR domain
        if (domain !== "war") {
            setViewType("default");
        }
    }, []);

    const handleViewChange = useCallback((viewId: ViewId) => {
        // WAR views: radar=770, strike/defense/intel=650, others=700
        if (viewId === "radar") {
            setViewType("radar");
        } else if (["counterattack", "defense", "intel"].includes(viewId)) {
            setViewType("war");
        } else {
            setViewType("default");
        }
    }, []);

    // Drag handlers
    const handleDragStart = useCallback((e: React.MouseEvent) => {
        if ((e.target as HTMLElement).closest('[data-no-drag]')) return;

        setIsDragging(true);
        hasDragged.current = false;
        dragOffset.current = {
            x: e.clientX - positionRef.current.x,
            y: e.clientY - positionRef.current.y,
        };
        e.preventDefault();
    }, []);

    useEffect(() => {
        if (!isDragging) return;

        const handleMouseMove = (e: MouseEvent) => {
            hasDragged.current = true;
            const rawX = e.clientX - dragOffset.current.x;
            const rawY = e.clientY - dragOffset.current.y;
            const { x, y } = clampDashboardPosition({ x: rawX, y: rawY }, widthRef.current);
            positionRef.current = { x, y };
            if (rootRef.current) {
                rootRef.current.style.left = toPx(x);
                rootRef.current.style.top = toPx(y);
            }
        };

        const handleMouseUp = () => {
            setIsDragging(false);
        };

        window.addEventListener("mousemove", handleMouseMove);
        window.addEventListener("mouseup", handleMouseUp);

        return () => {
            window.removeEventListener("mousemove", handleMouseMove);
            window.removeEventListener("mouseup", handleMouseUp);
        };
    }, [isDragging]);

    useEffect(() => {
        const reclamp = () => {
            // Measuring here is allowed: useEffect runs post-commit and the
            // "resize" listener fires outside pointer-event dispatch.
            const rect = rootRef.current?.getBoundingClientRect();
            if (rect && rect.width > 0) widthRef.current = rect.width;
            const next = clampDashboardPosition(positionRef.current, widthRef.current);
            positionRef.current = next;
            if (rootRef.current) {
                rootRef.current.style.left = toPx(next.x);
                rootRef.current.style.top = toPx(next.y);
            }
        };
        reclamp();
        window.addEventListener("resize", reclamp);
        return () => window.removeEventListener("resize", reclamp);
    }, [activeDomain, isMinimized, viewType]);

    const rootStyle: React.CSSProperties = {
        ...s.root(activeDomain, viewType),
        left: positionRef.current.x,
        top: positionRef.current.y,
        right: "auto",
        cursor: isDragging ? "grabbing" : "default",
    };

    // Minimized widget view
    if (isMinimized) {
        return (
            <div
                ref={rootRef}
                style={{ ...s.rootMinimized, left: positionRef.current.x, top: positionRef.current.y, right: "auto", cursor: isDragging ? "grabbing" : "pointer" }}
                onMouseDown={handleDragStart}
                onClick={handleExpand}
                onKeyDown={(e) => {
                    if (e.key === "Enter" || e.key === " ") {
                        e.preventDefault();
                        handleExpand();
                    }
                }}
                role="button"
                tabIndex={0}
            >
                <MinimizedWidgetContent />
            </div>
        );
    }

    return (
        <div ref={rootRef} style={rootStyle}>
            <TutorialTabSignals activeDomain={activeDomain} />
            {/* Drag handle - the header */}
            <div
                style={{ ...s.header, cursor: "grab", position: "relative" as const }}
                onMouseDown={handleDragStart}
            >
                {/* Minimize button */}
                <button type="button" style={s.minimizeButton} onClick={handleMinimize} data-no-drag>
                    ─
                </button>
                <Profiled id="GlobalStatus"><GlobalStatus /></Profiled>
                <DomainTabs
                    activeDomain={activeDomain}
                    onDomainChange={handleDomainChange}
                />
            </div>

            {/* Main content */}
            <div style={s.main}>
                <div style={s.sidebarWrapper}>
                    <Profiled id="ContentPanel">
                        <ContentPanel
                            domain={activeDomain}
                            onViewChange={handleViewChange}
                        />
                    </Profiled>
                </div>
            </div>
        </div>
    );
});
DashboardInner.displayName = "DashboardInner";

export const Dashboard: React.FC = () => <DashboardInner />;
