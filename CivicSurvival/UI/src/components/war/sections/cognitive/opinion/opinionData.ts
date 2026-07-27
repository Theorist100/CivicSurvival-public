/**
 * Opinion Board — static scaffold + live derivations.
 *
 * The per-stratum crowd (count / opinion split / infection / resistance) and the
 * raids are ALWAYS live: counts come from the CognitiveStatsState read model
 * (buildLiveWealthAxis) and raids from in-flight PsyOpsAttack contacts. This file
 * carries only the STATIC game-design scaffold — stratum names/tags, the best-vs
 * hero archetype and the applicable countermeasure tools — plus the pure
 * derivations (share, trend, dot cloud, fog). It fabricates no numbers and no
 * raids: an unclassified/peaceful city renders an honest zero / "no operation"
 * state, never illustrative data.
 */

import { type TranslationKey } from "../../../../../locales";
import {
    type CognitiveStratumEntry,
    type CognitivePsyOpsEntry,
} from "../../../../../types/domainDtos.generated";
import { type HeroArchetypeType } from "../../../../../hooks/domain/cognitiveLabels";

export type AllegianceKind = "you" | "neu" | "enemy";
export type RaidType = "rumor" | "propaganda" | "fakevideo";
export type TrendKind = "holding" | "enemy" | "contested";
export type AxisId = "wealth";
export type ToolKind = "aid" | "broadcast";
export type ArchetypeId = "voice" | "arestovych" | "patriot";

/** A raid is always a live PsyOpsAttack contact now (no sample raids). */
export interface RaidInfo {
    type: RaidType;
    /** Enemy infection share, 0..1 — drives the contagion-dot clump + pulse. */
    infect: number;
    /**
     * True when the raid landed BLOCKED by the active speaker — the frozen
     * land-moment snapshot (PsyOpsAttack.LandedBlunted), not the current posture,
     * so a later speaker switch never rewrites it. Always false while in flight.
     */
    held: boolean;
    /** True once the carrier type is revealed (fog ≥ Detected). */
    known: boolean;
    /** Lifecycle phase: 0 = InFlight, 1 = Landed. */
    phase: number;
    /** Game-hours left until the interception window closes. */
    windowHours: number;
    /** Interception window remaining as a fraction of the full window, 0..1. */
    windowFraction: number;
    /** Game-hours until the attack reaches its target. */
    etaHours: number;
}

/** Static game-design scaffold for a stratum — no backend numbers, no raids. */
export interface StratumScaffold {
    id: string;
    nameKey: TranslationKey;
    tagKey: TranslationKey;
    /**
     * Standing countermeasure tools (battle axis only).
     *
     * NOTE: there is deliberately no per-stratum hero here. A hero counters an attack
     * CARRIER, never a stratum (C# StratumDefense.Counters) — the scaffold used to pin one
     * archetype per stratum, and CountermeasuresPanel read it as the "best answer", which
     * recommended a different hero than the raid card right above it. Use
     * COUNTER_ARCHETYPE_BY_TYPE against the live raid instead.
     */
    tools: ToolKind[];
}

/** A rendered stratum — scaffold plus the live-only backend numbers. */
export interface Stratum extends StratumScaffold {
    /** Households in the stratum (drives column width + dot count). */
    count: number;
    /** Loyal share, 0..100. */
    you: number;
    /** Enemy-swayed share, 0..100 (remainder = undecided). */
    enemy: number;
    /** Infection 0..1. */
    infect: number;
    /** Resistance 0..1. */
    resist: number;
    /** Live raid targeting this stratum, if a real contact is in flight. */
    raid?: RaidInfo;
}

/** Static axis scaffold — the lens definition, no live numbers. */
export interface AxisScaffold {
    id: AxisId;
    labelKey: TranslationKey;
    /** Only the battle axis exposes countermeasures; others are analysis. */
    isBattle: boolean;
    strata: StratumScaffold[];
}

