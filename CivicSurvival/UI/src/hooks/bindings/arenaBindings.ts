/**
 * Arena domain bindings - Leaderboards
 * Binds to ArenaUIPanel.cs
 */

import { bindCivicValue } from "../typedBinding.generated";
import { triggerCivic } from "@hooks/typedTrigger";
import { B } from "../bindingNames.generated";

// Leaderboard data (JSON arrays)
export const arenaLeaderboard$ = bindCivicValue(B.ArenaLeaderboard, "[]");
export const arenaWeekly$ = bindCivicValue(B.ArenaWeekly, "[]");
export const arenaRankTiers$ = bindCivicValue(B.ArenaRankTiers, "[]");
export const arenaLastRefreshResult$ = bindCivicValue(B.ArenaLastRefreshResult, "{\"RequestId\":0,\"Status\":\"idle\",\"ReasonId\":\"\",\"CanonicalEcho\":\"\",\"DiscriminatorKind\":\"none\",\"DiscriminatorValue\":\"\"}");

// Your positions
export const arenaYourPosition$ = bindCivicValue(B.ArenaYourPosition, -1);
export const arenaYourWeeklyPosition$ = bindCivicValue(B.ArenaYourWeeklyPosition, -1);

// Ladder progress (Duolingo-style feedback): server-confirmed score (-1 until
// the first YourStats fetch), locally pending score since that confirmation,
// server rank title, and session position movement per board.
export const arenaYourScore$ = bindCivicValue(B.ArenaYourScore, -1);
export const arenaPendingScore$ = bindCivicValue(B.ArenaPendingScore, 0);
export const arenaYourRankTier$ = bindCivicValue(B.ArenaYourRankTier, "");
export const arenaAllTimeDelta$ = bindCivicValue(B.ArenaAllTimeDelta, 0);
export const arenaWeeklyDelta$ = bindCivicValue(B.ArenaWeeklyDelta, 0);

// Shadow ladder (corruption board): ranked by net capital — income minus
// wallet confiscations. A non-empty rank tier means the server confirmed
// your stats (net itself can legitimately be negative).
export const arenaShadowLeaderboard$ = bindCivicValue(B.ArenaShadowLeaderboard, "[]");
export const arenaShadowWeekly$ = bindCivicValue(B.ArenaShadowWeekly, "[]");
export const arenaShadowRankTiers$ = bindCivicValue(B.ArenaShadowRankTiers, "[]");
export const arenaShadowYourPosition$ = bindCivicValue(B.ArenaShadowYourPosition, -1);
export const arenaShadowYourWeeklyPosition$ = bindCivicValue(B.ArenaShadowYourWeeklyPosition, -1);
export const arenaShadowNet$ = bindCivicValue(B.ArenaShadowNet, 0);
export const arenaShadowPending$ = bindCivicValue(B.ArenaShadowPending, 0);
export const arenaShadowRankTier$ = bindCivicValue(B.ArenaShadowRankTier, "");

// Prosperity ladder (city quality-of-life board): ranked by the per-wave
// prosperity index (average happiness scaled by the housed share) summed
// over waves. Score is -1 until the first YourStats fetch confirms it.
export const arenaProsperityLeaderboard$ = bindCivicValue(B.ArenaProsperityLeaderboard, "[]");
export const arenaProsperityWeekly$ = bindCivicValue(B.ArenaProsperityWeekly, "[]");
export const arenaProsperityRankTiers$ = bindCivicValue(B.ArenaProsperityRankTiers, "[]");
export const arenaProsperityYourPosition$ = bindCivicValue(B.ArenaProsperityYourPosition, -1);
export const arenaProsperityYourWeeklyPosition$ = bindCivicValue(B.ArenaProsperityYourWeeklyPosition, -1);
export const arenaProsperityScore$ = bindCivicValue(B.ArenaProsperityScore, -1);
export const arenaProsperityPending$ = bindCivicValue(B.ArenaProsperityPending, 0);
export const arenaProsperityRankTier$ = bindCivicValue(B.ArenaProsperityRankTier, "");

/** Manual leaderboard refresh (C# ArenaUISystem handles) */
export function refreshArenaLeaderboard(): void {
    triggerCivic(B.RefreshArenaLeaderboard);
}

