using Colossal.Serialization.Entities;
using Unity.Entities;

namespace CivicSurvival.Core.Components.Threats
{
    /// <summary>
    /// Marks a mod-created danger-owner entity: the <c>Game.Events.Duration</c> carrier that every
    /// <c>Endanger</c> row of one wave points at (<c>Endanger.m_Event</c>).
    ///
    /// Why an owner entity exists at all: vanilla <c>InDangerSystem.IsStillInDanger</c> keeps an
    /// <c>InDanger</c> alive only while the referenced event entity still carries a
    /// <c>Duration</c> whose <c>m_EndFrame</c> is in the future (decompile
    /// <c>Game.Simulation/InDangerSystem.cs:64-76</c>). One shared owner per wave therefore acts as
    /// the single teardown switch for the whole wave's evacuation — when its Duration lapses,
    /// vanilla clears every <c>InDanger</c> the wave raised, on vanilla's own 64-frame schedule.
    /// No mod code removes <c>InDanger</c>.
    ///
    /// Why the owner must NOT carry <c>Game.Common.Event</c>: that marker means "one-shot command
    /// entity" — <c>PrepareCleanUpSystem</c> queries <c>Any{Deleted, Event}</c> and hands every
    /// match to <c>CleanUpSystem.DestroyEntity</c> in the same frame (decompile
    /// <c>Game.Common/PrepareCleanUpSystem.cs:21-28</c>). An owner tagged that way would die the
    /// frame it was born, leaving every <c>InDanger</c> pointing at a dead reference — vanilla would
    /// then drop the danger within one 64-frame interval and the evacuation would never happen.
    /// The short-lived <c>Endanger</c> rows DO carry <c>Game.Common.Event</c> on purpose: they are
    /// one-shot commands and that same cleanup is what disposes of them.
    ///
    /// Serialized as an empty payload: the owner is a pure identity + <c>Duration</c> carrier, and
    /// <c>InDanger</c> (vanilla-serialized, including its event reference) survives save/load — so
    /// the owner has to survive with it or a mid-attack save would drop the danger on load.
    /// <c>IEmptySerializable</c> is the framework marker for "intentionally no persisted payload";
    /// a plain <c>ISerializable</c> writing zero bytes throws in Colossal's serializer.
    /// </summary>
    public struct EvacuationDangerOwner : IComponentData, IEmptySerializable { }
}
