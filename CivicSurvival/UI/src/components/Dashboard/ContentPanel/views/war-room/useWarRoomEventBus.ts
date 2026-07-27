/**
 * useWarRoomEventBus — derives the C2 EVENT BUS ticker from binding deltas.
 *
 * NO new back-channel for the wave signals: phase/spawn/intercept/hit/crash lines are
 * computed from changes in the ThreatDto snapshot between ticks. On top of that it folds
 * in the offensive side of the war:
 *   - launch      outbound track count rose (a salvo left the city)
 *   - arrival     a new resolved strike landed on an enemy axis (from ResolvedStrikes)
 *   - intercepted a new resolved strike was caught by the enemy defence
 *
 * The resolved-strike deltas are keyed by the launch-frozen seed, so a strike is logged
 * exactly once even though the ResolvedStrikes ledger is a rolling last-8 window. The
 * buffer keeps the last N lines, newest first.
 */

import { useEffect, useRef, useState } from "react";
import { type ThreatDto } from "@hooks/domain";
import { type ResolvedStrikeDto } from "../../../../../types/domainDtos.generated";
import { type MirrorCitySnapshot } from "@hooks/useMirrorCity";
import { useLocale, type TranslationKey } from "@locales";
import { aaSiteDisplayName, axisLabelKey, strikeTargetDisplayName, toStrikeAxis } from "./strikeTargetNames";

const MAX_EVENTS = 40;
const MAX_SEEN_SEEDS = 64;

// EnemyTarget.NoTargetId on the wire — "nothing resolved" sentinel of ResolvedStrikeDto.TargetId.
const NO_TARGET_ID = 65535;

export type C2EventKind = "phase" | "intercept" | "hit" | "crash" | "spawn" | "launch" | "arrival" | "intercepted";

export interface C2Event {
    id: number;
    time: string;
    kind: C2EventKind;
    text: string;
}

interface Snapshot {
    phase: ThreatDto["WavePhase"];
    waveNumber: number;
    spawned: number;
    intercepted: number;
    hits: number;
    crashed: number;
}

const snapshotOf = (dto: ThreatDto): Snapshot => ({
    phase: dto.WavePhase ?? "calm",
    waveNumber: dto.WaveNumber ?? 0,
    spawned: dto.ThreatsSpawned ?? 0,
    intercepted: dto.ThreatsIntercepted ?? 0,
    hits: dto.ThreatsHit ?? 0,
    crashed: dto.ThreatsCrashed ?? 0,
});

const PHASE_KEY: Record<ThreatDto["WavePhase"], TranslationKey> = {
    calm: "UI_WARROOM_EVT_AIRSPACE_CLEAR",
    alert: "UI_WARROOM_EVT_ALERT",
    attack: "UI_WARROOM_EVT_ATTACK",
    psyattack: "UI_WARROOM_EVT_PSYATTACK",
    recovery: "UI_WARROOM_EVT_RECOVERY",
};

const pad2 = (n: number): string => n.toString().padStart(2, "0");

// Composite dedup identity of one resolved strike (see seenStrikesRef).
const strikeKey = (s: ResolvedStrikeDto): string => `${s.Seed}:${s.Axis}`;

const nowStamp = (): string => {
    const d = new Date();
    return `${pad2(d.getHours())}:${pad2(d.getMinutes())}:${pad2(d.getSeconds())}`;
};

