/**
 * Hook for the Shadow Ladder (corruption board).
 *
 * Ranked by NET shadow capital: everything the schemes earned minus what
 * investigations confiscated — what the player stole and KEPT. The server
 * computes it from shadow-economy telemetry; the client only renders, plus a
 * locally pending income estimate that the next fetch absorbs.
 *
 * `known` flips true once the server confirms your stats (a non-empty rank
 * tier); net alone is no signal because it is legitimately negative for a
 * player whose wallet was seized.
 */

import { useMemo } from "react";
import { useNumberBinding, useStringBinding, useValidatedJsonArray } from "../useSafeBinding";
import {
    arenaShadowLeaderboard$,
    arenaShadowNet$,
    arenaShadowPending$,
    arenaShadowRankTier$,
    arenaShadowRankTiers$,
    arenaShadowWeekly$,
    arenaShadowYourPosition$,
    arenaShadowYourWeeklyPosition$,
} from "../bindings";
import { combineBindingStates } from "../domain/combineBindingStates";
import { type BindingState } from "../domain/useDtoBinding";
import {
    isRankTierDto,
    isShadowLeaderboardEntryDto,
    isWeeklyShadowLeaderboardEntryDto,
    type RankTierDto,
    type ShadowLeaderboardEntryDto,
    type WeeklyShadowLeaderboardEntryDto,
} from "../../types/domainDtos.generated";
import { isRankIconId, type LadderEntryBase, type LadderTier, type RankTierDef } from "./ladderTypes";

// ============ Types ============

export interface ShadowEntry extends LadderEntryBase {
    earned: number;
    confiscated: number;
    net: number;
    rank_tier: string;
}

export interface WeeklyShadowEntry extends LadderEntryBase {
    net: number;
}

export type ShadowTier = LadderTier<ShadowEntry>;

