using System.IO;
using System.Text;
using CivicSurvival.Core.Config;

namespace CivicSurvival.Core.Systems.Bootstrap
{
    /// <summary>
    /// Names what is actually wrong with the mod's INSTALLATION, and — separately — what caused it.
    /// One place, because the same question is asked from three unrelated verdicts (the folder
    /// watchdog, the prefab-absent finalize, the revoked-entities log) and three copies of the rule
    /// would drift apart within a session.
    ///
    /// WHY THIS EXISTS AT ALL (2026-07-26). Every report used to be phrased as "core mod prefab(s)
    /// absent" — a symptom — no matter what had really happened. In the prod cases behind it the models
    /// had loaded perfectly well and the actual event was that the install folder had been deleted out
    /// from under the running process; calling that "prefabs absent" put a symptom where a cause
    /// belongs and sent a month of investigation looking for a loader bug. The class below states only
    /// what the facts prove, and says "observing" when they prove nothing.
    ///
    /// The two axes are independent on purpose:
    ///   * STATE — what is inconsistent right now. Asserted from direct facts only (a directory that
    ///     is or is not there, a registry that does or does not hold our prefabs).
    ///   * CAUSE — why it got that way. Proven only by one-sided evidence: a newer sibling instance
    ///     (an update swapped us) or an unsubscribe event carrying our id (the player removed us).
    ///     Everything else is "unlisted" (the server does not list us — strong but not conclusive) or
    ///     plainly "unknown". A cause is never inferred from a state, and no cause currently changes
    ///     what gets logged or at which level — it is recorded so the decision can be made on data.
    /// </summary>
    internal static class ModStateClassifier
    {
        /// <summary>
        /// The install folder is gone while our assembly keeps running. Both halves are direct facts:
        /// the directory probe failed, and this code is executing. Says NOTHING about models — in the
        /// observed prod sessions they had already loaded and kept working from memory, while the
        /// boot-pinned paths inside registered vanilla assets went dead.
        /// </summary>
        public const string StateFilesGoneAssemblyLive = "inconsistent:files-gone-assembly-live";

        /// <summary>
        /// The files are on disk and our prefabs are still not in the registry — the only class where
        /// "the models did not load" is a true statement rather than a guess.
        /// </summary>
        public const string StateFilesPresentNotRegistered = "inconsistent:files-present-not-registered";

        /// <summary>
        /// The install folder is there but the core .cok inside it are not. Distinct from both
        /// neighbours: the folder was not removed (so it is not the deletion case) and nothing failed to
        /// register (there was nothing to register). This is the version-gated bucket — Paradox believes
        /// the mod is up to date and will not re-deliver files that went missing underneath it.
        /// </summary>
        public const string StateCokMissingInPlace = "inconsistent:cok-missing-in-place";

        /// <summary>Prefabs resolved earlier in the session and the entity was revoked afterwards.</summary>
        public const string StateRevokedAfterResolve = "inconsistent:revoked-after-resolve";

        /// <summary>
        /// A revoked verdict that UNDID itself: the entities were back on a later look. Not an
        /// inconsistency at all — the session recovered without anyone doing anything — and the
        /// prefix says so, so a query counting `state=inconsistent` never picks it up.
        ///
        /// It exists because the revoked verdict is taken in the first second after a city load
        /// (its clock restarts there), which is exactly when the active mod set may not be populated
        /// yet. Prod showed one such case recovering within 37s while others held for over half an
        /// hour, and nothing in the data distinguished them at verdict time. Rather than delay the
        /// verdict — which would lose the report entirely for players who quit right after, the very
        /// ones whose reports turned out to be false — the verdict ships immediately and this
        /// follow-up states what became of it.
        /// </summary>
        public const string StateRecoveredAfterRevoke = "recovered:after-revoke";

        /// <summary>
        /// Something moved (our row left the active set, a sync is running) but no class above is
        /// proven. Deliberately not a verdict: reporting a guess as a state is what the old wording did.
        /// </summary>
        public const string StateObserving = "observing";

        // ---- state ---------------------------------------------------------------------------

