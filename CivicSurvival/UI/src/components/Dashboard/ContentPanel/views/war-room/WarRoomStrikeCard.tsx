/**
 * WarRoomStrikeCard — the selected-target readout for the War Room STRIKE view.
 *
 * Reads the currently-picked mirror-city target out of the (already intel-quantized) snapshot and
 * shows: name (real at L2, "TARGET #NNN" below), tier, axis, contribution, AA coverage over the
 * target, intercept risk, and life-cycle state / rebuild progress. Two buttons fire the EXISTING
 * per-axis operation triggers (prepare / execute) — the pick itself was already sent to C# on
 * selection (SetStrikeTarget), so executing that axis aims at this target when it is still valid.
 *
 * No emoji — SVG-only iconography, inline styles like the surrounding War Room components.
 */

import React, { memo, useMemo } from "react";
import { type Accents, type Theme, hexToRgba } from "@themes";
import { useLocale, type TranslationKey } from "@locales";
import { type GridOperationType } from "../../../../GridWarfare/GridWarfare.types";
import {
    type MirrorCitySnapshot,
    type MirrorAaSite,
} from "@hooks/useMirrorCity";
import { aaSiteDisplayName, axisLabelKey, strikeTargetDisplayName, toStrikeAxis, type StrikeAxis } from "./strikeTargetNames";

// Axis → the operation type whose strike lands on that axis (drone=kinetic/physical,
// blackout=cyber/digital, disinfo=psyops/social), and the strip accent for that axis.
const AXIS_OP: Record<StrikeAxis, GridOperationType> = {
    physical: "drone",
    digital: "blackout",
    social: "disinfo",
};

const axisAccent = (accents: Accents, axis: StrikeAxis): string =>
    axis === "physical" ? accents.crisis.accent : axis === "digital" ? accents.operations.accent : accents.schemes.accent;

// Count of live AA sites whose coverage circle contains this point (world-space).
const coveringAaCount = (aa: MirrorAaSite[], x: number, z: number): number => {
    let n = 0;
    for (const site of aa) {
        if (site.state === "DEAD") continue;
        const dx = site.x - x;
        const dz = site.z - z;
        if (Math.hypot(dx, dz) <= site.range) n += 1;
    }
    return n;
};

// Selected pick: either a mirror target or an AA site (mirrors the useMemo result shape).
type ResolvedPick =
    | { kind: "target"; target: MirrorCitySnapshot["targets"][number] }
    | { kind: "aa"; aa: MirrorAaSite };

const tierLabelKeyFor = (resolved: ResolvedPick): TranslationKey => {
    if (resolved.kind === "aa") return "UI_WARROOM_TIER_AA";
    if (resolved.target.tier === "reserve") return "UI_WARROOM_TIER_RESERVE";
    if (resolved.target.tier === "key") return "UI_WARROOM_TIER_KEY";
    return "UI_WARROOM_TIER_REGULAR";
};

const stateLabelKey = (state: string): TranslationKey => {
    if (state === "DEAD") return "UI_WARROOM_STATE_DEAD";
    if (state === "REBUILDING") return "UI_WARROOM_STATE_REBUILDING";
    if (state === "DAMAGED") return "UI_WARROOM_STATE_DAMAGED";
    return "UI_WARROOM_STATE_INTACT";
};

const riskLabelKey = (covering: number): TranslationKey =>
    covering >= 2 ? "UI_WARROOM_RISK_HIGH" : covering === 1 ? "UI_WARROOM_RISK_MED" : "UI_WARROOM_RISK_LOW";

const riskAccent = (accents: Accents, covering: number): string =>
    covering >= 2 ? accents.crisis.accent : covering === 1 ? accents.resilience.accent : accents.schemes.accent;

interface Props {
    snapshot: MirrorCitySnapshot;
    selectedTargetId: number | null;
    intelLevel: number;
    accents: Accents;
    theme: Theme;
    onPrepare: (op: GridOperationType) => void;
    onExecute: (op: GridOperationType) => void;
}

