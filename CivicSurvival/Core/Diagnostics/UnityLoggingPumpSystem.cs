#if ENABLE_BURST
using CivicSurvival.Core.Attributes;
using Game;
using Unity.Logging.Internal;

namespace CivicSurvival.Core.Diagnostics
{
    /// <summary>
    /// Keeps Unity.Logging's memory manager updated even when the host rebuilds
    /// PlayerLoop and drops Unity.Logging's default update delegate.
    /// </summary>
    [ActIndependent]
    [FrameworkSystem]
    public partial class UnityLoggingPumpSystem : GameSystemBase
    {
        protected override void OnUpdate()
        {
            // Housekeeping pump only — NOT the message-write path. BurstLogBootstrap uses
            // SyncMode.FullSync: each Log.* flushes straight to the file sink on the calling thread
            // (Unity.Logging LogController.DispatchMessage → FlushSync, decompile-verified 2026-06-13),
            // so the only work scheduled here is reclaiming payload memory (AfterUpdateLoggersJob),
            // which can always be deferred — a skipped/late frame never loses a [BURSTMARK] crash marker.
            //
            // Schedule async, never Complete on the main thread. ScheduleUpdateLoggers() already stores
            // its handle in LoggerManager.s_LastLogUpdate and CombineDependencies()-chains it into the
            // next frame's schedule (decompile-verified, LoggerManager.ScheduleUpdateLoggers:153-165),
            // so ordering is the framework's responsibility. A main-thread .Complete() here would be a
            // pure sync-point barrier that drains the worker queue (measured 1.5-6 ms/call) for zero
            // durability gain — that was the real cost behind this system topping PERF, not call
            // frequency, which is why throttling never removed it. Letting the housekeeping job run on
            // a worker and complete whenever is the async frame N → frame N+1 pattern (Axiom 9: no
            // blocking Complete). The handle is finally drained on shutdown via
            // LoggerManager.DeleteAllLoggers → CompleteUpdateLoggers. Stateless: no field to reset on
            // load (CIVIC367/CIVIC253 do not apply).
            LoggerManager.ScheduleUpdateLoggers();
        }
    }
}
#endif
