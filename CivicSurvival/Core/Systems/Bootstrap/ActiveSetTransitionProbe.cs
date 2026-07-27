using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Colossal.Core;
using Colossal.IO.AssetDatabase;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Utils;
// 'Mod' unqualified resolves to CivicSurvival.Mod (the IMod) from the enclosing namespace, not the
// platform descriptor — alias both platform types (same convention as ModInstallResolver).
using PdxMod = Colossal.PSI.Common.Mod;
using PdxSdkPlatform = Colossal.PSI.PdxSdk.PdxSdkPlatform;

namespace CivicSurvival.Core.Systems.Bootstrap
{
    /// <summary>
    /// Two snapshots around vanilla's active-set recalculation: one when the platform announces the
    /// transition, one when the asset database has finished rebuilding the set. Answers the question no
    /// existing probe can — WHEN our mod leaves the active set, and whether it leaves alone.
    ///
    /// Why the existing probes cannot: <c>activeSetBoot</c> is a single answer at boot and then
    /// unregisters, <c>DescribeActiveSetSafe</c> is read at finalize time — 45s of blindness in between,
    /// and neither sees the moment of the drop.
    ///
    /// The two edges (decompile-verified, CS2 1.5.5):
    ///   "before" — <c>PdxSdkPlatform.onActivePlaysetChanged</c> (<c>:505</c>) /
    ///     <c>onModStatusChanged</c> (<c>:503</c>). The asset database learns of the change from the same
    ///     platform events (it subscribes in its own constructor, <c>ParadoxModsDataSource.cs:54-55</c>),
    ///     and its removal procedure then yields a frame (<c>:196</c>, <c>WaitXFrames</c>) and walks the
    ///     local cache before it touches anything — so our snapshot on the next main-thread tick still
    ///     sees the pre-removal set.
    ///   "after" — <c>ParadoxModsDataSource.onAfterActivePlaysetOrModStatusChanged</c> (declared
    ///     <c>:32</c>), raised at the end of the playset branch unconditionally (<c>:292</c>) and in the
    ///     mod-status branch when something actually changed (<c>:161</c>).
    ///
    /// The <c>activeMods=N</c> count in both snapshots is the discriminator the folder watchdog gives on
    /// disk and no probe gave for the set: a drop of one is an addressed removal of OUR row, a large drop
    /// is a playset-wide resync from an incomplete backend answer (not our bug).
    ///
    /// Read-only throughout: it reads the set through <see cref="ModInstallResolver"/> and asks the
    /// filesystem whether our install folder is there. It never touches the asset database — vanilla's own
    /// handler is walking those collections at that moment.
    ///
    /// Cost envelope: no polling. One dispatcher tick that does two volatile reads and returns while idle;
    /// the two set reads plus two <c>Directory.Exists</c> happen only on a real transition (units per
    /// session), and the single Error line is latched to one per process — a healthy session logs nothing.
    /// </summary>
    internal sealed class ActiveSetTransitionProbe
    {
        private static readonly LogContext Log = new("ModLoad.D");

        // One line per PROCESS (not per instance): a world reload builds a new CivicPrefabInitSystem and
        // therefore a new probe, and the same transition must not be reported twice. Flipped with
        // Interlocked so two worlds racing at a reload cannot both print.
        private static int s_Reported;

        // Attach poll: the SDK data source does not exist yet when Activate() runs (that is
        // Mod.OnLoad → CivicPrefabInitSystem.OnCreate), and there is no event for its creation — the boot
        // active-set probe polls for the same reason (ModInstallResolver: "SDK data source not up yet").
        // Gentle stride, and a cap: without the source the "after" edge can never arrive, so a probe that
        // has not attached by then can never complete a pair and stops ticking instead of polling forever.
        private const int ATTACH_TICK_STRIDE = 30;
        private const double ATTACH_CAP_SECONDS = 600.0;

        private readonly Stopwatch m_Clock = Stopwatch.StartNew();

        private PdxSdkPlatform? m_Pdx;
        private ParadoxModsDataSource? m_Ds;
        private Guid m_Updater;
        private int m_AttachTick;

        // Written by the platform / data-source handlers on an unknown thread, consumed by the dispatcher
        // tick on the main thread. Non-null = a snapshot of that edge is due; the string carries the event
        // tag and its arrival time, so one volatile field carries the whole handler payload (a double
        // timestamp field could tear across threads, a string reference cannot).
        private volatile string? m_BeforeDue;
        private volatile string? m_AfterDue;
        // Folded into the reported line: a handler that threw must not disappear silently, and it must not
        // call the logger from an unknown thread either (see the handler block below).
        private volatile string? m_HandlerFault;

