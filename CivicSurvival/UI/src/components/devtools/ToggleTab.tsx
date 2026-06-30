/**
 * Toggle tab — domain-level + core system toggles for performance isolation.
 * Inventory and enabled state are backend-owned through debugToggleStates.
 */

import React, { useState, useCallback, useRef, useEffect, useMemo } from "react";
import { triggerCivic } from "@hooks/typedTrigger";
import { bindCivicValue } from "../../hooks/typedBinding.generated";
import { B } from "../../hooks/bindingNames.generated";
import { useSafeString } from "../../hooks/useSafeBinding";
import { useDebugToggleStates } from "../../hooks/state/useDebugToggleStates";
import { type DebugToggleEntry } from "../../types/domainDtos";
import type { TabProps } from "./debugPanelShared";
import { DOMAIN_DISPLAY, FALLBACK_DISPLAY } from "./ToggleTab.config";
import { ABTestControls, SharedOverheadABButton } from "./toggle-controls/ABTestControls";
import { MasterControls } from "./toggle-controls/BulkControls";
import { DomainToggle, SectionLabel, ThreatsHeader, ToggleGridButton } from "./toggle-controls/DomainControls";

const abTestStatus$ = bindCivicValue(B.Debug_ABTestStatus, "");
const abTestProgress$ = bindCivicValue(B.Debug_ABTestProgress, "");

interface ToggleView {
    key: string;
    name: string;
    count: number;
    color: string;
}

const display = (key: string) => DOMAIN_DISPLAY[key] ?? { name: key, color: FALLBACK_DISPLAY.color };

