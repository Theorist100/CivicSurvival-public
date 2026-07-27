/**
 * Hook for the Prosperity Ladder (city quality-of-life board).
 *
 * Ranked by the summed per-wave prosperity index: vanilla average happiness
 * scaled by the housed share (1 − homeless/population), one index per survived
 * wave. Keeping the city livable for longer ranks higher than a one-off peak.
 * The server computes it from wave-snapshot telemetry; the client only
 * renders, plus a locally pending estimate that the next fetch absorbs.
 *
 * `known` flips true once the server confirms your stats (confirmed score is
 * -1 until the first YourStats fetch).
 */

import { useMemo } from "react";
import { useNumberBinding, useStringBinding, useValidatedJsonArray } from "../useSafeBinding";
import {
    arenaProsperityLeaderboard$,
    arenaProsperityPending$,
    arenaProsperityRankTier$,
    arenaProsperityRankTiers$,
    arenaProsperityScore$,
    arenaProsperityWeekly$,
    arenaProsperityYourPosition$,
    arenaProsperityYourWeeklyPosition$,
} from "../bindings";
import { combineBindingStates } from "../domain/combineBindingStates";
import { type BindingState } from "../domain/useDtoBinding";
import {
    isProsperityLeaderboardEntryDto,
    isRankTierDto,
    isWeeklyProsperityLeaderboardEntryDto,
    type ProsperityLeaderboardEntryDto,
    type RankTierDto,
    type WeeklyProsperityLeaderboardEntryDto,
} from "../../types/domainDtos.generated";
import { isRankIconId, type LadderEntryBase, type LadderTier, type RankTierDef } from "./ladderTypes";

// ============ Types ============

export interface ProsperityEntry extends LadderEntryBase {
    waves: number;
    avgIndex: number;
    score: number;
    rank_tier: string;
}

export interface WeeklyProsperityEntry extends LadderEntryBase {
    score: number;
}

export type ProsperityTier = LadderTier<ProsperityEntry>;

export interface ProsperityLeaderboardState {
    leaderboard: ProsperityEntry[];
    weeklyLeaderboard: WeeklyProsperityEntry[];
    rankTiers: ProsperityTier[];
    yourPosition: number | null;
    yourWeeklyPosition: number | null;
    /** True once the server confirmed your prosperity stats this session. */
    known: boolean;
    /** Confirmed score + locally pending per-wave index. */
    displayedScore: number;
    /** Index accumulated locally since the last confirmation. */
    pendingScore: number;
    /** Tier the displayed score falls into. */
    tier: RankTierDef;
    /** Next tier up, or null at the top. */
    nextTier: RankTierDef | null;
    /** 0..1 fill toward the next tier. */
    progressToNext: number;
}

const normalizeTier = (dto: RankTierDto): RankTierDef | null => {
    if (dto.Name.length === 0 || !isRankIconId(dto.Icon)) return null;
    return { name: dto.Name, minScore: dto.MinScore, icon: dto.Icon };
};

const prosperityEntryId = (e: ProsperityLeaderboardEntryDto): string =>
    `pr:${e.Position}:${e.Nickname}:${e.Score}`;
const weeklyProsperityEntryId = (e: WeeklyProsperityLeaderboardEntryDto): string =>
    `prw:${e.Position}:${e.Nickname}:${e.Score}`;

const normalizeEntry = (e: ProsperityLeaderboardEntryDto): ProsperityEntry => ({
    id: prosperityEntryId(e),
    position: e.Position,
    nickname: e.Nickname,
    waves: e.Waves,
    avgIndex: e.AvgIndex,
    score: e.Score,
    rank_tier: e.RankTier.length > 0 ? e.RankTier : "Unranked",
});

const normalizeWeeklyEntry = (e: WeeklyProsperityLeaderboardEntryDto): WeeklyProsperityEntry => ({
    id: weeklyProsperityEntryId(e),
    position: e.Position,
    nickname: e.Nickname,
    score: e.Score,
});

// Mirrors the contract defaults (balance.contract.yaml, Arena.ProsperityRankThresholds);
// used only until the binding delivers the live config.
const DEFAULT_PROSPERITY_TIERS: RankTierDef[] = [
    { name: "Mayor of the Unbroken City", minScore: 15000, icon: "rank5" },
    { name: "People's Mayor", minScore: 5000, icon: "rank4" },
    { name: "Crisis Manager", minScore: 1500, icon: "rank3" },
    { name: "Shelter Keeper", minScore: 300, icon: "rank2" },
    { name: "Ruin Warden", minScore: 0, icon: "rank1" },
];

