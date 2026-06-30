/**
 * useWarRoomEventBus — derives the C2 EVENT BUS ticker from ThreatDto deltas.
 *
 * NO new back-channel (Phase 35 A.2): the feed is computed entirely from changes
 * in the existing ThreatDto snapshot between binding ticks. Each tick we compare
 * the new snapshot to the previous one and emit a line for:
 *   - phase change          (calm/alert/attack/recovery)
 *   - intercepted increase  (one or more threats downed)
 *   - hit increase          (one or more threats struck the city)
 *   - crash increase        (one or more threats fell short)
 *   - spawn increase        (new contacts entered the airspace)
 *
 * The snapshot is a count-state, not an event stream — so we report the delta as
 * a single aggregated line per tick (one localized event), keeping the feed honest
 * about what the binding actually told us. The buffer keeps the last N lines.
 */

import { useEffect, useRef, useState } from "react";
import { type ThreatDto } from "@hooks/domain";
import { useLocale, type TranslationKey } from "@locales";

const MAX_EVENTS = 40;

export type C2EventKind = "phase" | "intercept" | "hit" | "crash" | "spawn";

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
    recovery: "UI_WARROOM_EVT_RECOVERY",
};

const pad2 = (n: number): string => n.toString().padStart(2, "0");

const nowStamp = (): string => {
    const d = new Date();
    return `${pad2(d.getHours())}:${pad2(d.getMinutes())}:${pad2(d.getSeconds())}`;
};

export const useWarRoomEventBus = (threat: ThreatDto): C2Event[] => {
    const { t } = useLocale();
    const [events, setEvents] = useState<C2Event[]>([]);
    const prevRef = useRef<Snapshot | null>(null);
    const seqRef = useRef(0);

    useEffect(() => {
        const next = snapshotOf(threat);
        const prev = prevRef.current;

        // First snapshot (or a wave reset/load) seeds the baseline silently — we
        // only ever log forward deltas, never the cold-start absolute values.
        if (prev === null || next.waveNumber !== prev.waveNumber) {
            prevRef.current = next;
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
    }, [threat, t]);

    return events;
};
