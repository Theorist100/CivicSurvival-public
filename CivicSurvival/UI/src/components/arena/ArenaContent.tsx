/**
 * ArenaContent - wave-aware swap wrapper for the ops/arena slot.
 *
 * While the ArenaUI rollout wave is unreached, no Arena bindings are
 * registered, so the real panel would render an empty dimmed table. Instead
 * render the existing deferred-feature teaser. Only when the wave is open
 * (bindings live) mount the binding-driven LeaderboardPanel.
 */

import React, { memo } from "react";
import { useBetaWave } from "@hooks/useBetaWave";
import { bindingDataOrDefault } from "@hooks/domain";
import {
    useArenaLeaderboard,
    DEFAULT_ARENA_LEADERBOARD_STATE,
} from "@hooks/state/useArenaLeaderboard";
import { DEFAULT_GLOBAL_NEWS_STATE, useGlobalNews } from "@hooks/state/useGlobalNews";
import { refreshArenaLeaderboard } from "@hooks/bindings/arenaBindings";
import { ArenaPreviewContent } from "../Dashboard/ContentPanel/views";
import { LeaderboardPanel } from "./LeaderboardPanel";

export const ArenaContent: React.FC = memo(() => {
    const { status } = useBetaWave("ArenaUI");

    const leaderboard = bindingDataOrDefault(useArenaLeaderboard(), DEFAULT_ARENA_LEADERBOARD_STATE);
    // Gate the leaderboard UI on Online, not diagnostics. After the Online/diagnostics
    // consolidation the leaderboard backend (ArenaLeaderboardSystem) is gated on
    // OnlineEnabled, not telemetry; gating the UI on TelemetryEnabled left a dead button
    // (a player with Online ON + diagnostics OFF saw the board "disabled" while the
    // backend was already serving it).
    const news = bindingDataOrDefault(useGlobalNews(), DEFAULT_GLOBAL_NEWS_STATE);

    if (status !== "open") {
        // preview | pending | unavailable — deferred feature: keep the teaser.
        return <ArenaPreviewContent />;
    }

    return (
        <LeaderboardPanel
            leaderboard={leaderboard}
            onRefreshLeaderboard={refreshArenaLeaderboard}
            onlineEnabled={news.networkConnectionEnabled}
            onlineConsentRecorded={news.onlineConsentRecorded}
        />
    );
});
ArenaContent.displayName = "ArenaContent";
