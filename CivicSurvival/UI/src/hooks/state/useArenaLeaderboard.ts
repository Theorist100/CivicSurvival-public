/**
 * Hook for Arena Leaderboard state.
 * Uses C# bindings for real server data.
 * Shows rank tiers with "VACANT" status when no player has achieved them.
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

// ============ Types ============

export type RankIconId = "rank1" | "rank2" | "rank3" | "rank4" | "rank5";
export type RankTierName = "Entropy Lord" | "Chaos Broker" | "Grid Tycoon" | "System Operator" | "Blackout Survivor";

export interface LeaderboardEntry {
    id: string;
    position: number;
    nickname: string;
    floor_hits: number;
    total_damage: number;
    best_streak: number;
    rank_tier: RankTierName | "Unranked";
}

export interface WeeklyLeaderboardEntry {
    id: string;
    position: number;
    nickname: string;
    floor_hits: number;
    damage_dealt: number;
}

export interface RankTierDef {
    name: RankTierName;
    minFloorHits: number;
    icon: RankIconId;
}

export interface RankTier {
    name: RankTierName;
    minFloorHits: number;
    icon: RankIconId;
    holder: LeaderboardEntry | null;
}

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

const isRankTierName = (value: string): value is RankTierName =>
    value === "Entropy Lord"
    || value === "Chaos Broker"
    || value === "Grid Tycoon"
    || value === "System Operator"
    || value === "Blackout Survivor";

const isRankIconId = (value: string): value is RankIconId =>
    value === "rank1" || value === "rank2" || value === "rank3" || value === "rank4" || value === "rank5";

const isValidRankTier = (dto: RankTierDto): boolean =>
    isRankTierName(dto.Name) && isRankIconId(dto.Icon);

const normalizeRankTierDef = (dto: RankTierDto): RankTierDef | null => {
    if (!isRankTierName(dto.Name) || !isRankIconId(dto.Icon)) return null;
    return { name: dto.Name, minFloorHits: dto.MinFloorHits, icon: dto.Icon };
};

const normalizeLeaderboardEntry = (entry: LeaderboardEntryDto): LeaderboardEntry => ({
    id: leaderboardEntryId(entry),
    position: entry.Position,
    nickname: entry.Nickname,
    floor_hits: entry.FloorHits,
    total_damage: entry.TotalDamage,
    best_streak: entry.BestStreak,
    rank_tier: isRankTierName(entry.RankTier) ? entry.RankTier : "Unranked",
});

const normalizeWeeklyEntry = (entry: WeeklyLeaderboardEntryDto): WeeklyLeaderboardEntry => ({
    id: weeklyLeaderboardEntryId(entry),
    position: entry.Position,
    nickname: entry.Nickname,
    floor_hits: entry.FloorHits,
    damage_dealt: entry.DamageDealt,
});

const DEFAULT_RANK_TIERS: RankTierDef[] = [
    { name: "Entropy Lord", minFloorHits: 500, icon: "rank5" },
    { name: "Chaos Broker", minFloorHits: 100, icon: "rank4" },
    { name: "Grid Tycoon", minFloorHits: 25, icon: "rank3" },
    { name: "System Operator", minFloorHits: 5, icon: "rank2" },
    { name: "Blackout Survivor", minFloorHits: 0, icon: "rank1" },
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
        minFloorHits: tier.minFloorHits,
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

        const assignedEntryIds = new Set<string>();
        const sortedTiers = [...tiers].sort((a, b) => b.minFloorHits - a.minFloorHits);

        const tierResults = new Map<string, RankTier>();

        const sortedLeaderboard = [...leaderboard].sort((a, b) => b.floor_hits - a.floor_hits);

        for (const tier of sortedTiers) {
            const holder = sortedLeaderboard.find(
                (entry) => entry.floor_hits >= tier.minFloorHits
                    && !assignedEntryIds.has(entry.id)
            );
            if (holder) {
                assignedEntryIds.add(holder.id);
            }
            tierResults.set(tier.name, {
                name: tier.name,
                minFloorHits: tier.minFloorHits,
                icon: tier.icon,
                holder: holder || null,
            });
        }

        const rankTiers = tiers.map((tier) => tierResults.get(tier.name) ?? { name: tier.name, minFloorHits: tier.minFloorHits, icon: tier.icon, holder: null });

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