const FALLBACK_TIER: RankTierDef = { name: "Ruin Warden", minScore: 0, icon: "rank1" };

export const DEFAULT_PROSPERITY_LEADERBOARD_STATE: ProsperityLeaderboardState = {
    leaderboard: [],
    weeklyLeaderboard: [],
    rankTiers: DEFAULT_PROSPERITY_TIERS.map((tier) => ({
        name: tier.name,
        minScore: tier.minScore,
        icon: tier.icon,
        holder: null,
    })),
    yourPosition: null,
    yourWeeklyPosition: null,
    known: false,
    displayedScore: 0,
    pendingScore: 0,
    tier: FALLBACK_TIER,
    nextTier: null,
    progressToNext: 0,
};

// ============ Hook ============

export function useProsperityLeaderboard(): BindingState<ProsperityLeaderboardState> {
    const leaderboardRaw = useValidatedJsonArray(arenaProsperityLeaderboard$, isProsperityLeaderboardEntryDto, { debugName: "arenaProsperityLeaderboard" });
    const weeklyRaw = useValidatedJsonArray(arenaProsperityWeekly$, isWeeklyProsperityLeaderboardEntryDto, { debugName: "arenaProsperityWeekly" });
    const tierDtos = useValidatedJsonArray(arenaProsperityRankTiers$, isRankTierDto, { debugName: "arenaProsperityRankTiers" });
    const yourPosRaw = useNumberBinding(arenaProsperityYourPosition$, "arenaProsperityYourPosition");
    const yourWeeklyPosRaw = useNumberBinding(arenaProsperityYourWeeklyPosition$, "arenaProsperityYourWeeklyPosition");
    const scoreRaw = useNumberBinding(arenaProsperityScore$, "arenaProsperityScore");
    const pendingRaw = useNumberBinding(arenaProsperityPending$, "arenaProsperityPending");
    const rankTierRaw = useStringBinding(arenaProsperityRankTier$, "arenaProsperityRankTier");

    return useMemo(() => combineBindingStates({
        leaderboardRaw,
        weeklyRaw,
        tierDtos,
        yourPosRaw,
        yourWeeklyPosRaw,
        scoreRaw,
        pendingRaw,
        rankTierRaw,
    }, (ready) => {
        const leaderboard = ready.leaderboardRaw.map(normalizeEntry);
        const normalized = ready.tierDtos
            .map(normalizeTier)
            .filter((tier): tier is RankTierDef => tier !== null);
        const tiers = normalized.length > 0 ? normalized : DEFAULT_PROSPERITY_TIERS;

        // Holder = best player whose actual rank is this tier, VACANT otherwise
        // (same semantics as the defense board).
        const sorted = [...leaderboard].sort((a, b) => b.score - a.score);
        const rankTiers = tiers.map((tier) => ({
            name: tier.name,
            minScore: tier.minScore,
            icon: tier.icon,
            holder: sorted.find((entry) => entry.rank_tier === tier.name) ?? null,
        }));

        const known = ready.scoreRaw >= 0;
        const displayedScore = Math.max(ready.scoreRaw, 0) + Math.max(ready.pendingRaw, 0);

        const ascending = [...tiers].sort((a, b) => a.minScore - b.minScore);
        let tierIndex = 0;
        for (let i = 0; i < ascending.length; i++) {
            const candidate = ascending[i];
            if (candidate && displayedScore >= candidate.minScore) tierIndex = i;
        }
        const tier = ascending[tierIndex] ?? FALLBACK_TIER;
        const nextTier = ascending[tierIndex + 1] ?? null;
        const progressToNext = nextTier
            ? Math.min(1, Math.max(0,
                (displayedScore - tier.minScore) / Math.max(1, nextTier.minScore - tier.minScore)))
            : 1;

        return {
            leaderboard,
            weeklyLeaderboard: ready.weeklyRaw.map(normalizeWeeklyEntry),
            rankTiers,
            yourPosition: ready.yourPosRaw >= 0 ? ready.yourPosRaw : null,
            yourWeeklyPosition: ready.yourWeeklyPosRaw >= 0 ? ready.yourWeeklyPosRaw : null,
            known,
            displayedScore,
            pendingScore: Math.max(ready.pendingRaw, 0),
            tier,
            nextTier,
            progressToNext,
        };
    }), [leaderboardRaw, weeklyRaw, tierDtos, yourPosRaw, yourWeeklyPosRaw, scoreRaw, pendingRaw, rankTierRaw]);
}
