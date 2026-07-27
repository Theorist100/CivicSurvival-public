/**
 * ArenaContent - wave-aware swap wrapper for the ops/arena slot.
 *
 * While the ArenaUI rollout wave is unreached, no Arena bindings are
 * registered, so the real panel would render an empty dimmed table. Instead
 * render a plain locked note (the old "Coming Soon War Room" teaser is gone —
 * the War Room ships as its own dashboard tab now). Only when the wave is open
 * (bindings live) mount the binding-driven LeaderboardPanel.
 */

import React, { memo } from "react";
import { useTheme } from "@themes";
import { useLocale } from "@locales";
import { useBetaWave } from "@hooks/useBetaWave";
import { bindingDataOrDefault } from "@hooks/domain";
import {
    useArenaLeaderboard,
    DEFAULT_ARENA_LEADERBOARD_STATE,
} from "@hooks/state/useArenaLeaderboard";
import { DEFAULT_GLOBAL_NEWS_STATE, useGlobalNews } from "@hooks/state/useGlobalNews";
import { refreshArenaLeaderboard } from "@hooks/bindings/arenaBindings";
import { LeaderboardPanel } from "./LeaderboardPanel";

const ArenaLockedNote: React.FC = memo(() => {
    const theme = useTheme();
    const l = useLocale();
    return (
        <div style={{
            display: "flex",
            alignItems: "center",
            justifyContent: "center",
            height: "100%",
            padding: theme.spacing.lg,
            fontSize: "12rem",
            textTransform: "uppercase" as const,
            letterSpacing: "1rem",
            color: theme.colors.textMuted,
            fontFamily: theme.typography.fontFamilyMono,
            textAlign: "center" as const,
        }}>
            {l.t("UI_ARENA_WAVE_LOCKED")}
        </div>
    );
});
ArenaLockedNote.displayName = "ArenaLockedNote";

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
        // preview | pending | unavailable — brief manifest-load flash or a build
        // whose rollout wave has not reached ArenaUI yet.
        return <ArenaLockedNote />;
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
