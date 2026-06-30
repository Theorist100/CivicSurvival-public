// Debug-only diagnostic. Stripped from the Release build shipped to players
// (release-preflight.ps1 builds -c Release → DEBUG undefined), so the always-on
// Harmony finalizer on the hot SafeCommandBufferSystem.CreateCommandBuffer path
// exists only in dev/debug builds for now.
#if DEBUG
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using HarmonyLib;
using Game;
using Unity.Entities;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Patches
{
    /// <summary>
    /// Observes the one place vanilla throws "Trying to create EntityCommandBuffer when
    /// it's not allowed!" — <see cref="SafeCommandBufferSystem.CreateCommandBuffer"/> with
    /// the barrier's gate closed — and attributes it. Two real failure classes share this
    /// exact symptom, and one finalizer catches both:
    ///
    ///   • CLASS 1 (another mod): a mod registers a vanilla ModificationBarrier producer
    ///     (e.g. Game.Simulation.UpdateGroupSystem) into a non-Modification phase
    ///     (GameSimulation / Pre / PostSimulation) where AllowBarrier never reopens the
    ///     gate. The vanilla producer then throws every frame it has work — caught by
    ///     UpdateSystem.Update, logged [CRITICAL], and leaking the TempJob chunk list it
    ///     allocated before the throw (the JobTempAlloc warnings seen alongside it). NOT a
    ///     CivicSurvival bug; only the conflicting mod or its removal fixes it.
    ///
    ///   • CLASS 2 (our regression): our own RefMap AllowBarrier wiring (SystemRegistrar)
    ///     regresses so one of our barriers (GameSimulationEndBarrier / ThreatLifecycleBarrier
    ///     / ModCleanupBarrier) runs its producers with the gate closed. That IS our bug —
    ///     the precedent was PowerCapacityIndexSystem (2026-05-25).
    ///
    /// Behaviour is unchanged: the finalizer observes the exception and lets it propagate
    /// (void return → Harmony preserves __exception). Detection uses only the public
    /// <c>UpdateSystem.currentPhase</c> plus the exception's own stack — no reflection into
    /// vanilla's private registration tables, so a Game.dll field rename cannot break it.
    /// One log per (producer, barrier, phase) signature, for the process lifetime.
    /// </summary>
    // PERF: the finalizer fast-returns on the success path (__exception == null) — the only
    // work (stack walk + classify + log) runs on the rare gate-violation throw, never per frame.
    [HarmonyPatch(typeof(SafeCommandBufferSystem), nameof(SafeCommandBufferSystem.CreateCommandBuffer))]
    public static class BarrierGateViolationDetector
    {
        private const string PatchName = nameof(BarrierGateViolationDetector);
        private static readonly LogContext Log = new("BarrierGate");

        private const string GateClosedMarker = "not allowed";

        // One log per unique (producer, barrier, phase). Process-lifetime: the conflict is a
        // mod-environment property, not per-save, so it must not respam across reloads.
        // Main-thread-only — CreateCommandBuffer runs on the simulation main thread.
#pragma warning disable CIVIC207
        private static readonly HashSet<string> s_Seen = new();
#pragma warning restore CIVIC207

        [HarmonyPrepare]
        public static bool Prepare()
        {
            var method = AccessTools.Method(typeof(SafeCommandBufferSystem), nameof(SafeCommandBufferSystem.CreateCommandBuffer));
            if (method == null)
            {
                Log.Warn("SafeCommandBufferSystem.CreateCommandBuffer not found — gate-violation detector will not apply");
                return false;
            }
            return true;
        }

        [HarmonyFinalizer]
        public static void Finalizer(Exception __exception, SafeCommandBufferSystem __instance)
        {
            // Hot path: every successful CreateCommandBuffer call lands here with a null
            // exception. Return immediately — the throw branch below is the only work.
            if (__exception == null)
                return;

            try
            {
                if (__exception.Message == null
                    || __exception.Message.IndexOf(GateClosedMarker, StringComparison.Ordinal) < 0)
                    return;

                Observe(__exception, __instance);
            }
#pragma warning disable CIVIC052 // Diagnostic: must never add a failure of its own
            catch { /* never let the detector destabilise the sim */ }
#pragma warning restore CIVIC052
        }

        private static void Observe(Exception ex, SafeCommandBufferSystem barrier)
        {
            Type? barrierType = barrier?.GetType();
            Type? producerType = ResolveProducer(ex);
            SystemUpdatePhase phase = ResolvePhase(barrier);

            string producerName = producerType?.Name ?? "<unknown>";
            string barrierName = barrierType?.Name ?? "<unknown>";

            string signature = producerName + "/" + barrierName + "/" + phase;
            if (!s_Seen.Add(signature))
                return;

            bool ours = producerType?.Namespace != null
                && producerType.Namespace.StartsWith("CivicSurvival", StringComparison.Ordinal);

            if (ours)
            {
                Log.Error(
                    $"Barrier-gate regression: CivicSurvival producer '{producerName}' hit a CLOSED gate on " +
                    $"'{barrierName}' in phase '{phase}'. Our AllowBarrier<{barrierName}> RefMap ordering regressed " +
                    "— check SystemRegistrar barrier wiring (precedent: PowerCapacityIndexSystem 2026-05-25). " +
                    "This IS a CivicSurvival bug.");
            }
            else
            {
                Log.Warn(
                    $"Mod conflict: '{producerName}' created an ECB on '{barrierName}' in phase '{phase}' with the gate " +
                    $"CLOSED. Another mod registered the vanilla '{producerName}' outside its Modification phase. Expect " +
                    "caught '[CRITICAL] ... EntityCommandBuffer when it's not allowed!' + JobTempAlloc leaks during this " +
                    "phase. NOT a CivicSurvival bug — remove or report the conflicting mod.");
            }
        }

        // Resolve the system whose OnUpdate called CreateCommandBuffer, from the throw-site
        // stack carried by the exception. Skip barrier frames (SafeCommandBufferSystem and its
        // subclasses); the first other ComponentSystemBase-derived type is the producer.
        private static Type? ResolveProducer(Exception ex)
        {
            var frames = new StackTrace(ex, fNeedFileInfo: false).GetFrames();
            if (frames == null)
                return null;

            foreach (var frame in frames)
            {
                Type? declaring = frame.GetMethod()?.DeclaringType;
                if (declaring == null)
                    continue;
                if (typeof(SafeCommandBufferSystem).IsAssignableFrom(declaring))
                    continue; // barrier frame, not the producer
                if (typeof(ComponentSystemBase).IsAssignableFrom(declaring))
                    return declaring;
            }
            return null;
        }

        // currentPhase is a public getter on UpdateSystem — the stable signal for which phase
        // is executing. No reflection into private registration tables. UpdateSystem is the
        // foundational scheduler driving this very update loop, so it always exists here;
        // GetOrCreate is the mandatory-host resolve path (CIVIC468) and never creates anything.
        private static SystemUpdatePhase ResolvePhase(SafeCommandBufferSystem? barrier)
        {
            World? world = barrier?.World;
            if (world == null || !world.IsCreated)
                return SystemUpdatePhase.Invalid;

            return world.GetOrCreateSystemManaged<UpdateSystem>().currentPhase;
        }

        public static void Cleanup()
        {
            s_Seen.Clear();
        }

        public static void VerifyAndReport()
        {
            var target = AccessTools.Method(typeof(SafeCommandBufferSystem), nameof(SafeCommandBufferSystem.CreateCommandBuffer));
            if (target == null)
            {
                PatchStatusTracker.ReportFailure(PatchName, "SafeCommandBufferSystem.CreateCommandBuffer not found");
                return;
            }

            var info = Harmony.GetPatchInfo(target);
            bool present = info?.Finalizers != null
                && info.Finalizers.Any(p => p.owner == Mod.HARMONY_ID
                    && p.PatchMethod.DeclaringType == typeof(BarrierGateViolationDetector));

            if (present)
                PatchStatusTracker.ReportSuccess(PatchName);
            else
                PatchStatusTracker.ReportFailure(PatchName, "CreateCommandBuffer finalizer not present after PatchAll");
        }
    }
}
#endif
