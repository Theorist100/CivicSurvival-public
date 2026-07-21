using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Utils;
using Game;
using Unity.Entities;

namespace CivicSurvival.Core.Systems.Scheduling
{
    /// <summary>
    /// Frame-end clear system for <see cref="IFrameMutationDedup"/>.
    ///
    /// Runs in PostSimulation before <c>ModCleanupBarrier</c> playback so every
    /// consumer has finished queueing intents for the current frame. Calls
    /// <see cref="IFrameMutationDedup.Clear"/> exactly once per frame so the
    /// next sim tick observes an empty dedup map.
    ///
    /// Ordering: anchored on
    /// the <c>ModCleanupBarrier</c> producer chain in <c>SystemRegistrar</c>.
    /// Gameplay systems queue intents during GameSimulation; this PostSimulation
    /// shim clears the frame-local map before the next gameplay tick observes it.
    ///
    /// State held: none. The dedup state itself lives in the
    /// <see cref="FrameMutationDedupService"/> resolved from
    /// <see cref="ServiceRegistry"/>; this system is a thin scheduling shim.
    /// </summary>
    [FrameworkSystem]
    public partial class FrameMutationDedupClearSystem : SystemBase
    {
        private static readonly LogContext Log = new("FrameMutationDedupClear");

        // CIVIC150 false positive: this field is a lazy reference cache for the
        // ServiceRegistry-published IFrameMutationDedup, not durable system state.
        // The service itself owns the (frame-local) NativeParallelHashMap and is
        // re-resolved on world load; no serialization needed here.
#pragma warning disable CIVIC150
        private IFrameMutationDedup? m_Dedup;
        private IThreatLifecycleDedup? m_ThreatLifecycleDedup;
#pragma warning restore CIVIC150

        protected override void OnCreate()
        {
            base.OnCreate();
            // Service resolved lazily on first update because Mod.OnLoad guarantees
            // ServiceRegistry registration before SystemRegistrar runs, but
            // OnCreate may execute in registration-order edge cases where another
            // system pulls us in via World.GetOrCreateSystemManaged. Lazy resolve
            // keeps OnCreate side-effect-free.
            Log.Info("Created");
        }

        protected override void OnUpdate()
        {
            m_Dedup ??= ServiceRegistry.Instance.Require<IFrameMutationDedup>();
            m_ThreatLifecycleDedup ??= ServiceRegistry.Instance.Require<IThreatLifecycleDedup>();
            m_Dedup.Clear();
            m_ThreatLifecycleDedup.Clear();
        }
    }
}