        // A "before" whose "after" never arrives must not survive: vanilla's playset branch returns
        // before any dispatch when the platform hands it a null set, so the settled event simply does
        // not fire. Without an expiry that stale snapshot pairs with the NEXT unrelated settle —
        // minutes later, possibly from our own Populate() recovery — and the report reads like a real
        // pair while comparing two unconnected moments. Vanilla's own recalculation is seconds at
        // worst, so half a minute is far past any legitimate pairing.
        private const long BEFORE_TTL_MS = 30_000;

        // "before" snapshot — main thread only.
        private string? m_BeforeTag;
        private string? m_BeforeSet;
        private string? m_BeforeDir;
        private long m_BeforeAtMs;
        private bool m_BeforeLate;
        // Same snapshot as m_BeforeSet, kept as values because the report condition COMPARES samples:
        // string inequality counts a harmless change (someone else's mod enabled, the set growing) as
        // the event and burns the one report this probe gets per process.
        private int m_BeforeCount;
        private bool m_BeforeIdSeen;
        private bool m_BeforeFactsOk;

        /// <summary>
        /// Subscribe to both platform edges and start the main-thread tick that takes the snapshots.
        /// Called from <c>ParadoxNativeLoader.Activate</c> once the PdxSdk platform is known healthy
        /// (a non-Paradox launch has no playset to watch); self-guarded against a double arm.
        /// </summary>
        public void Arm(PdxSdkPlatform pdx)
        {
            if (m_Pdx != null)
                return;

            m_Pdx = pdx;
            pdx.onActivePlaysetChanged += OnPlatformActivePlaysetChanged;
            pdx.onModStatusChanged += OnPlatformModStatusChanged;
            m_Updater = MainThreadDispatcher.RegisterUpdater(Pump);
        }

        /// <summary>
        /// Detach everything: both platform handlers, the data-source handler, and the dispatcher updater.
        /// Called from <c>ParadoxNativeLoader.Deactivate</c> (the owner's OnDestroy). All three outlive our
        /// world — a permanently-false updater in particular survives an assembly unload, so its Guid is
        /// kept for exactly this. Safe to call unarmed and twice.
        /// </summary>
        public void Disarm()
        {
            if (m_Pdx != null)
            {
                m_Pdx.onActivePlaysetChanged -= OnPlatformActivePlaysetChanged;
                m_Pdx.onModStatusChanged -= OnPlatformModStatusChanged;
                m_Pdx = null;
            }
            if (m_Ds != null)
            {
                m_Ds.onAfterActivePlaysetOrModStatusChanged -= OnDataSourceSettled;
                m_Ds = null;
            }
            if (m_Updater != Guid.Empty)
            {
                MainThreadDispatcher.UnregisterUpdater(m_Updater);
                m_Updater = Guid.Empty;
            }
        }

        // ---- event handlers (unknown thread) -------------------------------------------------
        // The platform raises these from its own async chain (onModStatusChanged is published after an
        // awaited GetDetails, PdxSdkPlatform.cs:1480-1497), so NOTHING here reads game state.
        //
        // The reason is the thread WE are on, not the one vanilla is on: m_ActiveMods is a plain
        // unlocked Dictionary (ParadoxModsDataSource.cs:24), and vanilla always mutates it from the
        // MAIN thread — both branches wait for frames first (WaitXFrames(4) at :124 before the
        // removal at :149; WaitXFrames(1) at :196 before the one at :226). A read from this handler
        // therefore runs on the platform's thread against a dictionary the main thread may be
        // rewriting, and a concurrent read of one can spin or throw, not merely answer stale. Only a
        // tag plus a timestamp here — the values are sampled next tick on the main thread, where
        // vanilla cannot be mutating at the same moment.

        // Each handler swallows its own exception: one escaping here would break the multicast for every
        // subscriber queued after us — vanilla's own handlers included. The s_Reported check keeps a
        // reported process from marking further work. Marking is last-write-wins: several status events
        // can arrive back to back, and the one closest to the recalculation is the meaningful "before".
#pragma warning disable CIVIC052 // False positive: every exception IS reported — folded into this probe's
        // own Error line via m_HandlerFault. Calling the logger from these handlers would log from an
        // unknown thread inside a vanilla multicast, which is precisely what they must not do.
        private void OnPlatformActivePlaysetChanged()
        {
            try
            {
                if (s_Reported == 0)
                    m_BeforeDue = Stamp("activePlayset");
            }
            catch (Exception e)
            {
                m_HandlerFault = $"activePlayset:{e.GetType().Name}";
            }
        }

