/**
 * AllocationSection — the broadcast-allocation body of the Center's manage
 * drill-down (ManageCenterSubView), which owns the back header, the status
 * chip and the reach/telecom read; this section renders only the split itself.
 *
 * The Propaganda Center holds a FIXED household coverage ceiling (grows by upgrade, not by city
 * size), so a bigger city strains it and the player must choose which stratum to hold. This section
 * is that choice: three normalized weights (Poor/Middle/Wealthy) split the ceiling, each capped by
 * the stratum's telecom-signal ceiling. The measurable core is per-stratum "enemy reach vs covered"
 * with a SAFE / GAP verdict. Pause-safe — the commit is a sync host-direct command (no ECB/request),
 * so dragging a slider works while the simulation is paused.
 */

import React, { memo, useCallback, useMemo, useRef, useState } from "react";
import { Column, Row } from "../../../../coherent";
import { useTheme, useAccents, hexToRgba } from "../../../../../themes";
import { useLocale, type TranslationKey } from "../../../../../locales";
import { type useCognitiveActions } from "@hooks/actions";
import { bindingDataOrDefault, useCognitive } from "@hooks/domain";
import { useSafeJsonArray } from "@hooks/useSafeBinding";
import { cognitiveStrata$, isCognitiveStratumDto } from "@hooks/bindings/coreBindings";
import { DEFAULT_COGNITIVE_DTO } from "../../../../../types/domainDtos";
import { type CognitiveStratumEntry } from "../../../../../types/domainDtos.generated";
import { STRATUM_NAME_KEY, formatHouseholds } from "./opinionData";
import { createBoardStyles } from "./opinionStyles";
import { AllocSlider } from "./AllocSlider";
import { Sep } from "./Sep";

interface AllocationSectionProps {
    actions: ReturnType<typeof useCognitiveActions>;
}

type StratumKey = "poor" | "middle" | "wealthy";
interface Weights { poor: number; middle: number; wealthy: number; }

// Fixed row order Poor → Middle → Wealthy, keyed to the C# SocialStratum int (1/2/3).
const ROWS: readonly { key: StratumKey; stratumInt: number }[] = [
    { key: "poor", stratumInt: 1 },
    { key: "middle", stratumInt: 2 },
    { key: "wealthy", stratumInt: 3 },
];

const EPS = 1e-3;
const clamp01 = (v: number): number => Math.max(0, Math.min(1, v));
const pct = (v: number): number => Math.round(clamp01(v) * 100);

/** Set one weight and re-normalize the other two proportionally so the three keep summing to 1. */
function reweight(w: Weights, key: StratumKey, value: number): Weights {
    const v = clamp01(value);
    const rest = 1 - v;
    const others = (["poor", "middle", "wealthy"] as StratumKey[]).filter((k) => k !== key);
    const otherSum = others.reduce((s, k) => s + w[k], 0);
    const next: Weights = { ...w, [key]: v };
    if (otherSum <= EPS) {
        for (const k of others) next[k] = rest / 2;
    } else {
        for (const k of others) next[k] = (w[k] / otherSum) * rest;
    }
    return next;
}

/** Normalize a raw triple to sum 1 (even split when degenerate). */
function normalize(poor: number, middle: number, wealthy: number): Weights {
    const p = Math.max(0, poor), m = Math.max(0, middle), wy = Math.max(0, wealthy);
    const sum = p + m + wy;
    if (sum <= EPS) return { poor: 1 / 3, middle: 1 / 3, wealthy: 1 / 3 };
    return { poor: p / sum, middle: m / sum, wealthy: wy / sum };
}