export interface ShadowLeaderboardState {
    leaderboard: ShadowEntry[];
    weeklyLeaderboard: WeeklyShadowEntry[];
    rankTiers: ShadowTier[];
    yourPosition: number | null;
    yourWeeklyPosition: number | null;
    /** True once the server confirmed your shadow stats this session. */
    known: boolean;
    /** Confirmed net + locally pending income. */
    displayedNet: number;
    /** Income earned locally since the last confirmation. */
    pendingNet: number;
    /** Tier the displayed net falls into. */
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

const shadowEntryId = (e: ShadowLeaderboardEntryDto): string =>
    `sh:${e.Position}:${e.Nickname}:${e.Net}`;
const weeklyShadowEntryId = (e: WeeklyShadowLeaderboardEntryDto): string =>
    `shw:${e.Position}:${e.Nickname}:${e.Net}`;

const normalizeEntry = (e: ShadowLeaderboardEntryDto): ShadowEntry => ({
    id: shadowEntryId(e),
    position: e.Position,
    nickname: e.Nickname,
    earned: e.Earned,
    confiscated: e.Confiscated,
    net: e.Net,
    rank_tier: e.RankTier.length > 0 ? e.RankTier : "Unranked",
});

const normalizeWeeklyEntry = (e: WeeklyShadowLeaderboardEntryDto): WeeklyShadowEntry => ({
    id: weeklyShadowEntryId(e),
    position: e.Position,
    nickname: e.Nickname,
    net: e.Net,
});

// Mirrors the contract defaults (balance.contract.yaml, Arena.ShadowRankThresholds);
// used only until the binding delivers the live config.
const DEFAULT_SHADOW_TIERS: RankTierDef[] = [
    { name: "Disaster Capitalist", minScore: 5000000, icon: "rank5" },
    { name: "Tender Baron", minScore: 1000000, icon: "rank4" },
    { name: "Fixer", minScore: 250000, icon: "rank3" },
    { name: "Fuel Siphoner", minScore: 50000, icon: "rank2" },
    { name: "Aid Skimmer", minScore: 0, icon: "rank1" },
];

const FALLBACK_TIER: RankTierDef = { name: "Aid Skimmer", minScore: 0, icon: "rank1" };

export const DEFAULT_SHADOW_LEADERBOARD_STATE: ShadowLeaderboardState = {
    leaderboard: [],
    weeklyLeaderboard: [],
    rankTiers: DEFAULT_SHADOW_TIERS.map((tier) => ({
        name: tier.name,
        minScore: tier.minScore,
        icon: tier.icon,
        holder: null,
    })),
    yourPosition: null,
    yourWeeklyPosition: null,
    known: false,
    displayedNet: 0,
    pendingNet: 0,
    tier: FALLBACK_TIER,
    nextTier: null,
    progressToNext: 0,
};

// ============ Hook ============

export function useShadowLeaderboard(): BindingState<ShadowLeaderboardState> {
    const leaderboardRaw = useValidatedJsonArray(arenaShadowLeaderboard$, isShadowLeaderboardEntryDto, { debugName: "arenaShadowLeaderboard" });
    const weeklyRaw = useValidatedJsonArray(arenaShadowWeekly$, isWeeklyShadowLeaderboardEntryDto, { debugName: "arenaShadowWeekly" });
    const tierDtos = useValidatedJsonArray(arenaShadowRankTiers$, isRankTierDto, { debugName: "arenaShadowRankTiers" });
    const yourPosRaw = useNumberBinding(arenaShadowYourPosition$, "arenaShadowYourPosition");
    const yourWeeklyPosRaw = useNumberBinding(arenaShadowYourWeeklyPosition$, "arenaShadowYourWeeklyPosition");
    const netRaw = useNumberBinding(arenaShadowNet$, "arenaShadowNet");
    const pendingRaw = useNumberBinding(arenaShadowPending$, "arenaShadowPending");
    const rankTierRaw = useStringBinding(arenaShadowRankTier$, "arenaShadowRankTier");

    return useMemo(() => combineBindingStates({
        leaderboardRaw,
        weeklyRaw,
        tierDtos,
        yourPosRaw,
        yourWeeklyPosRaw,
        netRaw,
        pendingRaw,
        rankTierRaw,
    }, (ready) => {
        const leaderboard = ready.leaderboardRaw.map(normalizeEntry);
        const normalized = ready.tierDtos
            .map(normalizeTier)
            .filter((tier): tier is RankTierDef => tier !== null);
        const tiers = normalized.length > 0 ? normalized : DEFAULT_SHADOW_TIERS;

        // Holder = best player whose actual rank is this tier, VACANT otherwise
        // (same semantics as the defense board).
        const sorted = [...leaderboard].sort((a, b) => b.net - a.net);
        const rankTiers = tiers.map((tier) => ({
            name: tier.name,
            minScore: tier.minScore,
            icon: tier.icon,
            holder: sorted.find((entry) => entry.rank_tier === tier.name) ?? null,
        }));

        const known = ready.rankTierRaw.length > 0;
        const displayedNet = ready.netRaw + Math.max(ready.pendingRaw, 0);

        const ascending = [...tiers].sort((a, b) => a.minScore - b.minScore);
        let tierIndex = 0;
        for (let i = 0; i < ascending.length; i++) {
            const candidate = ascending[i];
            if (candidate && displayedNet >= candidate.minScore) tierIndex = i;
        }
        const tier = ascending[tierIndex] ?? FALLBACK_TIER;
        const nextTier = ascending[tierIndex + 1] ?? null;
        const progressToNext = nextTier
            ? Math.min(1, Math.max(0,
                (displayedNet - tier.minScore) / Math.max(1, nextTier.minScore - tier.minScore)))
            : 1;

        return {
            leaderboard,
            weeklyLeaderboard: ready.weeklyRaw.map(normalizeWeeklyEntry),
            rankTiers,
            yourPosition: ready.yourPosRaw >= 0 ? ready.yourPosRaw : null,
            yourWeeklyPosition: ready.yourWeeklyPosRaw >= 0 ? ready.yourWeeklyPosRaw : null,
            known,
            displayedNet,
            pendingNet: Math.max(ready.pendingRaw, 0),
            tier,
            nextTier,
            progressToNext,
        };
    }), [leaderboardRaw, weeklyRaw, tierDtos, yourPosRaw, yourWeeklyPosRaw, netRaw, pendingRaw, rankTierRaw]);
}