        private void OnPlatformModStatusChanged(PdxMod mod, bool enable)
        {
            try
            {
                if (s_Reported != 0)
                    return;
                // Read off the event's own payload struct, not off any shared collection: whose row moved
                // and in which direction is half the answer when the set size barely changes.
                string modId = ModInstallResolver.StableModId();
                bool ours = modId.Length != 0 && string.Equals(mod.id, modId, StringComparison.Ordinal);
                m_BeforeDue = Stamp($"modStatus(ours={ours}, enable={enable})");
            }
            catch (Exception e)
            {
                m_HandlerFault = $"modStatus:{e.GetType().Name}";
            }
        }

        private void OnDataSourceSettled()
        {
            try
            {
                if (s_Reported == 0)
                    m_AfterDue = Stamp("afterRecalc");
            }
            catch (Exception e)
            {
                m_HandlerFault = $"afterRecalc:{e.GetType().Name}";
            }
        }
#pragma warning restore CIVIC052

        private string Stamp(string tag) => $"{tag}@{m_Clock.ElapsedMilliseconds}ms";

        // ---- main-thread tick ----------------------------------------------------------------

        // Pumped every frame by MainThreadDispatcher (GameManager pumps it unconditionally, menu
        // included). Returns true only when the probe is finished — it is a permanent updater otherwise,
        // which is why Arm keeps its Guid for Disarm.
        private bool Pump()
        {
            try
            {
                // Until the "after" edge is attached a "before" edge is worthless (the pair can never
                // close), so edges are deliberately ignored while this is retrying — it resolves within
                // the first second of the process, long before any in-session recalculation.
                if (m_Ds == null && !TryAttachDataSource())
                    return m_Clock.Elapsed.TotalSeconds > ATTACH_CAP_SECONDS;

                // PERF-LOCK: the idle tick reads two volatile fields and returns — the snapshots are taken
                // ONLY on an edge the platform announced. Do not turn this into a poll of GetActiveMods /
                // the SDK: reading the active set per frame is tens of thousands of reads of vanilla's
                // unlocked dictionary per session, which is the design this probe deliberately replaced.
                // Expire a "before" that never got its pair, so it cannot be joined to an unrelated
                // settle later (see BEFORE_TTL_MS).
                if (m_BeforeSet != null && m_Clock.ElapsedMilliseconds - m_BeforeAtMs > BEFORE_TTL_MS)
                    ClearPair();

                string? before = m_BeforeDue;
                string? after = m_AfterDue;
                if (before == null && after == null)
                    return false;

                if (before != null)
                {
                    m_BeforeDue = null;
                    m_BeforeTag = before;
                    m_BeforeSet = ModInstallResolver.DescribeActiveSetSafe();
                    m_BeforeFactsOk = ReadFactsSafe(out m_BeforeCount, out m_BeforeIdSeen);
                    m_BeforeDir = DescribeInstallDir();
                    m_BeforeAtMs = m_Clock.ElapsedMilliseconds;
                    // Both edges landed in the same tick: vanilla finished the recalculation before we got
                    // the main thread, so this "before" is not actually before. Reported, not hidden.
                    m_BeforeLate = after != null;
                }

                if (after == null)
                    return false;
                m_AfterDue = null;

                // No paired "before": the recalculation came from a path that raises no platform event —
                // our own case-(a) recovery calls ds.Populate() directly, and that still ends in
                // onAfterActivePlaysetOrModStatusChanged. Nothing to compare against, so the one-shot line
                // is kept for a genuine pair.
                if (m_BeforeSet == null)
                    return false;

                string afterSet = ModInstallResolver.DescribeActiveSetSafe();
                string afterDir = DescribeInstallDir();
                bool afterFactsOk = ReadFactsSafe(out int afterCount, out bool afterIdSeen);

                // Report only a transition that goes the WRONG way: our row disappearing from the set,
                // or the set collapsing by more than one row. Anything else — the player enabling another
                // mod, the set growing, a reordering — is a legitimate recalculation, and reporting it
                // would spend the single per-process line on an event nobody is investigating, leaving the
                // real one unrecorded. Comparing the rendered strings (the first version of this check)
                // could not tell those apart: any difference read as "something changed".
                //
                // A drop of exactly one WHILE our row is still seen is that same legitimate category with
                // the sign flipped — some other mod was turned off. Both prod firings of this probe
                // (2026-07-25, the only two it has ever produced) were exactly that:
                // beforeEvt=modStatus(ours=False, enable=False), idSeen=True on both sides, dir=present.
                // One of them burned the per-process line 23 s before that same session's install folder
                // was deleted for real — the event this probe exists to catch. So a lone drop that leaves
                // us in the set is not reported; losing OUR row is, whatever the count does, and so is a
                // multi-row collapse (a playset-wide resync, which reaches us next).
                bool ourRowLost = m_BeforeIdSeen && !afterIdSeen;
                bool bulkResync = afterCount < m_BeforeCount - 1;
                bool worsened = m_BeforeFactsOk && afterFactsOk && (ourRowLost || bulkResync);
                if (!worsened)
                {
                    ClearPair();
                    return false; // keep watching — the interesting transition may still come
                }

                if (Interlocked.Exchange(ref s_Reported, 1) == 0)
                    Report(afterSet, afterDir, after);
                return true; // one line per process — nothing left to sample
            }
            catch (Exception e)
            {
                // Main thread, outside the log pipeline: logging here is safe (unlike the handlers above).
                // Error because a dead probe is invisible in prod otherwise, and stop ticking so a
                // recurring fault cannot repeat it every frame.
                Log.Error($"[ParadoxNative] active-set transition probe failed and stopped: {e}");
                return true;
            }
        }