export interface Axis {
    id: AxisId;
    labelKey: TranslationKey;
    /** Only the battle axis exposes countermeasures; others are analysis. */
    isBattle: boolean;
    strata: Stratum[];
}

export interface Archetype {
    id: ArchetypeId;
    nameKey: TranslationKey;
    buffKey: TranslationKey;
    debuffKey: TranslationKey;
    bestVsKey: TranslationKey;
    /** Arestovych carries a trust-debt collapse risk surfaced in the picker. */
    trustDebt: boolean;
}

// ============================================================================
// Wealth axis scaffold — the only lens with a backend. Names/tags/best-vs
// archetype/tools are game design; every number and raid is overlaid live by
// buildLiveWealthAxis. Education/district were sample-only lenses and are gone
// (see Docs/Plans/CognitiveUpgrade/Phase-11-LiveStrataNoTemplate.md).
// ============================================================================

export const WEALTH_SCAFFOLD: AxisScaffold = {
    id: "wealth",
    labelKey: "UI_OB_AXIS_WEALTH",
    isBattle: true,
    strata: [
        { id: "poor", nameKey: "UI_OB_STRAT_POOR", tagKey: "UI_OB_TAG_POOR", tools: ["aid", "broadcast"] },
        { id: "middle", nameKey: "UI_OB_STRAT_MIDDLE", tagKey: "UI_OB_TAG_MIDDLE", tools: ["broadcast"] },
        { id: "wealthy", nameKey: "UI_OB_STRAT_WEALTHY", tagKey: "UI_OB_TAG_WEALTHY", tools: [] },
    ],
};

/** Static-data invariant guard — the scaffold strata are compile-time constants. */
function req<T>(value: T | undefined): T {
    if (value === undefined) throw new Error("Opinion board data invariant violated");
    return value;
}

/** First stratum of an axis (axes are never empty). */
export function firstStratum(axis: Axis): Stratum {
    return req(axis.strata[0]);
}

export const ARCHETYPES: readonly Archetype[] = [
    { id: "voice", nameKey: "UI_OB_ARCH_VOICE", buffKey: "UI_OB_ARCH_VOICE_BUFF", debuffKey: "UI_OB_ARCH_VOICE_DEBUFF", bestVsKey: "UI_OB_TYPE_RUMOR", trustDebt: false },
    { id: "arestovych", nameKey: "UI_OB_ARCH_ARESTOVYCH", buffKey: "UI_OB_ARCH_ARESTOVYCH_BUFF", debuffKey: "UI_OB_ARCH_ARESTOVYCH_DEBUFF", bestVsKey: "UI_OB_TYPE_FAKEVIDEO", trustDebt: true },
    { id: "patriot", nameKey: "UI_OB_ARCH_PATRIOT", buffKey: "UI_OB_ARCH_PATRIOT_BUFF", debuffKey: "UI_OB_ARCH_PATRIOT_DEBUFF", bestVsKey: "UI_OB_TYPE_PROPAGANDA", trustDebt: false },
];

/** Default hero archetype (Voice). */
export const DEFAULT_ARCHETYPE: Archetype = req(ARCHETYPES[0]);

export const RAID_TYPE_LABEL: Record<RaidType, TranslationKey> = {
    rumor: "UI_OB_TYPE_RUMOR",
    propaganda: "UI_OB_TYPE_PROPAGANDA",
    fakevideo: "UI_OB_TYPE_FAKEVIDEO",
};

/**
 * Harm a landed raid does, by carrier — a qualitative, type-derived label for the
 * INTEL "what hit you" line. The live magnitudes (media trust %, food-run %, etc.)
 * live in the STATE-of-the-info-war readout; the per-raid entry only names the vector.
 */
export const RAID_HARM_LABEL: Record<RaidType, TranslationKey> = {
    rumor: "UI_OB_HARM_RUMOR",
    propaganda: "UI_OB_HARM_PROPAGANDA",
    fakevideo: "UI_OB_HARM_FAKEVIDEO",
};

