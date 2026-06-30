/**
 * Hook for Global News state.
 * Provides online stats and connection status for Herald section.
 *
 * FIX S8-02: Reads from newsState$ JSON binding (single registered binding)
 * instead of 6 individual bindings that were never registered in C#.
 */

import { useMemo } from "react";
import { newsState$ } from "../bindings/domainJsonBindings";
import { mapBindingState, useDtoBinding, type BindingState } from "../domain";
import { DEFAULT_NEWS_DTO, isNewsDto, type GlobalConnectionStatus, type NewsDto } from "../../types/domainDtos";
import type { RequestResult } from "../../types/dtoSubTypes";

// ============ Types ============

export interface GlobalNewsState {
    /** Players online right now */
    onlineNow: number;
    /** Players online in last hour */
    onlineHour: number;
    /** Players online today */
    onlineToday: number;
    /** Total registered players */
    totalPlayers: number;
    /** Whether connected to global grid */
    isConnected: boolean;
    /** Status message for display */
    connectionStatus: GlobalConnectionStatus;
    /** Whether global news polling is enabled */
    networkConnectionEnabled: boolean;
    /** Player nickname for leaderboard */
    playerNickname: string;
    /** Last nickname update result */
    nicknameRequest: RequestResult;
    /** Monthly nickname change budget left (max 3); first set is free */
    nicknameChangesRemaining: number;
    /** Whether the player has ever set a nickname (first set free, beyond budget) */
    nicknameInitialized: boolean;
    /** Whether an Online consent decision was ever recorded (gates first-enable modal) */
    onlineConsentRecorded: boolean;
}

// ============ Hook ============

function toGlobalNewsState(dto: NewsDto): GlobalNewsState {
    return {
        onlineNow: dto.GlobalOnlineNow ?? 0,
        onlineHour: dto.GlobalOnlineHour ?? 0,
        onlineToday: dto.GlobalOnlineToday ?? 0,
        totalPlayers: dto.GlobalOnlineTotal ?? 0,
        isConnected: dto.GlobalConnected ?? false,
        connectionStatus: dto.GlobalConnectionStatus ?? "Disconnected",
        networkConnectionEnabled: dto.NetworkConnectionEnabled ?? false,
        playerNickname: dto.PlayerNickname ?? "",
        nicknameRequest: dto.NicknameRequest,
        nicknameChangesRemaining: dto.NicknameChangesRemaining ?? 0,
        nicknameInitialized: dto.NicknameInitialized ?? false,
        onlineConsentRecorded: dto.OnlineConsentRecorded ?? false,
    };
}

/** Derived default — the shape the hook produces for the default NewsDto.
 *  Single source: computed via toGlobalNewsState, not hand-duplicated. */
export const DEFAULT_GLOBAL_NEWS_STATE: GlobalNewsState = toGlobalNewsState(DEFAULT_NEWS_DTO);

export function useGlobalNews(): BindingState<GlobalNewsState> {
    const dtoState = useDtoBinding(newsState$, isNewsDto, { debugName: "newsState", defaultValue: DEFAULT_NEWS_DTO });
    return useMemo(() => mapBindingState(dtoState, toGlobalNewsState), [dtoState]);
}
