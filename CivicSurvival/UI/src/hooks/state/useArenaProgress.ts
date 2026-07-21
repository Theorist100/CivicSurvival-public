/**
 * Hook for the player's ladder progress (Duolingo-style feedback).
 *
 * Combines the server-confirmed score (YourStats from the leaderboard fetch)
 * with the locally pending score C# accumulates from WaveEndedEvent, and maps
 * the total onto the config-driven rank tiers. `known` flips true after the
 * first server confirmation — celebrations and the progress bar stay silent
 * until then so a fresh city load never fires a false rank-up.
 */

import { useMemo } from "react";
import { useNumberBinding, useValidatedJsonArray } from "../useSafeBinding";
import {
    arenaAllTimeDelta$,
    arenaPendingScore$,
    arenaRankTiers$,
    arenaWeeklyDelta$,
    arenaYourScore$,
} from "../bindings";
import { combineBindingStates } from "../domain/combineBindingStates";
import { type BindingState } from "../domain/useDtoBinding";
import { isRankTierDto, type RankTierDto } from "../../types/domainDtos.generated";
import { DEFAULT_RANK_TIERS } from "./useArenaLeaderboard";
import { type RankTierDef } from "./ladderTypes";

export interface ArenaProgress {
    /** True once the server confirmed YourStats at least once this session. */
    known: boolean;
    /** Confirmed + locally pending score (what the player should feel as "now"). */
    displayedScore: number;
    /** Locally earned since the last server confirmation. */
    pendingScore: number;
    /** Tier the displayed score falls into. */
    tier: RankTierDef;
    /** Next tier up, or null at the top of the ladder. */
    nextTier: RankTierDef | null;
    /** 0..1 fill of the current tier's segment toward the next threshold. */
    progressToNext: number;
    /** Positions climbed on the all-time board this session (positive = up). */
    allTimeDelta: number;
    /** Positions climbed on the weekly board this session (positive = up). */
    weeklyDelta: number;
}

const normalizeTier = (dto: RankTierDto): RankTierDef | null => {
    if (dto.Name.length === 0) return null;
    if (dto.Icon !== "rank1" && dto.Icon !== "rank2" && dto.Icon !== "rank3" && dto.Icon !== "rank4" && dto.Icon !== "rank5") return null;
    return { name: dto.Name, minScore: dto.MinScore, icon: dto.Icon };
};

// Bottom tier of the ladder — safe fallback while nothing is known.
const FALLBACK_TIER: RankTierDef = { name: "Blackout Survivor", minScore: 0, icon: "rank1" };

export const DEFAULT_ARENA_PROGRESS: ArenaProgress = {
    known: false,
    displayedScore: 0,
    pendingScore: 0,
    tier: FALLBACK_TIER,
    nextTier: null,
    progressToNext: 0,
    allTimeDelta: 0,
    weeklyDelta: 0,
};

export function useArenaProgress(): BindingState<ArenaProgress> {
    const confirmedRaw = useNumberBinding(arenaYourScore$, "arenaYourScore");
    const pendingRaw = useNumberBinding(arenaPendingScore$, "arenaPendingScore");
    const allTimeDeltaRaw = useNumberBinding(arenaAllTimeDelta$, "arenaAllTimeDelta");
    const weeklyDeltaRaw = useNumberBinding(arenaWeeklyDelta$, "arenaWeeklyDelta");
    const tierDtos = useValidatedJsonArray(arenaRankTiers$, isRankTierDto, { debugName: "arenaRankTiersProgress" });

    return useMemo(() => combineBindingStates({
        confirmedRaw,
        pendingRaw,
        allTimeDeltaRaw,
        weeklyDeltaRaw,
        tierDtos,
    }, (ready) => {
        const normalized = ready.tierDtos
            .map(normalizeTier)
            .filter((tier): tier is RankTierDef => tier !== null);
        const tiers = (normalized.length > 0 ? normalized : DEFAULT_RANK_TIERS)
            .slice()
            .sort((a, b) => a.minScore - b.minScore);

        const known = ready.confirmedRaw >= 0;
        const displayedScore = Math.max(ready.confirmedRaw, 0) + Math.max(ready.pendingRaw, 0);

        let tierIndex = 0;
        for (let i = 0; i < tiers.length; i++) {
            const candidate = tiers[i];
            if (candidate && displayedScore >= candidate.minScore) tierIndex = i;
        }
        const tier = tiers[tierIndex] ?? FALLBACK_TIER;
        const nextTier = tiers[tierIndex + 1] ?? null;
        const progressToNext = nextTier
            ? Math.min(1, Math.max(0,
                (displayedScore - tier.minScore) / Math.max(1, nextTier.minScore - tier.minScore)))
            : 1;

        return {
            known,
            displayedScore,
            pendingScore: Math.max(ready.pendingRaw, 0),
            tier,
            nextTier,
            progressToNext,
            allTimeDelta: ready.allTimeDeltaRaw,
            weeklyDelta: ready.weeklyDeltaRaw,
        };
    }), [confirmedRaw, pendingRaw, allTimeDeltaRaw, weeklyDeltaRaw, tierDtos]);
}