        // Emits the whole finding as ONE Error line: prod telemetry forwards only LogType.Error, and this
        // probe's edges happen long before (and often without) any FinalizeMissing, so the Info channel
        // would never reach the server. Not a crash and not a verdict — a measurement.
        private void Report(string afterSet, string afterDir, string afterTag)
        {
            long spanMs = m_Clock.ElapsedMilliseconds - m_BeforeAtMs;
            string fault = m_HandlerFault is string f ? $", handlerFault={f}" : string.Empty;
            Log.Error("[ParadoxNative] active-set transition (diagnostic, not a failure): the playset/mod-status " +
                      "recalculation moved our membership or the set size. " +
                      $"beforeEvt={m_BeforeTag}, before=[{m_BeforeSet}, dir={m_BeforeDir}], " +
                      $"afterEvt={afterTag}, after=[{afterSet}, dir={afterDir}], " +
                      $"spanMs={spanMs}, beforeLate={m_BeforeLate}{fault}. " +
                      "Event stamps are ms since the probe armed (loader activation); spanMs is between the " +
                      "two snapshots. activeMods across them splits the roots: a drop of one is an addressed " +
                      "removal of our row, a large drop is a playset-wide resync that took us along.");
        }

        private void ClearPair()
        {
            m_BeforeTag = null;
            m_BeforeSet = null;
            m_BeforeDir = null;
            m_BeforeLate = false;
            m_BeforeCount = 0;
            m_BeforeIdSeen = false;
            m_BeforeFactsOk = false;
        }

        /// <summary>
        /// The active-set facts as values, for the before/after comparison. False means the sample
        /// could not be taken (no data source yet, or the read threw) — the caller then reports
        /// nothing, because an unread sample must not be mistaken for "the set shrank to zero".
        /// Main thread only: this enumerates vanilla's live key collection.
        /// </summary>
        private bool ReadFactsSafe(out int activeMods, out bool idSeen)
        {
            activeMods = 0;
            idSeen = false;
            ParadoxModsDataSource? ds = m_Ds;
            if (ds == null)
                return false;
            try
            {
                ModInstallResolver.ReadActiveSetFacts(ds, out activeMods, out idSeen, out _);
                return true;
            }
#pragma warning disable CIVIC052 // False positive: the failure IS reported — it lands in the probe's
            // own m_HandlerFault, which rides the reported Error line, and it suppresses the report
            // rather than being swallowed silently.
            catch (Exception e)
            {
                m_HandlerFault ??= $"facts:{e.GetType().Name}";
                return false;
            }
#pragma warning restore CIVIC052
        }

        // Attach to the "after" edge. The data source is created during asset-database init, after our
        // Activate() runs, so this is retried on a stride until it exists.
        private bool TryAttachDataSource()
        {
            // Counts up to the stride and resets, rather than growing for the life of the process:
            // an unbounded frame counter eventually wraps, and a negative value turns the modulo
            // form of this check into one that never fires again (CIVIC226).
            if (++m_AttachTick < ATTACH_TICK_STRIDE)
                return false;
            m_AttachTick = 0;
            if (AssetDatabase<ParadoxMods>.instance?.dataSource is not ParadoxModsDataSource ds)
                return false;

            m_Ds = ds;
            ds.onAfterActivePlaysetOrModStatusChanged += OnDataSourceSettled;
            return true;
        }

        // Is our install folder on disk at this instant, and which folder was asked about — the live
        // active-playset instance when the set still resolves one, else the boot-pinned path (which is
        // itself the answer when the set has just dropped us). Path tail is user-home stripped.
        private static string DescribeInstallDir()
        {
            string dir = ModInstallResolver.LiveInstallDirectoryOrBoot();
            return $"{(Directory.Exists(dir) ? "present" : "absent")}:{ModPaths.SanitizePathTail(dir)}";
        }
    }
}
