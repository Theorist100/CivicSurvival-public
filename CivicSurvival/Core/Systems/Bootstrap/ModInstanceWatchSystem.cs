using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Events;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.UI;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Core.Systems.Bootstrap
{
    /// <summary>
    /// Watches the mod's own install folder for deletion UNDER the live process.
    ///
    /// Why this can happen at all: the Paradox SDK's playset sync replaces the instance
    /// folder on a version update — download the new <c>&lt;modId&gt;_&lt;N+1&gt;</c>, then
    /// <c>ModsCache.DeleteModFromDisk</c> recursively deletes <c>&lt;modId&gt;_&lt;N&gt;</c> with no
    /// check for a running game (reproduced live 2026-07-19 on the private test listing;
    /// PdxSdk.log names the chain QueueRemoval → ProcessPendingRemovals → DeleteModFromDisk).
    /// The running session survives on in-memory state, but the boot-pinned paths inside
    /// already-registered vanilla assets keep pointing at the deleted folder: icons 404, and
    /// reloading a save rebuilds render batches whose SurfaceAsset.LoadProperties reads the
    /// .Surface files from disk on every rebuild by design (MeshSystem.GetMaterialIndex
    /// load→use→unload).
    ///
    /// What that costs, measured 2026-07-25 on the private test listing (a deletion taken
    /// deliberately, then the notice dismissed and the session continued): the poisoned load
    /// NREs inside ManagedBatchSystem.CreateBatch, vanilla catches and logs it, so the process
    /// SURVIVES — but the batch is never built. Every model whose batch was not already in
    /// memory then places INVISIBLE; only what was built BEFORE the deletion keeps working,
    /// its batch being reused (in the run: one AA piece placed 17 min earlier was the sole
    /// survivor, four other models failed three times each). Player.log meanwhile fills with
    /// "Read handle failed. VT may become unstable" — 17k lines in that one session. Restarting
    /// clears all of it. This is why the notice matters: the damage is silent and partial, not
    /// a crash the player would think to report.
    ///
    /// Response is deliberately non-invasive (decision 2026-07-19): no file restore, no SDK
    /// patch. One Error line with the facts for telemetry (attribution channel for the
    /// prod "prefab-absent at boot" reports — did a live-session deletion precede them?),
    /// and the restart notice on TWO channels: the vanilla MessageDialog (guaranteed —
    /// vanilla UI ships with the game install; see <see cref="UI.VanillaRestartNotice"/>)
    /// plus the styled ModUpdatedRestart React modal (may be dead in exactly this
    /// situation — its .mjs rides the deleted folder's activation). The residual risk —
    /// a player who ignores both notices and reloads a save still crashes — is accepted;
    /// every stronger option (folder copy, Harmony gate on the delete, AssetDatabase
    /// re-pointing) was rejected as invasive.
    ///
    /// Detection and delivery are separate. Detection runs from system creation onward —
    /// menu, city loading and paused gameplay included (<c>RequiresLoadedGame =&gt; false</c>):
    /// the SDK deletes the folder whatever the player is doing, and a watch that only sees
    /// loaded cities timestamps the deletion at the first city frame instead of the event.
    /// Delivery waits for a loaded city on purpose — nothing is shown in the menu, where
    /// vanilla owns its single dialog callback slot (see <see cref="OnGameplayReady"/>).
    /// Telemetry does not wait: the Error line ships through the Unity log hook the moment
    /// it is written, city or not.
    ///
    /// Cost envelope: one Directory.Exists per CHECK_INTERVAL_SECONDS (30s), main thread,
    /// Paradox installs only (a dev install's folder is rebuilt by every build — watching
    /// it would false-alarm constantly), plus three one-shot listings outside that throttle —
    /// the boot baseline count, and the sibling listing with the second count at the verdict.
    /// A HEALTHY SESSION PAYS NOTHING BEYOND THAT: everything else here sits on the deletion path,
    /// which is entered once per process and only after the folder is already gone — the SDK
    /// activation-row read and the sync-status read (both synchronous, no disk, no network), and one
    /// backend playset listing whose answer the follow-up waits for over at most
    /// FOLLOW_UP_MAX_CHECKS ticks that touch no disk at all (see <see cref="PumpFollowUp"/>).
    /// The DETECTION latches after the first hit and the FOLLOW-UP after its line; notice
    /// delivery does neither — the React modal request is re-issued after every coordinator
    /// reset (see <see cref="OnModalReset"/>), and only the vanilla dialog is once-per-process.
    /// </summary>
    [ActIndependent]
    public partial class ModInstanceWatchSystem : CivicSystemBase, IGameplayReadyListener
    {
        private static readonly LogContext Log = new("ModInstanceWatch");

        private const double CHECK_INTERVAL_SECONDS = 30.0;

        // The follow-up runs on its own, much shorter step, and NOT on CHECK_INTERVAL_SECONDS. The
        // deciding fact is player behaviour: in the prod deletions on record the session ended within
        // seconds of the event (one player quit 3s after the watchdog line), so a follow-up on the 30s
        // detection step would routinely never be written at all. These ticks are free to be frequent
        // because they touch no disk — they read one volatile string.
        private const double FOLLOW_UP_INTERVAL_SECONDS = 5.0;

        // How many of those ticks to wait for the backend listing before writing the line with
        // whatever arrived — six ≈ 30s total, well past a healthy answer (one GET plus a local-data
        // fill over the returned rows). The cap is what makes the line unconditional: an offline
        // backend leaves the probe "pending" forever, and a follow-up that waits for a perfect answer
        // would simply never be written.
        private const int FOLLOW_UP_MAX_CHECKS = 6;

        // Wall-clock throttle (game-speed independent; must keep counting while paused —
        // the SDK deletes folders regardless of simulation state).
        private readonly Stopwatch m_Clock = Stopwatch.StartNew();
        private double m_NextCheckSeconds = CHECK_INTERVAL_SECONDS;
#pragma warning disable CIVIC150 // Deliberately process-lifetime, NOT save state: the folder
        // deletion is a fact about this game PROCESS (its boot-pinned paths are dead), true for
        // every city loaded until the player restarts. CS2 reuses system instances across loads,
        // so the unserialized latches persisting through save/load are exactly the wanted
        // behavior — detect once, show the vanilla dialog once per process.
        // Two separate latches by design: the detection latch stops the disk probe, the delivery
        // latch stops the vanilla dialog. Sharing one field is what would kill the notice — the
        // early return below would swallow a detection that has not been delivered yet.
        private bool m_DetectionLatched;
        private bool m_VanillaNoticeShown;
        // Baseline for the "did we go alone or with company" discriminator, sampled once at
        // creation. Process-lifetime by the same argument as the latches: it describes the disk
        // this process booted from. -1 = never sampled (non-Paradox install or a throwing probe).
        private int m_ModIdsOnDiskAtBoot = -1;
        // Follow-up state, process-lifetime for the same reason: the deletion it reports on happened
        // to this PROCESS. The third latch stops the follow-up line after it has been written (or
        // given up on) — kept separate from the detection latch for the same reason the notice latch
        // is: one shared flag would silence a line that has not been written yet.
        private bool m_FollowUpDone;
        private int m_FollowUpChecks;
        // The sdkRow reading taken AT the deletion verdict, carried so the follow-up line can print
        // the PAIR. The pair is the point: a lone included=False cannot be told apart from a read
        // that landed inside the sync's playset rebuild (ModInstallResolver.DescribeSdkRowSafe), and
        // telemetry may deliver either line without the other, so each must be readable alone.
        private string m_SdkRowAtDetection = "not-sampled";
        // Distinct mod ids on disk counted AT the deletion, kept so the follow-up event reports the
        // same pair as the detection one. Re-counting there would walk the mod roots a second time
        // for a number that cannot have changed meaning — the deletion already happened.
        private int m_ModIdsOnDiskAtDetection = -1;
#pragma warning restore CIVIC150
        private bool m_IsParadoxInstall;

        // Which of the watchdog's two readings an event carries; mirrors the contract's Phase enum.
        private const string ModInstallRemovedPhaseDetected = "detected";
        private const string ModInstallRemovedPhaseSettled = "settled";

        // A deleted install folder is a fact about the process, not about a city: it happens
        // while the player sits in the menu just as readily as mid-game, and the game keeps
        // running either way. The inherited gameplay gate would sleep through all of it and
        // report the first city frame as the deletion time.
        protected override bool RequiresLoadedGame => false;

        protected override void OnCreate()
        {
            base.OnCreate();
            // StableModId is derived from the boot folder name ("147665" from
            // ".../pdx_mods/147665_30"); empty means a local dev install — nothing to watch.
            m_IsParadoxInstall = ModInstallResolver.StableModId().Length != 0;

            // Baseline half of the paired listing (the other half runs at the detection verdict).
            // A single count is useless — "14 mods on disk" says nothing without the number this
            // process started with. The pair answers the one question the sibling listing cannot:
            // did the sync remove us alone (our key did not match the server's playset listing) or
            // wipe a crowd (an incomplete backend answer taking everything absent from it with it).
            if (m_IsParadoxInstall)
                m_ModIdsOnDiskAtBoot = CountDistinctModIdsOnDisk();

            // Every gameplay load runs ModalCoordinator.Reset() (PostLoadValidationSystem),
            // which clears the active modal AND the pending queue — a one-shot request would be
            // erased by the very load the player is being warned about.
            SubscribeRequired<ModalResetEvent>(OnModalReset);
        }

        protected override void OnDestroy()
        {
            UnsubscribeSafe<ModalResetEvent>(OnModalReset);
            base.OnDestroy();
        }

        protected override void OnUpdateImpl()
        {
            // Detection and follow-up latches. Neither gates the notice: delivery lives in the two
            // handlers below, and the system deliberately stays enabled after a detection so its
            // lifecycle/EventBus wiring survives. Once BOTH the detection and its follow-up line are
            // done, a tick costs these two bool tests.
            if (!m_IsParadoxInstall || (m_DetectionLatched && m_FollowUpDone))
                return;

            double now = m_Clock.Elapsed.TotalSeconds;
            if (now < m_NextCheckSeconds)
                return;
            m_NextCheckSeconds = now + CHECK_INTERVAL_SECONDS;

            // Post-detection ticks do no disk work at all — they read one volatile string to see
            // whether the backend listing has landed. Same 30s throttle, a handful of times at most.
            if (m_DetectionLatched)
            {
                PumpFollowUp(now);
                return;
            }

            // PERF-LOCK: one Directory.Exists per 30s is this system's entire budget IN A HEALTHY
            // SESSION — do not add per-frame work above the throttle or wider disk scans below it.
            // Everything heavier is on the deletion path, which is entered once per process and only
            // after the folder is already gone: the boot baseline count in OnCreate, the sibling
            // listing plus the second count at the verdict, and the backend playset listing armed
            // there (a GET plus a local-data fill over the returned rows — deliberately the listing
            // and NOT Mods.List(), which sweeps every mod root and rebuilds the SDK's on-disk version
            // index; see StartPlaysetListingProbeAfterDeletion). The follow-up ticks that wait for its
            // answer read one volatile string and do no disk work at all. The window this
            // budget is spent in spans the whole process, menu and city loading included
            // (RequiresLoadedGame => false), so the absolute probe count per session is higher
            // than a city-only watch would produce; the per-30s rate is the invariant that
            // keeps that harmless, and it is what must not be relaxed.
            if (Directory.Exists(ModPaths.ModInstallDirectory))
                return;

            m_DetectionLatched = true;

            // Arm the backend listing FIRST, before anything else on this path spends time: it is the
            // one probe whose answer is asynchronous, and every millisecond of head start is a
            // millisecond it might land within the follow-up window. It answers the question that
            // splits the two roots of a no-replacement deletion — does the SERVER still list us in the
            // active playset — and it is armed into its own slot precisely so a prefab-failure verdict
            // earlier in the session cannot have spent it on a pre-deletion sample.
            ModInstallResolver.StartPlaysetListingProbeAfterDeletion();

            // Synchronous, verdict-time state of the SDK's own activation row, kept for the follow-up
            // line to print as a pair. Read the comment on DescribeSdkRowSafe before trusting a single
            // sample of this: included=False can be the sync's rebuild window rather than a lost row,
            // which is why syncOngoing rides next to it and why the follow-up samples it again.
            m_SdkRowAtDetection = ModInstallResolver.DescribeSdkRowSafe();

            // Nothing is shown to a player sitting in the menu — see OnGameplayReady for why.
            bool inCity = CivicGameLifecycle.IsGameplayReady;

            // Vanilla-channel notice FIRST when it may fire at all, so its outcome rides the
            // Error line below (telemetry forwards Error only — this is the prod-visible proof
            // the guaranteed channel fired). The React modal request further down stays as the
            // styled primary, but after a folder deletion the React layer may already be dead
            // (0 update_modal events across every α report in prod history) — vanilla UI
            // ships with the game install and survives any state of our folder. A player
            // with a live React layer sees both notices; accepted for a once-per-process
            // event whose miss costs a crash on save reload.
            //
            // vanillaNotice= therefore means "outcome of the guaranteed channel" when the
            // detection happens in a city, and the marker "deferred-until-city" when it happens
            // in the menu — in that case the outcome reaches prod on the second Error line that
            // OnGameplayReady writes at delivery. The fact is never dropped, only postponed.
            string vanillaNotice = inCity ? ShowVanillaNoticeOnce() : "deferred-until-city";

            // The facts prod needs for attribution. This line is an Error only while the cause is not
            // proven benign (see the level choice under it); the countable half always rides the
            // ModInstallRemovedEvent published just above, whatever the level ends up being:
            // when the folder vanished (session-relative) and whether a replacement instance
            // is already materialised next to it — "sibling with readable core .cok present"
            // separates a normal update-swap from a deletion with nothing left behind.
            //
            // modIdsOnDisk=<boot>-><now> closes the other split: alone or with company. Counted
            // by distinct mod id (the "<id>" of "<id>_<instance>"), NOT by folder — an ordinary
            // update of any other mod swaps one instance folder for another and would move a
            // folder count while the mod is still installed. "?" means the baseline was never
            // sampled (non-Paradox install, or the probe threw).
            //
            // sdkRow + syncOngoing answer what the disk cannot: was our ACTIVATION row still visible
            // when the files went. They are printed together because neither is readable alone —
            // included=False during a sync may be the rebuild window (DescribeSdkRowSafe), and
            // included=True with the folder gone also happens mid-update-re-extraction. The pair with
            // the follow-up line's second sample is what makes the reading safe.
            //
            // state= names what is proven — the folder is gone while this code runs — and deliberately
            // says nothing about models: in both prod deletions on record the models had loaded and
            // kept working. cause= is the separate axis, proven only by a newer sibling or an
            // unsubscribe event; at THIS point the backend listing has not landed yet, so a benign
            // removal the player made without unsubscribing still reads "unknown" here and is settled
            // in the follow-up line.
            string siblings = ModStateClassifier.DescribeSiblings(out bool newerSibling);
            string listingSource = ModInstallResolver.PlaysetListingAfterDeletionSource();
            string cause = ModStateClassifier.DescribeDeletionCause(newerSibling, listingSource);
            // One disk sweep feeding both the line and the event — this walks the mod roots.
            m_ModIdsOnDiskAtDetection = CountDistinctModIdsOnDisk();

            // The event carries this deletion whatever its cause, so the rate stays countable even
            // when the line below is not an Error. Published BEFORE the line: a proven-benign
            // deletion no longer travels through the error channel at all, and this is then the
            // only thing prod ever hears about it.
            PublishRemoved(ModInstallRemovedPhaseDetected, cause, listingSource, now, inCity, newerSibling);

            WriteDeletionReport(cause, siblings, vanillaNotice, now, inCity);

            if (inCity)
                RequestRestartModal();

            // Hand the tick over to the follow-up's own, shorter step. Leaving the 30s detection step
            // here would mean the first follow-up check lands half a minute after the deletion, which
            // is longer than the observed prod sessions survived it.
            m_NextCheckSeconds = m_Clock.Elapsed.TotalSeconds + FOLLOW_UP_INTERVAL_SECONDS;
        }

        /// <summary>
        /// The post-deletion follow-up: waits for the backend playset listing armed at the verdict,
        /// then writes ONE more Error line and stops. Runs on the same 30s throttle as the detection
        /// probe but touches no disk — it reads the probe's volatile answer and re-samples the two
        /// synchronous SDK facts.
        ///
        /// Why a second line rather than more fields on the first: the listing is a backend round trip
        /// and cannot be waited for inside the verdict (the main thread must not block, and the whole
        /// point of the deletion line is that it ships immediately). Why a second sdkRow sample: the
        /// sync rebuilds the playset object in two critical sections, so one reading cannot separate
        /// "our row is gone" from "we read during the rebuild" — across the pair, False→True says it
        /// was the rebuild. The listing's own source= field is the honest part: "backend" means the
        /// server still lists us while our files were removed anyway, "absent" means the removal
        /// followed the listing it is supposed to follow.
        ///
        /// Bounded by FOLLOW_UP_MAX_CHECKS so an offline backend (which leaves the probe "pending"
        /// forever) still gets the line out with "pending" in it, rather than silently never writing.
        /// </summary>
        private void PumpFollowUp(double now)
        {
            // Own step, replacing the detection throttle the caller just armed (see the constant).
            m_NextCheckSeconds = now + FOLLOW_UP_INTERVAL_SECONDS;

            m_FollowUpChecks++;
            string listing = ModInstallResolver.PlaysetListingAfterDeletionSafe();
            bool answered = !string.Equals(listing, "pending", System.StringComparison.Ordinal);
            if (!answered && m_FollowUpChecks < FOLLOW_UP_MAX_CHECKS)
                return;

            m_FollowUpDone = true;
            // The settled cause: by now the backend listing has had its chance, and an unsubscribe
            // event racing the deletion has had time to arrive. This is the reading to trust — the
            // verdict line's cause is the same call made before either could answer.
            // Re-listed rather than remembered from the verdict: a replacement instance can finish
            // materialising after the old folder went, and that late arrival is still an update swap.
            // One directory enumeration, once per process.
            _ = ModStateClassifier.DescribeSiblings(out bool newerSibling);
            string listingSource = ModInstallResolver.PlaysetListingAfterDeletionSource();
            string cause = ModStateClassifier.DescribeDeletionCause(newerSibling, listingSource);

            // The settled reading of the same deletion. Both phases are published because a session
            // ending inside the follow-up window leaves the detected one as the only record.
            PublishRemoved(ModInstallRemovedPhaseSettled, cause, listingSource,
                           now, CivicGameLifecycle.IsGameplayReady, newerSibling);

            string report = "Post-deletion follow-up on the inconsistent mod install — " +
                      $"state={ModStateClassifier.StateFilesGoneAssemblyLive}, cause={cause}, " +
                      $"playsetListing=[{listing}], subEvent={ModSubscriptionWatch.Describe()}, " +
                      $"sdkRowAtDetection=[{m_SdkRowAtDetection}], " +
                      $"sdkRowNow=[{ModInstallResolver.DescribeSdkRowSafe()}], " +
                      $"syncOngoing={ModInstallResolver.DescribeSyncOngoingSafe()}, " +
                      $"elapsedSinceBootSeconds={(long)now}, checks={m_FollowUpChecks}. " +
                      "cause=update-swap or cause=unsubscribed are PROVEN benign — the deletion was a " +
                      "version swap or the player's own removal, and only a restart is owed. " +
                      "cause=unlisted is the server not listing us: strong, not conclusive. " +
                      "cause=unknown with playsetListing source=backend is the malignant case — the " +
                      "server still listed us while our files were removed anyway. " +
                      "sdkRow False->True across the pair means the first sample fell inside the " +
                      "sync's playset rebuild, not that the activation row was ever lost.";

            // This is the cause to trust: the listing has had its chance and an unsubscribe event
            // racing the deletion has had time to arrive. A deletion that proves benign here leaves
            // the error channel for good — its count lives in the event published above. Anything
            // else keeps the Error line, which carries the half of the evidence the detection line
            // physically could not.
            if (ModStateClassifier.IsProvenBenignCause(cause))
                Log.Warn(report);
            else
                Log.Error(report);
        }

        /// <summary>
        /// The detection report, in its own method for the same reason the follow-up line has one:
        /// it is a once-per-process narrative, not update work. Both the text and the level decision
        /// live here so the message exists exactly once — the level cannot be chosen around a
        /// duplicated string without the two copies drifting apart.
        /// </summary>
        private void WriteDeletionReport(string cause, string siblings, string vanillaNotice,
                                         double now, bool inCity)
        {
            string report = "Mod install is inconsistent: folder deleted under the live process — " +
                      $"state={ModStateClassifier.StateFilesGoneAssemblyLive}, cause={cause}, " +
                      $"installDir={ModPaths.SanitizePathTail(ModPaths.ModInstallDirectory)}, " +
                      $"elapsedSinceBootSeconds={(long)now}, siblings=[{siblings}], " +
                      $"modIdsOnDisk={FormatCount(m_ModIdsOnDiskAtBoot)}->" +
                      $"{FormatCount(m_ModIdsOnDiskAtDetection)}, " +
                      $"sdkRow=[{m_SdkRowAtDetection}], syncOngoing={ModInstallResolver.DescribeSyncOngoingSafe()}, " +
                      $"subEvent={ModSubscriptionWatch.Describe()}, " +
                      $"vanillaNotice={vanillaNotice}, inCity={inCity}. " +
                      "Session keeps running from memory; save reload in this process would read " +
                      "surface assets from the deleted path. " +
                      (inCity
                          ? "ModUpdatedRestart modal requested."
                          : "Both notice channels deferred to the first city entry.") +
                      " A follow-up line carries the backend playset listing, the settled cause and a " +
                      "second sdkRow sample.";

            // Level by cause, not by the fact of a deletion. A version swap or the player's own
            // unsubscribe is not a failure — five sessions on 0.3.30 filled the error channel with
            // exactly that — so it drops to Warn and is counted by the event instead. The cause here
            // is still provisional (the backend listing has not answered), which is why the fallback
            // is Error: a deletion that has not PROVEN itself benign keeps the line that makes a
            // human look, and a short session may end before the follow-up settles the cause at all.
            if (ModStateClassifier.IsProvenBenignCause(cause))
                Log.Warn(report);
            else
                Log.Error(report);
        }

        /// <summary>
        /// Publish the deletion as a countable telemetry event
        /// (<c>diagnostics.mod_install_removed</c>). Emitted for EVERY deletion, benign or not,
        /// precisely because the log line's level now depends on the cause — without this the error
        /// channel could no longer answer how often deletions happen, and the benign majority (five
        /// sessions on 0.3.30, every one of them an unsubscribe) would vanish from prod entirely.
        /// The mod-id counts come from the detection sweep so both phases report the same pair.
        /// </summary>
        private void PublishRemoved(string phase, string cause, string listingSource,
                                    double elapsedSeconds, bool inCity, bool newerSibling)
            => EventBus?.SafePublish(new ModInstallRemovedEvent(
                phase,
                cause,
                listingSource,
                (int)elapsedSeconds,
                inCity,
                newerSibling,
                ModSubscriptionWatch.UnsubscribeSeen,
                m_ModIdsOnDiskAtBoot,
                m_ModIdsOnDiskAtDetection));

        /// <summary>
        /// Notice delivery anchor: the moment a gameplay session is ready, on every gameplay
        /// load. Chosen over the alternatives for its ordering — <c>PostLoadValidationSystem</c>
        /// runs <c>ModalCoordinator.Reset()</c> during the load and opens this gate only after
        /// the post-load pass, so a request placed here is the first one the cleared coordinator
        /// sees. <c>OnGameLoaded</c> would place it before that same Reset (erased), and
        /// <c>OnStartRunning</c> fires during the cold-boot menu, where nothing may be shown.
        /// Registration is automatic (<c>CivicSystemBase.OnCreate</c> /
        /// <c>OnDestroy</c> wire <see cref="IGameplayReadyListener"/>).
        ///
        /// A detection that happens while the player is already in a city does not wait for
        /// this callback — it delivers inline.
        /// </summary>
        public void OnGameplayReady()
        {
            if (!m_DetectionLatched)
                return;

            // Why both channels wait for a loaded city instead of firing in the menu: vanilla
            // keeps ONE dialog callback slot (AppBindings.ShowMessageDialog assigns
            // m_ConfirmationDialogCallback, no queue — decompile-verified), and the menu is
            // exactly where vanilla uses it (mod-load error dialogs, input-conflict dialog).
            // Showing over a live vanilla dialog replaces its body and eats its callback: the
            // player's answer arrives at our handler and vanilla's branch never runs. The React
            // modal is out for the same reason plus an unverified menu context. The player who
            // was hit in the menu learns one load later; the player who quits from the menu
            // never needed to know — without entering a city nothing breaks.
            if (!m_VanillaNoticeShown)
            {
                string vanillaNotice = ShowVanillaNoticeOnce();
                // Error, not Info: telemetry forwards Error lines only, and the detection line
                // of a menu-time deletion carries "deferred-until-city" in place of an outcome.
                // This line is where the guaranteed channel's result reaches prod. "attempted",
                // not "delivered": a failed show leaves the latch open on purpose, so this same
                // line can appear again at the next city entry — the outcome field says which.
                Log.Error("Deferred restart notice attempted on city entry after a menu-time " +
                          $"folder deletion — vanillaNotice={vanillaNotice}, " +
                          $"elapsedSinceBootSeconds={(long)m_Clock.Elapsed.TotalSeconds}. " +
                          "ModUpdatedRestart modal requested.");
            }

            RequestRestartModal();
        }

        /// <summary>
        /// Re-issues the modal request after <c>ModalCoordinator.Reset()</c>, which clears both
        /// the active modal and the pending queue and therefore drops ours.
        ///
        /// Deliberately never latched off. The available "delivered" signal —
        /// <c>ModalActivatedEvent</c> — fires when the coordinator takes the slot, not when the
        /// player sees anything, and after a folder deletion the React layer may be dead; a
        /// latch on it would re-open exactly the hole this closes. The event happens a handful
        /// of times per session and the reaction is one list insert, so re-requesting until the
        /// process ends costs nothing worth measuring.
        /// </summary>
        private void OnModalReset(ModalResetEvent evt)
        {
            if (!m_DetectionLatched)
                return;

            // A load-driven reset runs with the gameplay gate closed (MarkReloadPending → Reset
            // → post-load pass → MarkGameplayReady), and OnGameplayReady re-requests right after
            // it. So only a mid-session reset (intro scenario, crisis tutorial, countermeasures
            // serialization) needs answering here — and answering it in the menu is the one
            // thing this system must not do.
            if (!CivicGameLifecycle.IsGameplayReady)
                return;

            Log.Info("Modal coordinator reset while the restart notice was still owed — " +
                     "re-requesting ModUpdatedRestart.");
            RequestRestartModal();
        }

        /// <summary>
        /// Fires the guaranteed channel and closes the delivery latch — but ONLY on a confirmed
        /// show. Callers gate on <c>m_VanillaNoticeShown</c>: vanilla's dialog has a single
        /// callback slot and no queue, so a delivered notice is a once-per-process event, unlike
        /// the React modal request that is re-issued after every coordinator reset.
        ///
        /// The latch must mean "shown", not "attempted". <see cref="VanillaRestartNotice.Show"/>
        /// returns "no-ui" when the user interface is not up and "failed:&lt;Type&gt;" when the
        /// call threw, and in both cases nothing reached the player; latching there would close
        /// the only guaranteed channel in exactly the situation it exists for (folder deleted,
        /// React layer dead). Leaving it open costs one retry at the next delivery point — the
        /// first city entry, or the next coordinator reset.
        /// </summary>
        private string ShowVanillaNoticeOnce()
        {
            string outcome = VanillaRestartNotice.Show();
            if (string.Equals(outcome, "ok", System.StringComparison.Ordinal))
                m_VanillaNoticeShown = true;
            return outcome;
        }

        private static void RequestRestartModal()
        {
#pragma warning disable CIVIC098 // ModalCoordinator.Instance is static readonly = new(), never null
#pragma warning disable CIVIC239 // Best-effort surface: a busier slot queues the request inside the
            // coordinator (lower-priority requests pend until the slot frees), so false is not a
            // loss and there is nothing to retry here. The one way a request really disappears is
            // ModalCoordinator.Reset(), and that path comes back through OnModalReset /
            // OnGameplayReady.
            ModalCoordinator.Instance.TryShow("ModUpdatedRestart");
#pragma warning restore CIVIC239
#pragma warning restore CIVIC098
        }

        /// <summary>
        /// Counts DISTINCT mod ids installed next to us (<c>&lt;id&gt;</c> of
        /// <c>&lt;id&gt;_&lt;instance&gt;</c>), the paired half of the "alone or with company"
        /// discriminator. Distinct ids rather than folders: a version update of any mod replaces
        /// one instance folder with another, which moves a folder count without anything being
        /// uninstalled. Returns -1 when the parent is unreachable or the probe throws — the
        /// caller prints that as "?" rather than a number that would read as a real count.
        /// </summary>
        private static int CountDistinctModIdsOnDisk()
        {
            try
            {
                string? parent = Path.GetDirectoryName(ModPaths.ModInstallDirectory.TrimEnd('\\', '/'));
                if (parent == null || !Directory.Exists(parent))
                    return -1;

#pragma warning disable CIVIC050 // Two calls per process (boot baseline + detection verdict), both
                // outside the 30s throttle — not a per-frame allocation.
                var ids = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
#pragma warning restore CIVIC050
                foreach (string dir in Directory.GetDirectories(parent))
                {
                    string name = Path.GetFileName(dir);
                    int separator = name.IndexOf('_', System.StringComparison.Ordinal);
                    ids.Add(separator > 0 ? name.Substring(0, separator) : name);
                }
                return ids.Count;
            }
#pragma warning disable CIVIC052 // False positive: the failure IS reported — it becomes the "?"
            // in the modIdsOnDisk field of the deletion Error line (same fold-into-the-string
            // pattern as ModStateClassifier.DescribeSiblings).
            catch (System.Exception)
            {
                return -1;
            }
#pragma warning restore CIVIC052
        }

        /// <summary>Renders a count for the Error line; -1 (never sampled / probe failed) prints as "?".</summary>
        private static string FormatCount(int count)
            => count < 0 ? "?" : count.ToString(System.Globalization.CultureInfo.InvariantCulture);

    }
}