        /// <summary>
        /// Classifies a prefab-missing verdict into one of the three inconsistencies that can produce
        /// it. The directory is probed HERE, at verdict time, rather than taken from a cached boot fact:
        /// a folder deleted mid-session is exactly the case this split exists for, and it is invisible
        /// to anything sampled at startup.
        ///
        /// The boot-pinned path is what decides it, not the live one. Our assembly, its .cok and every
        /// registered vanilla asset point at the folder this process started from; a live path that
        /// resolves elsewhere means a replacement was materialised next to us, which does not make the
        /// running session any less broken.
        /// </summary>
        public static string ClassifyPrefabVerdict(bool coreFilesMissing)
        {
            if (!InstallDirPresent())
                return StateFilesGoneAssemblyLive;
            return coreFilesMissing ? StateCokMissingInPlace : StateFilesPresentNotRegistered;
        }

        /// <summary>
        /// Is the folder this process booted from still on disk. Failures answer "present": a probe
        /// that threw must not promote a verdict to the deletion class on no evidence.
        /// </summary>
        public static bool InstallDirPresent()
        {
            try
            {
                return Directory.Exists(ModPaths.ModInstallDirectory);
            }
#pragma warning disable CIVIC052 // False positive: the failure is folded into the answer, which rides
            // the caller's Error line; this runs inside the log pipeline, where the logger must not be
            // called (same contract as ModInstallResolver.DescribeActiveSetSafe).
            catch (System.Exception)
            {
                return true;
            }
#pragma warning restore CIVIC052
        }

        // ---- cause ---------------------------------------------------------------------------

        /// <summary>
        /// Why the install folder went away, on the evidence available at this instant. Ordered by
        /// strength — the two proofs first, then the strong-but-ambiguous signal, then "unknown".
        ///
        /// <paramref name="listingSource"/> is the backend listing probe's ATTRIBUTION as a value
        /// (<see cref="ModInstallResolver.PlaysetListingAfterDeletionSource"/>), never the rendered
        /// report string: a classification that parses a human-readable line changes meaning the day
        /// someone rewords the line. It is only consulted for the weakest verdict, so a "pending"
        /// answer costs nothing but that one distinction — the two proofs above it never touch the
        /// network at all.
        /// </summary>
        /// <summary>An update swapped our instance folder for a newer one — proven by the sibling.</summary>
        public const string CauseUpdateSwap = "update-swap";

        /// <summary>The player unsubscribed from this mod id — proven by the platform event.</summary>
        public const string CauseUnsubscribed = "unsubscribed";

        /// <summary>The server does not list us in the active playset. Strong, not conclusive.</summary>
        public const string CauseUnlisted = "unlisted";

        /// <summary>None of the evidence landed.</summary>
        public const string CauseUnknown = "unknown";

        /// <summary>
        /// Is the removal PROVEN benign — a version swap or the player's own unsubscribe? Only those
        /// two are proofs; <see cref="CauseUnlisted"/> is strong but not conclusive and
        /// <see cref="CauseUnknown"/> proves nothing, so both keep the Error line that makes a human
        /// look. This is the log-level decision: a proven-benign deletion is not a failure and must
        /// not spend the error channel, while still being counted — it travels as the
        /// <c>diagnostics.mod_install_removed</c> telemetry event, which is emitted for every
        /// deletion regardless of cause.
        /// </summary>
        public static bool IsProvenBenignCause(string cause)
            => string.Equals(cause, CauseUpdateSwap, System.StringComparison.Ordinal)
               || string.Equals(cause, CauseUnsubscribed, System.StringComparison.Ordinal);

        public static string DescribeDeletionCause(bool newerSiblingPresent, string listingSource)
        {
            // Proof 1: a newer instance folder of OUR mod with a readable core .cok is already
            // materialised. Paradox downloads the replacement before removing the old instance, so
            // this is an update swap and nothing is wrong with it beyond needing a restart.
            if (newerSiblingPresent)
                return CauseUpdateSwap;

            // Proof 2: the platform told us the player unsubscribed from THIS mod id. One-sided by
            // nature: the event rides an awaited details fetch and is not raised at all when that
            // fetch fails (offline), so its ABSENCE proves nothing — which is precisely why it is read
            // here as a proof of the benign case and never as evidence of the malignant one.
            if (ModSubscriptionWatch.UnsubscribeSeen)
                return CauseUnsubscribed;

            // Strong, not conclusive: the server does not list us in the active playset. That is the
            // input the removal path actually decides on, but it cannot separate "the player took us
            // out of the playset" from "the backend answered incompletely" — the suspected root.
            if (string.Equals(listingSource, "absent", System.StringComparison.Ordinal))
                return CauseUnlisted;

            return CauseUnknown;
        }