export const AllocationSection = memo(({ actions }: AllocationSectionProps) => {
    const theme = useTheme();
    const accents = useAccents();
    const l = useLocale();
    const control = accents.crisis.accent;
    const mono = theme.typography.fontFamilyMono;

    const cw = bindingDataOrDefault(useCognitive(), DEFAULT_COGNITIVE_DTO);
    const strataRaw = useSafeJsonArray(cognitiveStrata$, [], "cognitiveStrata");
    const strata = useMemo(
        () => strataRaw.filter(isCognitiveStratumDto) as CognitiveStratumEntry[],
        [strataRaw]
    );
    const byStratum = useMemo(() => {
        const map = new Map<number, CognitiveStratumEntry>();
        for (const e of strata) map.set(e.Stratum, e);
        return map;
    }, [strata]);

    // Local weights — seeded ONCE from the resolved DTO split (the state singleton already holds the
    // population default when the player never allocated). This view is the only writer, so we do not
    // resync from the DTO (that would revert the drag during the ~500 ms republish lag).
    const [weights, setWeights] = useState<Weights>(
        () => normalize(cw.AllocWeightPoor, cw.AllocWeightMiddle, cw.AllocWeightWealthy)
    );
    // Mirror of the live weights, written SYNCHRONOUSLY alongside every setWeights (the two
    // writers below), so onCommit sees the just-applied split. Keyboard commits fire onChange
    // then onDragEnd in one synchronous handler — a mirror updated by a post-render effect would
    // still hold the previous render's weights at that point, committing one keypress behind.
    const weightsRef = useRef(weights);

    const commit = useCallback((w: Weights) => {
        actions.setBroadcastAllocation(w.poor, w.middle, w.wealthy);
    }, [actions]);

    const onSlide = useCallback((key: StratumKey, value: number) => {
        const next = reweight(weightsRef.current, key, value);
        weightsRef.current = next;
        setWeights(next);
    }, []);

    const applyPreset = useCallback((w: Weights) => {
        weightsRef.current = w;
        setWeights(w);
        commit(w);
    }, [commit]);

    const presetEven = useCallback(
        () => applyPreset({ poor: 1 / 3, middle: 1 / 3, wealthy: 1 / 3 }),
        [applyPreset]
    );
    const presetByPop = useCallback(() => {
        const p = byStratum.get(1)?.Count ?? 0;
        const m = byStratum.get(2)?.Count ?? 0;
        const wy = byStratum.get(3)?.Count ?? 0;
        applyPreset(normalize(p, m, wy));
    }, [applyPreset, byStratum]);

    const capacity = cw.CoverageCapacityHouseholds;
    const totalCovered = useMemo(
        () => strata.reduce((s, e) => s + e.CoveredHouseholds, 0),
        [strata]
    );
    const coveredFill = capacity > 0 ? Math.min(100, Math.round((totalCovered / capacity) * 100)) : 0;
    const freeHouseholds = Math.max(0, capacity - totalCovered);

    // ── styles ──
    const presetBtn: React.CSSProperties = {
        padding: "5rem 12rem",
        fontSize: theme.typography.sizeXS, fontWeight: 700, fontFamily: mono,
        letterSpacing: "0.3rem", textTransform: "uppercase",
        color: control, backgroundColor: hexToRgba(control, 0.08),
        border: `2rem solid ${control}`, borderRadius: theme.layout.borderRadius,
        cursor: "pointer",
    };
    const metaText: React.CSSProperties = {
        fontSize: theme.typography.sizeXS, fontFamily: mono, color: theme.colors.textMuted,
    };

    return (
        <Column gap={theme.spacing.md} style={{ width: "100%" }}>
            {/* Total coverable capacity + presets + covered-vs-free bar. Reach/telecom
                read lives in the manage view's status tiles, not here. */}
            <Column gap="6rem">
                <Row align="center" justify="space-between">
                    <span style={{ fontSize: "18rem", fontWeight: 700, fontFamily: mono, color: theme.colors.textPrimary }}>
                        {l.t("UI_OB_ALLOC_COVERABLE", formatHouseholds(capacity))}
                    </span>
                    <Row align="center" gap={theme.spacing.xs}>
                        <button style={presetBtn} onClick={presetEven}>{l.t("UI_OB_ALLOC_PRESET_EVEN")}</button>
                        <button style={presetBtn} onClick={presetByPop}>{l.t("UI_OB_ALLOC_PRESET_BYPOP")}</button>
                    </Row>
                </Row>
                <div style={{ position: "relative", height: "10rem", borderRadius: "5rem", backgroundColor: theme.colors.surface, border: `2rem solid ${theme.colors.border}`, overflow: "hidden" }}>
                    <div style={{ position: "absolute", left: 0, top: 0, bottom: 0, width: `${coveredFill}%`, backgroundColor: theme.colors.success, opacity: 0.8 }} />
                </div>
                <Row align="center" justify="space-between">
                    <span style={{ ...metaText, color: theme.colors.success }}>{l.t("UI_OB_ALLOC_BAR_COVERED", formatHouseholds(totalCovered))}</span>
                    <span style={metaText}>{l.t("UI_OB_ALLOC_BAR_FREE", formatHouseholds(freeHouseholds))}</span>
                </Row>
            </Column>

            {/* One card per stratum, side by side — POOR/MIDDLE/WEALTHY compare at a
                glance, each with its own slider and a verdict anchored at the bottom. */}
            <Row gap={theme.spacing.md} align="stretch" style={{ width: "100%" }}>
                {ROWS.map((row) => (
                    <StratumCard
                        key={row.key}
                        nameKey={STRATUM_NAME_KEY[row.stratumInt] ?? "UI_OB_STRAT_POOR"}
                        entry={byStratum.get(row.stratumInt)}
                        weight={weights[row.key]}
                        capacity={capacity}
                        color={control}
                        onSlide={(v) => onSlide(row.key, v)}
                        onCommit={() => commit(weightsRef.current)}
                    />
                ))}
            </Row>
        </Column>
    );
});

