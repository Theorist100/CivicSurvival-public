using System;

namespace CivicSurvival.Core.Types
{
    /// <summary>
    /// Bit-flag enumeration of structural mutations the mod can queue against a
    /// building inside a single simulation frame. Used by
    /// <see cref="CivicSurvival.Core.Interfaces.Services.IFrameMutationDedup"/>
    /// to coordinate cross-system ignite/destroy intents that would otherwise
    /// race through independent per-system <c>NativeHashSet&lt;Entity&gt;</c>
    /// guards (V_REGRESSION Pattern 8: H10 / M17 / M20).
    ///
    /// Flag semantics:
    /// <list type="bullet">
    ///   <item><see cref="None"/> — no mutation queued.</item>
    ///   <item><see cref="Destroy"/> — a <c>Game.Objects.Destroy</c> event has
    ///     been queued (via <c>BuildingDamageHelper.TryDestroyBuilding</c> or
    ///     equivalent path).</item>
    ///   <item><see cref="Ignite"/> — an OnFire / Ignite intent has been queued
    ///     (via <c>BuildingDamageHelper.TryApplyModFire</c>).</item>
    ///   <item><see cref="Both"/> = <see cref="Destroy"/> | <see cref="Ignite"/>
    ///     — both have been queued this frame on the same target.</item>
    /// </list>
    ///
    /// Use bitwise <c>&amp;</c> against either flag to test membership. Order of
    /// queue is not preserved; consult <see cref="IFrameMutationDedup"/> when
    /// the order of intents matters (Destroy short-circuits subsequent Ignite
    /// on the same target — see M17 fix in <c>BuildingDamageHelper</c>).
    /// </summary>
    [Flags]
    public enum FrameMutationKind : byte
    {
        None = 0,
        Destroy = 1 << 0,
        Ignite = 1 << 1,
        Both = Destroy | Ignite,
    }
}