export const useWarRoomEventBus = (
    threat: ThreatDto,
    outboundCount: number,
    resolvedStrikes: ResolvedStrikeDto[],
    intelLevel: number,
    mirrorCity: MirrorCitySnapshot,
): C2Event[] => {
    const { t } = useLocale();
    const [events, setEvents] = useState<C2Event[]>([]);
    const prevRef = useRef<Snapshot | null>(null);
    const prevOutboundRef = useRef(0);
    // Dedup key is (seed, axis), not the bare seed: the launch-frozen seed is hash-derived
    // (never 0, but collisions between two different strikes are possible), and the composite
    // key keeps an unlucky collision from silently swallowing the second strike's C2 line.
    const seenStrikesRef = useRef<Set<string>>(new Set());
    const seqRef = useRef(0);

    useEffect(() => {
        const next = snapshotOf(threat);
        const prev = prevRef.current;

        // First snapshot (or a wave reset/load) seeds every baseline silently — we only
        // ever log forward deltas, never the cold-start absolutes or the backlog ledger.
        if (prev === null || next.waveNumber !== prev.waveNumber) {
            prevRef.current = next;
            prevOutboundRef.current = outboundCount;
            seenStrikesRef.current = new Set(resolvedStrikes.map(strikeKey));
            return;
        }

        const additions: Array<{ kind: C2EventKind; text: string }> = [];
        const stamp = nowStamp();

        if (next.phase !== prev.phase) {
            additions.push({ kind: "phase", text: t(PHASE_KEY[next.phase]) });
        }
        const spawnDelta = next.spawned - prev.spawned;
        if (spawnDelta > 0) {
            additions.push({ kind: "spawn", text: t("UI_WARROOM_EVT_CONTACTS_INBOUND", spawnDelta) });
        }
        const interceptDelta = next.intercepted - prev.intercepted;
        if (interceptDelta > 0) {
            additions.push({ kind: "intercept", text: t("UI_WARROOM_EVT_TARGETS_DOWNED", interceptDelta) });
        }
        const hitDelta = next.hits - prev.hits;
        if (hitDelta > 0) {
            additions.push({ kind: "hit", text: t("UI_WARROOM_EVT_IMPACTS_ON_CITY", hitDelta) });
        }
        const crashDelta = next.crashed - prev.crashed;
        if (crashDelta > 0) {
            additions.push({ kind: "crash", text: t("UI_WARROOM_EVT_CONTACTS_LOST", crashDelta) });
        }

        // Offensive side — a rise in outbound tracks is one or more salvos leaving the city.
        const outboundDelta = outboundCount - prevOutboundRef.current;
        if (outboundDelta > 0) {
            additions.push({ kind: "launch", text: t("UI_WARROOM_EVT_OUTBOUND_LAUNCHED", outboundDelta) });
        }
        prevOutboundRef.current = outboundCount;

        // Resolved strikes — log each new (seed, axis) once (the ledger is a rolling last-8
        // window). Walk oldest-first so the C2 ordering matches arrival order before the prepend.
        const seen = seenStrikesRef.current;
        for (let i = resolvedStrikes.length - 1; i >= 0; i--) {
            const strike = resolvedStrikes[i];
            if (!strike || seen.has(strikeKey(strike))) continue;
            seen.add(strikeKey(strike));

            const axis = toStrikeAxis(strike.Axis);
            // Name by the resolved TargetId (the same identity the strike card uses), falling back
            // to the launch seed only for a strike with no resolved target. An AA pick names as
            // "AA SITE #NNN", matching the card's SEAD naming.
            const targetId = strike.TargetId ?? NO_TARGET_ID;
            const hasTargetId = targetId >= 0 && targetId !== NO_TARGET_ID;
            const isAaTarget = hasTargetId && mirrorCity.aa.some((a) => a.id === targetId);
            const targetName = isAaTarget
                ? aaSiteDisplayName(t, targetId)
                : strikeTargetDisplayName(t, axis, hasTargetId ? targetId : strike.Seed, intelLevel);
            if (strike.Intercepted) {
                additions.push({ kind: "intercepted", text: t("UI_WARROOM_EVT_STRIKE_INTERCEPTED", targetName) });
            } else if (strike.NoEffect) {
                // Landed on the invulnerable reserve — a guaranteed zero, not a success line.
                additions.push({
                    kind: "arrival",
                    text: t("UI_WARROOM_EVT_STRIKE_NO_EFFECT", t(axisLabelKey(axis))),
                });
            } else {
                additions.push({
                    kind: "arrival",
                    text: t(
                        "UI_WARROOM_EVT_STRIKE_LANDED",
                        targetName,
                        t(axisLabelKey(axis)),
                        Math.round(strike.OldValue),
                        Math.round(strike.NewValue),
                    ),
                });
            }
        }
        if (seen.size > MAX_SEEN_SEEDS) {
            seenStrikesRef.current = new Set(resolvedStrikes.map(strikeKey));
        }

        prevRef.current = next;

        if (additions.length === 0) return;

        setEvents((cur) => {
            const appended = additions.map(({ kind, text }) => {
                seqRef.current += 1;
                // Newest first: prepend so the ticker reads top-down latest→oldest.
                return { id: seqRef.current, time: stamp, kind, text };
            });
            return [...appended.reverse(), ...cur].slice(0, MAX_EVENTS);
        });
    }, [threat, outboundCount, resolvedStrikes, intelLevel, mirrorCity, t]);

    return events;
};
