/**
 * Hook for Arena Leaderboard state (PvE defense ladder).
 * Uses C# bindings for real server data.
 * Shows rank tiers with "VACANT" status when no player has achieved them.
 *
 * Rank tier names and score thresholds are config-driven (BalanceConfig
 * Arena.RankThresholds via the arenaRankTiers binding) — the UI treats tier
 * names as opaque strings and only validates the icon ids it can render.
 */

import { useMemo } from "react";
import { useNumberBinding, useValidatedJsonArray } from "../useSafeBinding";
import {
    arenaLeaderboard$,
    arenaLastRefreshResult$,
    arenaWeekly$,
    arenaRankTiers$,
    arenaYourPosition$,
    arenaYourWeeklyPosition$,
} from "../bindings";
import { combineBindingStates } from "../domain/combineBindingStates";
import { type BindingState, useDtoBinding } from "../domain/useDtoBinding";
import { isRequestResult, type RequestResult } from "../../types/dtoSubTypes";
import {
    isLeaderboardEntryDto,
    isRankTierDto,
    isWeeklyLeaderboardEntryDto,
    type LeaderboardEntryDto,
    type RankTierDto,
    type WeeklyLeaderboardEntryDto,
} from "../../types/domainDtos.generated";
import { leaderboardEntryId, weeklyLeaderboardEntryId } from "./useArenaLeaderboardIds";
import { isRankIconId, type LadderEntryBase, type LadderTier, type RankTierDef } from "./ladderTypes";

// ============ Types ============

export interface LeaderboardEntry extends LadderEntryBase {
    waves_survived: number;
    score: number;
    rank_tier: string;
}

export interface WeeklyLeaderboardEntry extends LadderEntryBase {
    waves_survived: number;
    score: number;
}

export type RankTier = LadderTier<LeaderboardEntry>;

export interface ArenaLeaderboardState {
    /** All-time top players */
    leaderboard: LeaderboardEntry[];
    /** This week's top players */
    weeklyLeaderboard: WeeklyLeaderboardEntry[];
    /** All rank tiers with current holders (or VACANT) */
    rankTiers: RankTier[];
    /** Your all-time position (-1 = not ranked) */
    yourPosition: number | null;
    /** Your weekly position (-1 = not ranked) */
    yourWeeklyPosition: number | null;
    /** Last manual refresh request result */
    lastRefreshResult: RequestResult;
}

const isValidRankTier = (dto: RankTierDto): boolean =>
    dto.Name.length > 0 && isRankIconId(dto.Icon);

const normalizeRankTierDef = (dto: RankTierDto): RankTierDef | null => {
    if (dto.Name.length === 0 || !isRankIconId(dto.Icon)) return null;
    return { name: dto.Name, minScore: dto.MinScore, icon: dto.Icon };
};

const normalizeLeaderboardEntry = (entry: LeaderboardEntryDto): LeaderboardEntry => ({
    id: leaderboardEntryId(entry),
    position: entry.Position,
    nickname: entry.Nickname,
    waves_survived: entry.WavesSurvived,
    score: entry.Score,
    rank_tier: entry.RankTier.length > 0 ? entry.RankTier : "Unranked",
});

const normalizeWeeklyEntry = (entry: WeeklyLeaderboardEntryDto): WeeklyLeaderboardEntry => ({
    id: weeklyLeaderboardEntryId(entry),
    position: entry.Position,
    nickname: entry.Nickname,
    waves_survived: entry.WavesSurvived,
    score: entry.Score,
});

// Mirrors the contract defaults (balance.contract.yaml, Arena.RankThresholds);
// used only until the arenaRankTiers binding delivers the live config.
export const DEFAULT_RANK_TIERS: RankTierDef[] = [
    { name: "Iron Mayor", minScore: 10000, icon: "rank5" },
    { name: "Grid Guardian", minScore: 2500, icon: "rank4" },
    { name: "City Keeper", minScore: 500, icon: "rank3" },
    { name: "District Warden", minScore: 100, icon: "rank2" },
    { name: "Blackout Survivor", minScore: 0, icon: "rank1" },
];

