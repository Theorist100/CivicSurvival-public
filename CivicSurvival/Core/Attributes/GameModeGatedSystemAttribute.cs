using System;
using Game;

namespace CivicSurvival.Core.Attributes
{
    /// <summary>
    /// Marks a system whose runtime lifecycle is gated on the vanilla
    /// <see cref="GameMode"/> value reported by <c>OnGamePreload</c>. The system
    /// starts disabled in <c>OnCreate</c> and re-enables only when the gated
    /// mode arrives. CIVIC476 allowlist exception: the OnCreate disable is the
    /// documented entry state for this lifecycle, not a dependency failure.
    /// </summary>
    /// <remarks>
    /// The attribute is documentation only at the C# level: the system is still
    /// responsible for re-enabling itself in <c>OnGamePreload</c>. The analyzer
    /// uses the attribute to opt the class out of CIVIC476.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class GameModeGatedSystemAttribute : Attribute
    {
        public GameMode TargetMode { get; }
        public string Reason { get; }

        public GameModeGatedSystemAttribute(GameMode targetMode, string reason)
        {
            TargetMode = targetMode;
            Reason = reason ?? throw new ArgumentNullException(nameof(reason));
        }
    }
}
