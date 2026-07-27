import { useEffect, useMemo, useState } from "react";
import { updateAnnounceState$, INACTIVE_ANNOUNCE_JSON } from "./bindings/updateAnnounceBindings";
import { useSafeString } from "./useSafeBinding";

export interface UpdateAnnounce {
    /** An update announce is active (publish time still ahead or just passed). */
    active: boolean;
    /** Whole minutes until the announced publish; 0 when the moment has arrived/passed. */
    minutesRemaining: number;
}

const INACTIVE: UpdateAnnounce = { active: false, minutesRemaining: 0 };

// Announce lingers briefly after its publish moment ("publishing…" badge state) and then
// drops locally even if the next poll hasn't confirmed the server-side expiry yet.
const LINGER_AFTER_PUBLISH_MS = 10 * 60 * 1000;
const TICK_MS = 30 * 1000;

const parsePublishAtUnix = (raw: string): number => {
    try {
        const parsed: unknown = JSON.parse(raw);
        if (typeof parsed !== "object" || parsed === null) return 0;
        const rec = parsed as { Active?: unknown; PublishAtUnix?: unknown };
        if (rec.Active !== true || typeof rec.PublishAtUnix !== "number") return 0;
        return Math.max(0, rec.PublishAtUnix);
    } catch {
        return 0;
    }
};

/**
 * Live countdown to an announced mod update. Re-renders every 30s while an announce is
 * active so the badge keeps ticking between the C# system's 2-minute polls.
 */
export function useUpdateAnnounce(): UpdateAnnounce {
    const raw = useSafeString(updateAnnounceState$, INACTIVE_ANNOUNCE_JSON, "updateAnnounceState");
    const publishAtUnix = useMemo(() => parsePublishAtUnix(raw), [raw]);

    const [nowMs, setNowMs] = useState(() => Date.now());
    useEffect(() => {
        if (publishAtUnix <= 0) {
            return;
        }
        setNowMs(Date.now());
        const id = window.setInterval(() => setNowMs(Date.now()), TICK_MS);
        return () => window.clearInterval(id);
    }, [publishAtUnix]);

    return useMemo(() => {
        if (publishAtUnix <= 0) return INACTIVE;
        const remainingMs = publishAtUnix * 1000 - nowMs;
        if (remainingMs < -LINGER_AFTER_PUBLISH_MS) return INACTIVE;
        return {
            active: true,
            minutesRemaining: Math.max(0, Math.ceil(remainingMs / 60000)),
        };
    }, [publishAtUnix, nowMs]);
}