/**
 * Renderable empty state — identical to what {@link useArenaLeaderboard}
 * produces while no Arena bindings are live (all tiers VACANT). Used with
 * `bindingDataOrDefault` so a consumer never blanks on the loading tick.
 */
export const DEFAULT_ARENA_LEADERBOARD_STATE: ArenaLeaderboardState = {
    leaderboard: [],
    weeklyLeaderboard: [],
    rankTiers: DEFAULT_RANK_TIERS.map((tier) => ({
        name: tier.name,
        minScore: tier.minScore,
        icon: tier.icon,
        holder: null,
    })),
    yourPosition: null,
    yourWeeklyPosition: null,
    lastRefreshResult: {
        RequestId: 0,
        Status: "idle",
        ReasonId: "",
        CanonicalEcho: "",
        DiscriminatorKind: "none",
        DiscriminatorValue: "",
    },
};

// ============ Hook ============

export function useArenaLeaderboard(): BindingState<ArenaLeaderboardState> {
    const leaderboardRaw = useValidatedJsonArray(arenaLeaderboard$, isLeaderboardEntryDto, { debugName: "arenaLeaderboard" });
    const weeklyRaw = useValidatedJsonArray(arenaWeekly$, isWeeklyLeaderboardEntryDto, { debugName: "arenaWeekly" });
    const rankTierDefs = useValidatedJsonArray(arenaRankTiers$, (v): v is RankTierDto => isRankTierDto(v) && isValidRankTier(v), { debugName: "arenaRankTiers" });
    const lastRefreshResult = useDtoBinding(arenaLastRefreshResult$, isRequestResult, { debugName: "arenaLastRefreshResult" });
    const yourPosRaw = useNumberBinding(arenaYourPosition$, "arenaYourPosition");
    const yourWeeklyPosRaw = useNumberBinding(arenaYourWeeklyPosition$, "arenaYourWeeklyPosition");

    return useMemo(() => combineBindingStates({
        leaderboardRaw,
        weeklyRaw,
        rankTierDefs,
        lastRefreshResult,
        yourPosRaw,
        yourWeeklyPosRaw,
    }, (ready) => {
        const leaderboard = ready.leaderboardRaw.map(normalizeLeaderboardEntry);
        const normalizedTierDefs = ready.rankTierDefs
            .map(normalizeRankTierDef)
            .filter((tier): tier is RankTierDef => tier !== null);
        const tiers = normalizedTierDefs.length > 0 ? normalizedTierDefs : DEFAULT_RANK_TIERS;
        const yourPosition = ready.yourPosRaw >= 0 ? ready.yourPosRaw : null;
        const yourWeeklyPosition = ready.yourWeeklyPosRaw >= 0 ? ready.yourWeeklyPosRaw : null;

        // Holder = the best player whose ACTUAL rank is this tier (entries are
        // rank-labeled by the server from the same thresholds), VACANT otherwise —
        // not the old greedy "next best overall" spread that showed an Iron Mayor
        // as the District Warden holder.
        const sortedLeaderboard = [...leaderboard].sort((a, b) => b.score - a.score);
        const rankTiers = tiers.map((tier) => ({
            name: tier.name,
            minScore: tier.minScore,
            icon: tier.icon,
            holder: sortedLeaderboard.find((entry) => entry.rank_tier === tier.name) ?? null,
        }));

        return {
            leaderboard,
            weeklyLeaderboard: ready.weeklyRaw.map(normalizeWeeklyEntry),
            rankTiers,
            yourPosition,
            yourWeeklyPosition,
            lastRefreshResult: ready.lastRefreshResult,
        };
    }), [leaderboardRaw, weeklyRaw, rankTierDefs, lastRefreshResult, yourPosRaw, yourWeeklyPosRaw]);
}