AllocationSection.displayName = "AllocationSection";

interface StratumCardProps {
    nameKey: TranslationKey;
    entry: CognitiveStratumEntry | undefined;
    weight: number;
    capacity: number;
    color: string;
    onSlide: (value: number) => void;
    onCommit: () => void;
}

const StratumCard = memo(({ nameKey, entry, weight, capacity, color, onSlide, onCommit }: StratumCardProps) => {
    const theme = useTheme();
    const accents = useAccents();
    const l = useLocale();
    const b = createBoardStyles(theme, accents);
    const mono = theme.typography.fontFamilyMono;

    const count = entry?.Count ?? 0;
    const signalFraction = entry?.SignalCoverageFraction ?? 0;
    const covered = entry?.CoveredHouseholds ?? 0;
    const reach = entry?.EnemyReachHouseholds ?? 0;
    // Fog contract: reach only sums REVEALED raids; while any raid is still unread the backend
    // raises HasFoggedReach and the row must show honest uncertainty ("+?" / no-intel verdict)
    // instead of a false calm "Safe" over a zero it merely cannot see.
    const foggedReach = entry?.HasFoggedReach ?? false;

    // Allocated updates live from the local weight (the DTO's covered/reach lag until the commit
    // republishes). Cap tick = the weight at which allocation reaches the signal ceiling; past it
    // extra capacity is wasted, so the slider fill beyond the tick buys no coverage.
    const allocated = Math.round(weight * capacity);
    const capWeight = capacity > 0 && signalFraction > 0
        ? Math.min(1, (signalFraction * count) / capacity)
        : null;

    const gap = reach - covered;
    const safe = covered >= reach;
    const verdictColor = foggedReach ? theme.colors.textMuted
        : safe ? theme.colors.success : accents.crisis.accent;
    const reachText = foggedReach
        ? (reach > 0 ? `${formatHouseholds(reach)}+?` : "?")
        : formatHouseholds(reach);

    const metaText: React.CSSProperties = { fontSize: theme.typography.sizeXS, fontFamily: mono, color: theme.colors.textMuted };

    return (
        <Column
            gap={theme.spacing.sm}
            style={{
                flex: 1,
                minWidth: 0,
                backgroundColor: theme.colors.surface,
                border: `2rem solid ${theme.colors.border}`,
                borderRadius: theme.layout.borderRadiusLg,
                padding: theme.spacing.md,
            }}
        >
            <Row align="flex-end" justify="space-between">
                <span style={{ fontSize: theme.typography.sizeSM, fontWeight: 700, fontFamily: mono, color: theme.colors.textPrimary, textTransform: "uppercase", letterSpacing: "0.8rem" }}>
                    {l.t(nameKey)}
                </span>
                <span style={metaText}>{formatHouseholds(count)}</span>
            </Row>

            <AllocSlider
                value={weight}
                cap={capWeight}
                color={color}
                onChange={onSlide}
                onDragEnd={onCommit}
            />

            <Row align="center">
                <span style={metaText}>{l.t("UI_OB_ALLOC_ALLOCATED", formatHouseholds(allocated))}</span>
                <Sep />
                <span style={metaText}>{l.t("UI_OB_ALLOC_SIGNALCAP", pct(signalFraction))}</span>
            </Row>

            <Row align="center">
                <span style={{ fontSize: theme.typography.sizeXS, fontFamily: mono, color: foggedReach ? theme.colors.textMuted : accents.crisis.accent }}>
                    {l.t("UI_OB_ALLOC_REACH_HH", reachText)}
                </span>
                <Sep />
                <span style={{ fontSize: theme.typography.sizeXS, fontFamily: mono, color: theme.colors.success }}>
                    {l.t("UI_OB_ALLOC_COVERED_HH", formatHouseholds(covered))}
                </span>
            </Row>

            {/* Verdict anchors at the card bottom so the three cards read as one row. */}
            <Row align="center" justify="flex-end" style={{ marginTop: "auto" }}>
                <span style={b.chip(verdictColor)}>
                    {foggedReach ? l.t("UI_OB_ALLOC_NO_INTEL")
                        : safe ? l.t("UI_OB_ALLOC_SAFE")
                            : l.t("UI_OB_ALLOC_GAP", formatHouseholds(gap))}
                </span>
            </Row>
        </Column>
    );
});

StratumCard.displayName = "StratumCard";
