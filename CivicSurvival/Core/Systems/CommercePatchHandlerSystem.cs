using Colossal.Serialization.Entities;
using Game;
using Unity.Entities;
using CivicSurvival.Core.Attributes;

namespace CivicSurvival.Core.Systems
{
    /// <summary>
    /// Compatibility registration point for the commerce Harmony patch group.
    ///
    /// Demand-array scaling is intentionally owned by
    /// <c>CrisisEconomicsPatch.CommercePatch</c> getter postfixes using per-call
    /// <c>World.UpdateAllocator</c> copies. This system remains registered so
    /// existing framework/system ordering metadata stays stable, but it owns no
    /// NativeArray cache and exposes no arrays to vanilla async readers.
    /// </summary>
    [FrameworkSystem]
    [ActIndependent]
    public partial class CommercePatchHandlerSystem : GameSystemBase
    {
        protected override void OnCreate() => base.OnCreate();

        protected override void OnGamePreload(Purpose purpose, GameMode mode)
        {
            base.OnGamePreload(purpose, mode);
            _ = purpose;
            _ = mode;
        }

        protected override void OnUpdate()
        {
        }
    }
}
