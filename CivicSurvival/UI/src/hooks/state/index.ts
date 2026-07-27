/**
 * State hooks - domain-specific reactive state
 *
 * Core domain hooks (useThreatState, useCognitiveWarfare, useMobilization,
 * usePowerState, useCorruptionState, useDonorState, useAttentionState)
 * removed — replaced by domain hooks in hooks/domain/.
 */

// Feature states
export { useDebugState, type DebugState, type HistoryPoint, type PowerHistoryData } from "./useDebugState";
export { useDebugData } from "./useDebugData";
export { useToastState, type ToastState } from "./useToastState";
export { useGlobalNews, type GlobalNewsState } from "./useGlobalNews";
export { useGlobalNewsData, type NewsCategory, type CategorizedPost } from "./useGlobalNewsData";
export { type LadderEntryBase, type LadderTier, type RankTierDef, type RankIconId } from "./ladderTypes";
export { useArenaLeaderboard, type ArenaLeaderboardState, type LeaderboardEntry, type WeeklyLeaderboardEntry, type RankTier } from "./useArenaLeaderboard";
export { useArenaProgress, DEFAULT_ARENA_PROGRESS, type ArenaProgress } from "./useArenaProgress";
export {
    useShadowLeaderboard,
    DEFAULT_SHADOW_LEADERBOARD_STATE,
    type ShadowLeaderboardState,
    type ShadowEntry,
    type WeeklyShadowEntry,
    type ShadowTier,
} from "./useShadowLeaderboard";
export {
    useProsperityLeaderboard,
    DEFAULT_PROSPERITY_LEADERBOARD_STATE,
    type ProsperityLeaderboardState,
    type ProsperityEntry,
    type WeeklyProsperityEntry,
    type ProsperityTier,
} from "./useProsperityLeaderboard";