export const TOOL_LABEL: Record<ToolKind, TranslationKey> = {
    aid: "UI_OB_TOOL_AID",
    broadcast: "UI_OB_TOOL_BROADCAST",
};

// ============================================================================
// Derivations — every visual reads from the data, never hardcoded twice.
// ============================================================================

export interface CityOpinion {
    you: number;
    neu: number;
    enemy: number;
}

/** Population-weighted you/undecided/enemy mix across an axis (whole-city, axis-invariant). */
export function cityOpinion(axis: Axis): CityOpinion {
    let total = 0;
    let you = 0;
    let enemy = 0;
    for (const s of axis.strata) {
        total += s.count;
        you += s.count * s.you;
        enemy += s.count * s.enemy;
    }
    if (total === 0) return { you: 0, neu: 100, enemy: 0 };
    // Round `you`, then derive the other two from it so the three always sum to 100.
    // Rounding you and enemy independently can push their sum to 101 (both round up),
    // which would overflow the bar; cap enemy against the 100 − you remainder so
    // undecided is what's left over, never negative.
    const youRounded = Math.round(you / total);
    const enemyRounded = Math.min(Math.round(enemy / total), 100 - youRounded);
    return {
        you: youRounded,
        enemy: enemyRounded,
        neu: 100 - youRounded - enemyRounded,
    };
}

/** Stratum share of the whole axis population, 0..100. */
export function shareOf(axis: Axis, stratum: Stratum): number {
    const total = axis.strata.reduce((sum, s) => sum + s.count, 0);
    if (total === 0) return 0;
    return Math.round((stratum.count / total) * 100);
}

/** Trend read: you lead by >8 ⇒ holding; enemy ahead ⇒ enemy; else contested. */
export function trendOf(stratum: Stratum): TrendKind {
    if (stratum.enemy > stratum.you) return "enemy";
    if (stratum.you - stratum.enemy > 8) return "holding";
    return "contested";
}

/** Compact "34k" household count. */
export function formatHouseholds(count: number): string {
    if (count >= 1000) return `${Math.round(count / 1000)}k`;
    return String(count);
}

/**
 * Ordered dot composition for a stratum: loyal, then undecided, then enemy — a
 * wrapped "front line". The colour blocks are contiguous, so the proportion (and
 * who leads) reads at a glance, and an active raid simply grows the enemy block
 * at the tail. Reading order is left-to-right, top-to-bottom like text, so the
 * ordering survives the cards wrapping the dots across rows.
 */
export function dotCloud(stratum: Stratum, householdsPerDot: number): AllegianceKind[] {
    // A non-positive divisor would make `dots` Infinity and freeze the render loop.
    if (householdsPerDot <= 0) return ["neu"];
    const dots = Math.max(1, Math.round(stratum.count / householdsPerDot));
    let youDots = Math.round((dots * stratum.you) / 100);
    let enemyDots = Math.round((dots * stratum.enemy) / 100);
    // Keep the total ON budget: independent rounding can overspend by a dot — trim the
    // excess from the larger committed block (never let the sum exceed `dots`, and never
    // silently eat a real undecided remainder to absorb the overshoot).
    let excess = youDots + enemyDots - dots;
    if (excess > 0) {
        if (youDots >= enemyDots) {
            const cut = Math.min(youDots, excess);
            youDots -= cut;
            excess -= cut;
            enemyDots -= Math.min(enemyDots, excess);
        } else {
            const cut = Math.min(enemyDots, excess);
            enemyDots -= cut;
            excess -= cut;
            youDots -= Math.min(youDots, excess);
        }
    }
    const neuDots = Math.max(0, dots - youDots - enemyDots);

    const ordered: AllegianceKind[] = [];
    for (let i = 0; i < youDots; i++) ordered.push("you");
    for (let i = 0; i < neuDots; i++) ordered.push("neu");
    for (let i = 0; i < enemyDots; i++) ordered.push("enemy");
    return ordered;
}