export const ToggleTab: React.FC<TabProps> = ({ styles: _styles, theme }) => {
    const toggleSnapshot = useDebugToggleStates();
    const abRunning = useSafeString(abTestStatus$, "");
    const abProgress = useSafeString(abTestProgress$, "");

    const [cancelArmed, setCancelArmed] = useState(false);
    const cancelTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
    const pendingStartRef = useRef<string | null>(null);

    useEffect(() => {
        if (!abRunning && cancelArmed) {
            setCancelArmed(false);
            if (cancelTimer.current) {
                clearTimeout(cancelTimer.current);
                cancelTimer.current = null;
            }
        }
    }, [abRunning, cancelArmed]);

    useEffect(() => {
        return () => {
            if (cancelTimer.current) clearTimeout(cancelTimer.current);
        };
    }, []);

    useEffect(() => {
        if (!pendingStartRef.current) return;
        if (abRunning === pendingStartRef.current) {
            pendingStartRef.current = null;
            return;
        }

        const timer = setTimeout(() => {
            pendingStartRef.current = null;
        }, 1500);
        return () => clearTimeout(timer);
    }, [abRunning]);

    const startAB = useCallback((domain: string) => {
        if (pendingStartRef.current === domain && !abRunning) return;

        if (abRunning === domain) {
            if (!cancelArmed) {
                setCancelArmed(true);
                if (cancelTimer.current) clearTimeout(cancelTimer.current);
                cancelTimer.current = setTimeout(() => setCancelArmed(false), 3000);
                return;
            }

            setCancelArmed(false);
            if (cancelTimer.current) {
                clearTimeout(cancelTimer.current);
                cancelTimer.current = null;
            }
            triggerCivic(B.DebugToggleSystem, domain, false);
            return;
        }
        pendingStartRef.current = domain;
        triggerCivic(B.DebugToggleSystem, domain, true);
    }, [abRunning, cancelArmed]);

    const entries = useMemo(
        () => toggleSnapshot.status === "ready" ? toggleSnapshot.data.entries : [],
        [toggleSnapshot]
    );

    const entryByKey = useMemo(() => new Map(entries.map(entry => [entry.key, entry])), [entries]);
    const threatEntries = useMemo(() => entries.filter(entry => entry.group === "threat"), [entries]);
    const domainEntries = useMemo(() => entries.filter(entry => entry.group === "domain"), [entries]);

    const viewByKey = useMemo(() => {
        const next = new Map<string, ToggleView>();
        entries.forEach(entry => {
            const meta = display(entry.key);
            next.set(entry.key, {
                key: entry.key,
                name: meta.name,
                color: meta.color,
                count: entry.systemCount,
            });
        });
        return next;
    }, [entries]);

    const view = (entry: DebugToggleEntry): ToggleView => viewByKey.get(entry.key) ?? {
        key: entry.key,
        name: display(entry.key).name,
        color: display(entry.key).color,
        count: entry.systemCount,
    };

    const subStates = useMemo(() => {
        const next: Record<string, boolean> = {};
        entries.filter(entry => entry.group === "sub").forEach(entry => {
            next[entry.key] = entry.enabled;
        });
        return next;
    }, [entries]);

    const subToggles = useCallback((parent: string) =>
        entries
            .filter(entry => entry.group === "sub" && entry.parent === parent)
            .map(entry => ({ key: entry.key, name: display(entry.key).name })),
    [entries]);

    const toggleKey = useCallback((key: string) => {
        const entry = entryByKey.get(key);
        const current = entry?.enabled ?? true;
        if (entry?.canDisable === false) return;
        triggerCivic(B.DebugToggleSystem, key, !current);
    }, [entryByKey]);

    const toggleAllThreats = useCallback((enabled: boolean) => {
        triggerCivic(B.DebugToggleSystem, "d:allThreats", enabled);
    }, []);

    const toggleAll = useCallback((enabled: boolean) => {
        triggerCivic(B.DebugToggleSystem, "d:allThreats", enabled);
        entries
            .filter(entry => entry.group === "domain")
            .filter(entry => entry.canDisable)
            .forEach(entry => triggerCivic(B.DebugToggleSystem, entry.key, enabled));
    }, [entries]);

    const allThreatsOn = threatEntries.every(entry => entry.enabled);
    const threatsOnCount = threatEntries.filter(entry => entry.enabled).length;
    const domainOnCount = entries.filter(entry =>
        (entry.group === "threat" || entry.group === "domain") && entry.enabled
    ).length;
    const totalCount = entries.filter(entry => entry.group !== "sub").length;
    const totalOn = domainOnCount;
    const allOn = totalCount > 0 && totalOn === totalCount;
    const allOff = totalOn === 0;
    const threatTotalCount = threatEntries.reduce((sum, entry) => sum + entry.systemCount, 0);

    if (toggleSnapshot.status !== "ready") return null;

    return (
        <>
            <MasterControls
                allOff={allOff}
                allOn={allOn}
                totalOn={totalOn}
                totalCount={totalCount}
                textMuted={theme.colors.textMuted}
                onToggleAll={toggleAll}
            />

            <ABTestControls
                abRunning={abRunning}
                abProgress={abProgress}
                cancelArmed={cancelArmed}
                onStartAB={startAB}
            />

            <SharedOverheadABButton
                abRunning={abRunning}
                abProgress={abProgress}
                cancelArmed={cancelArmed}
                onStartAB={startAB}
            />

            <div style={{ marginBottom: "6rem" }}>
                <ThreatsHeader
                    allThreatsOn={allThreatsOn}
                    totalCount={threatTotalCount}
                    onCount={threatsOnCount}
                    domainCount={threatEntries.length}
                    onToggle={() => toggleAllThreats(!allThreatsOn)}
                />
                {threatEntries.map(entry => (
                    <DomainToggle
                        key={entry.key}
                        domain={view(entry)}
                        isOn={entry.enabled}
                        canDisable={entry.canDisable}
                        lockedReasonId={entry.lockedReasonId}
                        indent
                        subToggles={subToggles(entry.key)}
                        subStates={subStates}
                        onToggleDomain={toggleKey}
                        onToggleSub={toggleKey}
                    />
                ))}
            </div>

            <div style={{ borderBottom: "1rem solid #333", margin: "2rem 0 6rem 0" }} />

            <SectionLabel text="DOMAINS" color={theme.colors.textMuted} />
            <div style={{ display: "flex", flexWrap: "wrap" as const, marginBottom: "6rem" }}>
                {domainEntries.map(entry =>
                    <div key={entry.key} style={{ padding: "1.5rem", flexBasis: "50%" }}>
                        <ToggleGridButton domain={view(entry)} isOn={entry.enabled} canDisable={entry.canDisable} lockedReasonId={entry.lockedReasonId} onClick={() => toggleKey(entry.key)} />
                    </div>
                )}
            </div>

        </>
    );
};
