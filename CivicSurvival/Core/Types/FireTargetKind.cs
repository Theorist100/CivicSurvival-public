namespace CivicSurvival.Core.Types
{
    /// <summary>
    /// Which vanilla entity class a <c>ModFireIntent</c> targets, so the consumer
    /// (<c>ModFireApplySystem</c>) backs the <c>OnFire</c> with the matching vanilla
    /// fire-event prefab. A building fire uses the prefab whose
    /// <c>FireData.m_RandomTargetType == EventTargetType.Building</c>; a wild-tree fire
    /// uses the one with <c>EventTargetType.WildTree</c>, so vanilla
    /// <c>FireSimulationSystem</c>/<c>FireSpreadCheckJob</c> drive forest-fire spread with
    /// the tree prefab's <c>m_SpreadProbability</c>/<c>m_SpreadRange</c> (decompile-verified:
    /// spread is NOT gated by the natural-disasters setting — that gate lives only in
    /// <c>FireHazardSystem</c> spontaneous ignition).
    /// </summary>
    public enum FireTargetKind : byte
    {
        /// <summary>Vanilla building entity (the common mod-fire case).</summary>
        Building = 0,

        /// <summary>Wild tree (<c>Game.Objects.Tree</c> without <c>Owner</c>) — forest fire.</summary>
        WildTree = 1,
    }
}