// ============================================================================
// Live backend overlay — the wealth battle axis + PSYOPS contacts come from C#
// bindings (CognitiveStatsState per-stratum read model + PsyOpsAttack entities).
// The scaffold above supplies only static design (names, tags, archetype-best-vs,
// tools); counts, infection, resistance and raids are ALWAYS live. There is no
// fallback template: an empty binding yields a real zero-state axis, and a raid
// appears only for a real in-flight contact.
// ============================================================================

/** C# SocialStratum int (1/2/3) → wealth-axis display id. */
const STRATUM_DISPLAY_ID: Record<number, string> = { 1: "poor", 2: "middle", 3: "wealthy" };

/** C# PsyOpsAttackType int (0/1/2) → raid carrier. */
const PSYOPS_TYPE_BY_INT: Record<number, RaidType> = { 0: "propaganda", 1: "rumor", 2: "fakevideo" };

/** C# SocialStratum int (1/2/3) → stratum display name key (0/Unknown → not present → null). */
export const STRATUM_NAME_KEY: Record<number, TranslationKey> = {
    1: "UI_OB_STRAT_POOR",
    2: "UI_OB_STRAT_MIDDLE",
    3: "UI_OB_STRAT_WEALTHY",
};

/** Phase 13B forecast: carrier label for a predicted type int, or null when unknown (-1 / fogged / tie). */
export function forecastTypeLabelKey(type: number): TranslationKey | null {
    const carrier = PSYOPS_TYPE_BY_INT[type];
    return carrier === undefined ? null : RAID_TYPE_LABEL[carrier];
}

/** Phase 13B forecast: seam name for a predicted stratum int, or null when unknown (0 / fogged). */
export function forecastStratumNameKey(stratum: number): TranslationKey | null {
    return STRATUM_NAME_KEY[stratum] ?? null;
}

/** C# HeroArchetype int (0/1/2) ↔ picker archetype id. */
export const ARCHETYPE_BY_INT: Record<number, ArchetypeId> = { 0: "voice", 1: "arestovych", 2: "patriot" };
export const ARCHETYPE_TO_INT: Record<ArchetypeId, HeroArchetypeType> = { voice: 0, arestovych: 1, patriot: 2 };

/**
 * RPS best response: the archetype that hard-counters each carrier
 * (mirrors C# StratumDefense.Counters — Voice←Rumor, Arestovych←FakeVideo,
 * Patriot←Propaganda). Pure type→archetype map, computed UI-side (no new state).
 */
export const COUNTER_ARCHETYPE_BY_TYPE: Record<RaidType, ArchetypeId> = {
    propaganda: "patriot",
    rumor: "voice",
    fakevideo: "arestovych",
};

// Intel fog tiers over an incoming telegraph (mirrors the C# reader projection).
export const FOG_HIDDEN = 0;
export const FOG_DETECTED = 1;
export const FOG_REVEALED = 2;

/** Fog badge key for a partially-read contact, or null once fully revealed. */
export function fogLabelKey(fog: number): TranslationKey | null {
    if (fog >= FOG_REVEALED) return null;
    return fog === FOG_DETECTED ? "UI_OB_FOG_DETECTED" : "UI_OB_FOG_HIDDEN";
}

