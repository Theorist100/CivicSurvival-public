using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Domains.Network.Data;

namespace CivicSurvival.Domains.Network.Events
{
    /// <summary>
    /// Published when online stats are updated from server.
    /// UI shows "247 mayors online" indicator.
    /// </summary>
    public record OnlineStatsUpdatedEvent(OnlineStats Stats) : IGameEvent;

    /// <summary>
    /// Published when connection to global grid changes.
    /// UI updates connection status indicator.
    /// </summary>
    public record GlobalConnectionChangedEvent(bool IsConnected, string Message) : IGameEvent;

    /// <summary>
    /// Published on the main thread when server NEWS items are ready for Herald.
    /// Consumed by NewsFeedService, not by SocialFeedService.
    /// </summary>
    public record OfficialNewsReceivedEvent(NewsFeedPost Post) : IGameEvent;

    /// <summary>
    /// Command event to toggle telemetry/global connection.
    /// Published by UI when user clicks connect/disconnect.
    /// </summary>
    public record ToggleGlobalConnectionCommand(bool Enable) : IGameEvent;

    /// <summary>
    /// Published when the server reports the player's nickname change budget
    /// (after a successful set, or a read-only status fetch on connect).
    /// <c>ChangesRemaining</c> is the monthly budget left (max 3); -1 means unknown.
    /// <c>Initialized</c> is true once the player has ever set a nickname.
    /// <c>Nickname</c> is the server-held nickname (source of truth); only meaningful
    /// when <c>Initialized</c> is true (otherwise empty / a synthetic fallback).
    /// </summary>
    public record NicknameBudgetUpdatedEvent(int ChangesRemaining, bool Initialized, string Nickname) : IGameEvent;

    // FIX BUG-TEL-001: SetNicknameCommand moved to Core/Events/GameEvents.cs
    // to allow Services to consume without Domain imports.
    // Use: CivicSurvival.Core.Events.SetNicknameCommand
}