        // ---- sibling instances ----------------------------------------------------------------

        /// <summary>
        /// Lists sibling instances of our mod (<c>&lt;modId&gt;_*</c>) with a per-instance flag for a
        /// readable core .cok, and reports whether any of them is NEWER than the instance we booted
        /// from. One pass for both answers: the caller needs the human-readable list for the report and
        /// the boolean for the cause, and walking the directory twice for that would be silly.
        ///
        /// "Newer" is decided on the instance ordinal — the <c>NN</c> of <c>&lt;modId&gt;_NN</c>, which
        /// Paradox increments per re-install. A LOWER-numbered sibling is a leftover of a previous
        /// version, not a replacement, and treating it as one would certify a real removal as a
        /// harmless update — the whole reason this returns a comparison instead of a bare "any sibling
        /// exists" flag. An unparsable ordinal on either side yields false (unproven), never true.
        ///
        /// Exceptions fold into the returned string: it rides an Error line, and a throwing probe must
        /// not eat the line it decorates. In that case <paramref name="newerPresent"/> is false.
        /// </summary>
        public static string DescribeSiblings(out bool newerPresent)
        {
            newerPresent = false;
            try
            {
                string ourDir = ModPaths.ModInstallDirectory.TrimEnd('\\', '/');
                string? parent = Path.GetDirectoryName(ourDir);
                if (parent == null || !Directory.Exists(parent))
                    return "parent-absent";

                string modId = ModInstallResolver.StableModId();
                int ourOrdinal = ParseInstanceOrdinal(Path.GetFileName(ourDir));
#pragma warning disable CIVIC050 // One-shot at the deletion verdict (once per process) — not a
                // per-frame allocation.
                var sb = new StringBuilder(128);
#pragma warning restore CIVIC050
                foreach (string dir in Directory.GetDirectories(parent, modId + "_*"))
                {
                    string name = Path.GetFileName(dir);
                    bool cokPresent = CivicCokSelfLoader.CoreCokPresentIn(dir);
                    int ordinal = ParseInstanceOrdinal(name);
                    bool newer = cokPresent && ourOrdinal >= 0 && ordinal > ourOrdinal;
                    if (newer)
                        newerPresent = true;

                    if (sb.Length > 0)
                        sb.Append(", ");
                    sb.Append(name).Append(cokPresent ? "(cok=ok" : "(cok=missing");
                    sb.Append(newer ? ",newer)" : ")");
                }
                return sb.Length > 0 ? sb.ToString() : "none";
            }
#pragma warning disable CIVIC052 // False positive: the exception IS reported — folded into the
            // returned string, which rides the deletion Error line (same pattern as
            // ModInstallResolver.DescribeActiveSetSafe).
            catch (System.Exception e)
            {
                return $"probe-failed:{e.GetType().Name}";
            }
#pragma warning restore CIVIC052
        }

        /// <summary>
        /// The <c>NN</c> of a <c>&lt;modId&gt;_NN</c> instance folder name, or -1 when the name does not
        /// carry one. -1 propagates as "cannot compare" rather than as 0, which would make every
        /// sibling look newer.
        /// </summary>
        private static int ParseInstanceOrdinal(string folderName)
        {
            int separator = folderName.LastIndexOf('_');
            if (separator < 0 || separator == folderName.Length - 1)
                return -1;
            return int.TryParse(folderName.Substring(separator + 1),
                                System.Globalization.NumberStyles.None,
                                System.Globalization.CultureInfo.InvariantCulture,
                                out int ordinal)
                ? ordinal
                : -1;
        }
    }
}