export interface LiveRaid {
    id: string;
    type: RaidType;
    /** Wealth-axis display id the attack targets ("" when the seam is fogged). */
    targetId: string;
    /** True once the exact target seam is revealed (fog ≥ Revealed). */
    targetKnown: boolean;
    phase: number;
    windowHours: number;
    windowFraction: number;
    etaHours: number;
    /** Attack strength at launch, 0..1 (bucketed while fogged). */
    intensity: number;
    /** Dominant contact of its raid — surfaced only once fog ≥ Detected. */
    isDominant: boolean;
    /** Intel fog tier: 0 Hidden, 1 Detected, 2 Revealed. */
    fog: number;
    /** True once the carrier type is revealed (fog ≥ Detected). */
    known: boolean;
    // ── Phase 13C — legible counter outcome (two honest signals, never multiplied together).
    //    For a LANDED contact both signals are the frozen land-moment snapshot; for an
    //    in-flight contact they are the live read under the current speaker posture. ──
    /** Effective RPS hero cut on this contact's impact, 0..1 (the exact × (1 − heroShield) factor). */
    heroShield: number;
    /** True when the archetype hard-counters this type at full strength ("blunted"). */
    blunted: boolean;
    /** Target stratum's telecom coverage fraction, 0..1 (defence context, shown separately). */
    targetCoverage: number;
    /** Estimated households the strike reaches in its seam (split into shielded vs hit in the UI). */
    reachHouseholds: number;
    /** True when the raid landed BLOCKED (landed && blunted — the frozen land snapshot). */
    held: boolean;
    /** HeroArchetype int of the speaker at the landing moment (snapshot); -1 = not landed / no hero. */
    landedByArchetype: number;
}

/**
 * Raid accent state — the single colour contract shared by every raid surface
 * (IntelScreen row, ThreatCard, StratumCard badge):
 * "contained" = landed BLOCKED (success-green), "struck" = landed clean
 * (error-red — damage, never green), "live" = still in flight (amber, act now).
 */
export type RaidAccentState = "live" | "contained" | "struck";

/** Resolve the accent state from a raid's landed outcome (works for RaidInfo and LiveRaid). */
export function raidAccentState(r: { phase: number; held: boolean }): RaidAccentState {
    return r.phase === 1 ? (r.held ? "contained" : "struck") : "live";
}

/** Compact one-decimal game-hours string (the locale key supplies the unit). */
export function formatGameHours(hours: number): string {
    return Math.max(0, hours).toFixed(1);
}

function clampPct(value: number): number {
    return Math.round(Math.max(0, Math.min(1, value)) * 100);
}

/** Build the live PSYOPS contacts list from the binding entries. */
export function buildLiveRaids(entries: readonly CognitivePsyOpsEntry[]): LiveRaid[] {
    return entries.map((e) => {
        const known = e.PsyOpsType >= 0;
        const displayId = STRATUM_DISPLAY_ID[e.TargetStratum];
        return {
            // Stable identity from the launch-frozen ContactId (attack Seed), not the list
            // index — a React key that survives contacts expiring/reordering around it.
            id: `psyops-${e.ContactId}`,
            // Carrier hidden while fogged — keep a stable fallback for typing; renderers gate on `known`.
            type: known ? (PSYOPS_TYPE_BY_INT[e.PsyOpsType] ?? "propaganda") : "propaganda",
            targetId: displayId ?? "",
            targetKnown: displayId !== undefined,
            phase: e.Phase,
            windowHours: e.WindowHours,
            windowFraction: e.WindowFraction,
            etaHours: e.EtaHours,
            intensity: e.Intensity,
            isDominant: e.IsDominant,
            fog: e.FogState,
            known,
            heroShield: e.HeroShield,
            blunted: e.Blunted > 0,
            targetCoverage: e.TargetCoverage,
            reachHouseholds: e.ReachHouseholds,
            // Landed outcome — for phase 1 Blunted carries the frozen land snapshot.
            held: e.Phase === 1 && e.Blunted > 0,
            landedByArchetype: e.LandedByArchetype,
        };
    });
}

function toRaidInfo(raid: LiveRaid): RaidInfo {
    return {
        type: raid.type,
        held: raid.held,
        known: raid.known,
        // The contagion-dot pulse reads the raid's own strength for live contacts.
        infect: raid.intensity,
        phase: raid.phase,
        windowHours: raid.windowHours,
        windowFraction: raid.windowFraction,
        etaHours: raid.etaHours,
    };
}