export const WarRoomStrikeCard: React.FC<Props> = memo(({
    snapshot,
    selectedTargetId,
    intelLevel,
    accents,
    theme,
    onPrepare,
    onExecute,
}) => {
    const l = useLocale();
    const mono = theme.typography.fontFamilyMono;

    const resolved = useMemo(() => {
        if (selectedTargetId === null || selectedTargetId < 0) return null;
        const target = snapshot.targets.find((t) => t.id === selectedTargetId) ?? null;
        if (target) return { kind: "target" as const, target };
        const aa = snapshot.aa.find((a) => a.id === selectedTargetId) ?? null;
        if (aa) return { kind: "aa" as const, aa };
        return null;
    }, [snapshot, selectedTargetId]);

    const wrap: React.CSSProperties = {
        display: "flex",
        flexDirection: "column",
        minHeight: 0,
        flexShrink: 0,
        padding: `${theme.spacing.sm} ${theme.spacing.md}`,
        borderRadius: theme.layout.borderRadiusLg,
        border: `1rem solid ${hexToRgba(accents.crisis.accent, 0.4)}`,
        background: hexToRgba(accents.crisis.accent, 0.06),
        fontFamily: mono,
    };

    // Empty state — no pick yet.
    if (!resolved) {
        return (
            <div style={{ ...wrap, alignItems: "center" }}>
                <span style={{ fontSize: "11rem", color: theme.colors.textMuted }}>
                    {l.t("UI_WARROOM_STRIKE_PICK_HINT")}
                </span>
            </div>
        );
    }

    const axis: StrikeAxis = resolved.kind === "target" ? toStrikeAxis(resolved.target.axis) : "physical";
    const accent = axisAccent(accents, axis);
    const id = resolved.kind === "target" ? resolved.target.id : resolved.aa.id;
    // An AA pick names itself as an AA site — the axis TARGET pool is for offensive objectives.
    const name = resolved.kind === "aa"
        ? aaSiteDisplayName(l.t, id)
        : strikeTargetDisplayName(l.t, axis, id, intelLevel);
    const op = AXIS_OP[axis];

    const state = resolved.kind === "target" ? resolved.target.state : resolved.aa.state;
    const isDead = state === "DEAD";
    const isReserve = resolved.kind === "target" && resolved.target.tier === "reserve";
    // Reserve is indestructible and a dead key/regular has nothing to hit — a strike then falls back
    // to auto-target. Surface it rather than pretending the button aims here.
    const strikeFallsBack = isReserve;

    const tierLabelKey = tierLabelKeyFor(resolved);
    const stateKey = stateLabelKey(state);

    // AA coverage over the target (a target's intercept exposure). For an AA-site pick, show its own reach.
    const covering = resolved.kind === "target"
        ? coveringAaCount(snapshot.aa, resolved.target.x, resolved.target.z)
        : 0;
    const riskKey = riskLabelKey(covering);
    const riskColor = riskAccent(accents, covering);

    const labelStyle: React.CSSProperties = {
        fontSize: "10rem",
        textTransform: "uppercase",
        letterSpacing: "0.5rem",
        color: theme.colors.textSecondary,
    };
    const valueStyle: React.CSSProperties = {
        fontSize: "11rem",
        fontWeight: theme.typography.weightBold,
        color: theme.colors.textPrimary,
    };
    const row: React.CSSProperties = {
        display: "flex",
        alignItems: "center",
        justifyContent: "space-between",
        marginTop: "4rem",
    };
    const button = (enabled: boolean, color: string): React.CSSProperties => ({
        display: "flex",
        flex: 1,
        alignItems: "center",
        justifyContent: "center",
        padding: "7rem 0",
        marginTop: "8rem",
        background: "transparent",
        border: `1rem solid ${enabled ? color : hexToRgba(color, 0.3)}`,
        borderRadius: theme.layout.borderRadiusLg,
        cursor: enabled ? "pointer" : "default",
        fontSize: "11rem",
        fontWeight: theme.typography.weightBold,
        textTransform: "uppercase",
        letterSpacing: "0.5rem",
        color: enabled ? color : theme.colors.textMuted,
        fontFamily: mono,
    });

    const canStrike = !isDead;

    return (
        <div style={wrap}>
            {/* Header — name + tier/axis badges */}
            {/* flex-end, not baseline: Coherent rejects baseline (falls back to stretch) */}
            <div style={{ display: "flex", alignItems: "flex-end", justifyContent: "space-between" }}>
                <span style={{ fontSize: "14rem", fontWeight: theme.typography.weightBold, color: accent, letterSpacing: "0.5rem" }}>
                    {name}
                </span>
                <span style={{ fontSize: "10rem", fontWeight: theme.typography.weightBold, textTransform: "uppercase", letterSpacing: "1rem", color: hexToRgba(accent, 0.85) }}>
                    {`${l.t(tierLabelKey)} · ${l.t(axisLabelKey(axis))}`}
                </span>
            </div>

            {/* Contribution (targets) or role (AA) */}
            <div style={row}>
                <span style={labelStyle}>
                    {resolved.kind === "aa" ? l.t("UI_WARROOM_STRIKE_ROLE") : l.t("UI_WARROOM_STRIKE_CONTRIB")}
                </span>
                <span style={valueStyle}>
                    {resolved.kind === "aa"
                        ? l.t("UI_WARROOM_STRIKE_ROLE_AA")
                        : intelLevel >= 2
                            ? `${Math.round(resolved.target.contrib)}%`
                            : `~${Math.round(resolved.target.contrib)}%`}
                </span>
            </div>

            {/* AA coverage / intercept risk */}
            <div style={row}>
                <span style={labelStyle}>{l.t("UI_WARROOM_STRIKE_AA_COVER")}</span>
                <span style={{ ...valueStyle, color: riskColor }}>
                    {resolved.kind === "aa"
                        ? l.t("UI_WARROOM_STRIKE_AA_SELF")
                        : `${l.t(riskKey)} (${covering})`}
                </span>
            </div>

            {/* State / rebuild */}
            <div style={row}>
                <span style={labelStyle}>{l.t("UI_WARROOM_STRIKE_STATE")}</span>
                <span style={valueStyle}>
                    {state === "REBUILDING" && resolved.kind === "target" && resolved.target.rebuildPct >= 0
                        ? l.t("UI_WARROOM_STATE_REBUILDING_PCT", Math.round(resolved.target.rebuildPct))
                        : l.t(stateKey)}
                </span>
            </div>

            {strikeFallsBack && (
                <span style={{ fontSize: "10rem", color: theme.colors.textMuted, marginTop: "6rem" }}>
                    {l.t("UI_WARROOM_STRIKE_FALLBACK")}
                </span>
            )}

            {/* Prepare / Execute the axis operation (existing triggers). */}
            <div style={{ display: "flex", alignItems: "center" }}>
                <div
                    style={{ ...button(true, accent), marginRight: "8rem" }}
                    role="button"
                    tabIndex={0}
                    onClick={() => onPrepare(op)}
                    onKeyDown={(e) => {
                        if (e.key === "Enter" || e.key === " ") { e.preventDefault(); onPrepare(op); }
                    }}
                >
                    {l.t("UI_WARROOM_STRIKE_PREPARE")}
                </div>
                <div
                    style={button(canStrike, accents.crisis.accent)}
                    role="button"
                    tabIndex={0}
                    onClick={() => { if (canStrike) onExecute(op); }}
                    onKeyDown={(e) => {
                        if (canStrike && (e.key === "Enter" || e.key === " ")) { e.preventDefault(); onExecute(op); }
                    }}
                >
                    {l.t("UI_WARROOM_STRIKE_EXECUTE")}
                </div>
            </div>
        </div>
    );
});

WarRoomStrikeCard.displayName = "WarRoomStrikeCard";
