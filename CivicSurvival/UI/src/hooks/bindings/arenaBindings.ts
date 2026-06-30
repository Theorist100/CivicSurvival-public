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

/** Manual leaderboard refresh (C# ArenaUISystem handles) */
export function refreshArenaLeaderboard(): void {
    triggerCivic(B.RefreshArenaLeaderboard);
}