/**
 * Build the live wealth battle axis: overlay live per-stratum counts/infection/
 * resistance onto the static scaffold and attach live raids to their target
 * stratum. Never fabricates — a missing lane reads as a real zero-state stratum
 * (counts 0, no raid), so even a momentarily-empty binding shows an honest empty
 * crowd instead of illustrative data.
 *
 * Loyal/enemy split is a display derivation from the two real backend signals:
 * enemy ≈ infection, loyal ≈ resistance (resistance is the resilience-to-turning
 * signal); undecided is the remainder. Replace if a dedicated control field ships.
 */
export function buildLiveWealthAxis(
    entries: readonly CognitiveStratumEntry[],
    raids: readonly LiveRaid[],
): Axis {
    const byId = new Map<string, CognitiveStratumEntry>();
    for (const e of entries) {
        const id = STRATUM_DISPLAY_ID[e.Stratum];
        if (id !== undefined) byId.set(id, e);
    }

    const strata = WEALTH_SCAFFOLD.strata.map((scaffold): Stratum => {
        const live = byId.get(scaffold.id);
        const count = live?.Count ?? 0;
        const infect = live?.Infection ?? 0;
        const resist = live?.Resistance ?? 0;
        const enemy = clampPct(infect);
        const you = Math.min(100 - enemy, clampPct(resist));
        // A stratum gets a raid ONLY when a revealed live contact targets it; fogged
        // contacts stay in the INTEL list without leaking their seam. Among in-flight
        // candidates prefer the DOMINANT contact (the main threat the player is meant to
        // read), then the strongest; fall back to a landed one. No template-raid fallback.
        let contact: LiveRaid | undefined;
        for (const r of raids) {
            if (!r.targetKnown || r.targetId !== scaffold.id || r.phase !== 0) continue;
            if (contact === undefined
                || (r.isDominant && !contact.isDominant)
                || (r.isDominant === contact.isDominant && r.intensity > contact.intensity)) {
                contact = r;
            }
        }
        contact ??= raids.find((r) => r.targetKnown && r.targetId === scaffold.id);

        const entry: Stratum = { ...scaffold, count, infect, resist, you, enemy };
        // Attach a raid ONLY when a live contact targets this stratum — never assign
        // undefined (exactOptionalPropertyTypes) so a peaceful stratum simply omits it.
        if (contact !== undefined) entry.raid = toRaidInfo(contact);
        return entry;
    });

    return {
        id: WEALTH_SCAFFOLD.id,
        labelKey: WEALTH_SCAFFOLD.labelKey,
        isBattle: WEALTH_SCAFFOLD.isBattle,
        strata,
    };
}

/** Timing + landed-outcome fields shared by RaidInfo (crowd card) and LiveRaid (INTEL list). */
type RaidTiming = Pick<RaidInfo, "phase" | "held" | "windowHours" | "etaHours">;

/**
 * Localized status descriptor for a raid badge/row — used by both the crowd card
 * (RaidInfo) and the INTEL list (LiveRaid), which share the same timing fields.
 * Returns a key + optional arg so the caller does
 * `l.t(key, ...(arg ? [arg] : []))`.
 *
 * The landed text mirrors raidAccentState's colour: a BLOCKED landing reads
 * "contained" (green), a clean landing reads "struck" (red) — the word never
 * contradicts the badge colour.
 */
export function raidStatus(raid: RaidTiming): { key: TranslationKey; arg?: string } {
    if (raid.phase === 1) return { key: raid.held ? "UI_OB_RAID_CONTAINED" : "UI_OB_RAID_STRUCK" };
    if (raid.windowHours > 0) return { key: "UI_OB_INTERCEPT_H", arg: formatGameHours(raid.windowHours) };
    return { key: "UI_OB_RAID_INBOUND_H", arg: formatGameHours(raid.etaHours) };
}
