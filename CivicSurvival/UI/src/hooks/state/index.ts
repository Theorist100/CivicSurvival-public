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
export { useArenaLeaderboard, type ArenaLeaderboardState, type LeaderboardEntry, type WeeklyLeaderboardEntry, type RankTier, type RankTierDef } from "./useArenaLeaderboard";
