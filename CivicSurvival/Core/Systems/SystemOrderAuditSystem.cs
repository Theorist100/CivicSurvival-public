using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Utils;
using Game;
using Unity.Entities;
using Unity.Mathematics;

namespace CivicSurvival.Core.Systems
{
    /// <summary>
    /// One-shot self-audit of the RESOLVED ECS update order.
    ///
    /// A short settle after load — once <c>UpdateSystem</c> has flattened all registrations
    /// via its lazy <c>Refresh()</c> — this reads the actual order and flags any VANILLA
    /// (<c>Game.*</c>) system scheduled in BOTH a Modification phase (its ECB-barrier home)
    /// AND a simulation phase (GameSimulation / Pre / PostSimulation). That combination is
    /// the exact signature of the "Trying to create EntityCommandBuffer when it's not
    /// allowed!" crash: e.g. <c>UpdateGroupSystem</c> running in GameSimulation, where the
    /// <c>AllowBarrier</c> that reopens <c>ModificationBarrier5</c>'s gate only runs in
    /// Modification — so <c>CreateCommandBuffer</c> throws every frame the producer has work.
    ///
    /// Complements <c>RegistrationValidator</c>'s registration-time self-check (which only
    /// sees our DIRECT RegisterAt calls): this reads the post-Refresh truth, so it also
    /// catches indirect placement (RefMap child resolution), no matter who or how.
    ///
    /// Ships in Release: one <c>[Info]</c> confirmation line on a clean boot (so the absence of
    /// an error proves the audit actually ran, not that it failed to start), one <c>[ERROR]</c>
    /// per mis-phased producer otherwise — so a real player crash finally carries its own
    /// attribution in the log. Disables its own tick after one read (zero recurring cost).
    /// The reflection lives in <see cref="UpdateSystemOrderReader"/> (a plain static helper,
    /// not a system) and is fully guarded: a Game.dll field rename makes the audit no-op,
    /// never destabilises the sim.
    /// </summary>
    [ActIndependent]
    public partial class SystemOrderAuditSystem : CivicSystemBase
    {
        private static readonly LogContext Log = new("OrderAudit");

        // Let registration settle (features register during load) before reading the order.
        // The order is built on the first Refresh and stable after load; ~1s of ticks is paranoia.
        private const int SETTLE_TICKS = 60;

        // Transient one-shot counter. Runs once per process: the resolved order is process-global
        // (RegisterAll runs once from Mod.OnLoad), so re-auditing on a second loaded city is unnecessary.
        [NonSerialized] private int m_Ticks;

        protected override void OnUpdateImpl()
        {
            if (m_Ticks++ < SETTLE_TICKS)
                return;

            UpdateSystemOrderReader.Audit(World, Log);

            // One-shot: zero recurring cost after the single read.
            Enabled = false;
        }
    }

    /// <summary>
    /// Reflection reader for <c>UpdateSystem</c>'s resolved order tables. Kept out of the
    /// system class so its process-lifetime reflection cache and one-shot allocations are not
    /// constrained by the system-class analyzers (CIVIC031/050/150). All access is guarded.
    /// </summary>
    internal static class UpdateSystemOrderReader
    {
        // Process-lifetime reflection handles (resolved once; null if the Game.dll layout changed).
        private static FieldInfo? s_UpdatesField;
        private static FieldInfo? s_SystemDataSystemField;
        private static FieldInfo? s_SystemDataPhaseField;
        private static bool s_Resolved;

        // Vanilla ModificationBarrier ECB producers that vanilla schedules ONLY in Modification
        // phases — so appearing in a simulation phase is the crash signature. Extend as new
        // ModificationBarrier producers are identified. Kept precise on purpose (see the loop).
        private static readonly string[] s_ModBarProducersWatch =
        {
            "Game.Simulation.UpdateGroupSystem",
        };

        // Watched producers whose mis-phasing is INERT for us — we no longer expose the trigger.
        // The crash needs a Deleted+UpdateFrame entity present during a simulation phase; CivicSurvival
        // routes citizen casualties through vanilla deathcare (HealthProblem → Deleted on
        // EndFrameBarrier/MainLoop, enforced by CIVIC522), so we expose no such entity in the sim loop,
        // and vanilla never exposes one there either. The mis-phasing is still DETECTED (an external
        // reorder mod placed it there; we cannot un-register that) but cannot crash via us — logged at
        // Warn (visible, but NOT captured by TelemetryCrashDetector → no alert). A watched producer NOT
        // in this set stays Error. If another mod exposes a sim-loop Deleted+UpdateFrame entity, the
        // real crash still surfaces independently as a vanilla CRITICAL.
        private static readonly string[] s_InertMisphasedProducers =
        {
            "Game.Simulation.UpdateGroupSystem",   // inert: casualties route through deathcare (CIVIC522)
        };

        internal static void Audit(World? world, LogContext log)
        {
            try
            {
                AuditInner(world, log);
            }
#pragma warning disable CIVIC052 // Diagnostic must never add a failure of its own
            catch (Exception ex)
            {
                log.Warn($"order audit skipped: {ex}");
            }
#pragma warning restore CIVIC052
        }

        private static void AuditInner(World? world, LogContext log)
        {
            UpdateSystem? updateSystem = world?.GetExistingSystemManaged<UpdateSystem>();
            if (updateSystem == null)
            {
                log.Warn("order audit: UpdateSystem not found — skipped");
                return;
            }

            if (!ResolveReflection())
            {
                log.Warn("order audit unavailable — UpdateSystem layout not recognised (Game.dll change?)");
                return;
            }

            if (s_UpdatesField!.GetValue(updateSystem) is not IList updates)
            {
                log.Warn("order audit: m_Updates not readable — skipped");
                return;
            }

            // type -> every phase it is scheduled in (resolved, post-Refresh).
            var phasesByType = new Dictionary<Type, HashSet<SystemUpdatePhase>>();
            foreach (object? entry in updates)
            {
                if (s_SystemDataSystemField!.GetValue(entry) is not ComponentSystemBase sys)
                    continue;
                if (s_SystemDataPhaseField!.GetValue(entry) is not SystemUpdatePhase phase)
                    continue;

                Type t = sys.GetType();
                if (!phasesByType.TryGetValue(t, out HashSet<SystemUpdatePhase>? set))
                {
                    set = new HashSet<SystemUpdatePhase>();
                    phasesByType[t] = set;
                }
                set.Add(phase);
            }

            int violations = 0;
            int inert = 0;
            foreach (KeyValuePair<Type, HashSet<SystemUpdatePhase>> kv in phasesByType)
            {
                // Precise watch-list, NOT a generic "in both phases" rule: vanilla legitimately
                // dual-phases many non-ECB systems (e.g. GameModeGovernmentSubsidiesSystem at
                // SystemOrder.cs:300 ModificationEnd + :490 GameSimulation — harmless, no ECB).
                // Only a known ModificationBarrier producer landing in a simulation phase crashes.
                string? fullName = kv.Key.FullName;
                if (fullName == null || Array.IndexOf(s_ModBarProducersWatch, fullName) < 0)
                    continue;

                bool inSim = false;
                foreach (SystemUpdatePhase p in kv.Value)
                {
                    if (IsSimulationPhase(p)) { inSim = true; break; }
                }
                if (!inSim)
                    continue;

                // Inert producer: an external reorder mod mis-phased it, but it cannot crash via us —
                // we expose no Deleted+UpdateFrame entity in the simulation loop (casualties route
                // through vanilla deathcare, CIVIC522) → Warn (no error.report alert), not a violation.
                // The placement is the mod's, not ours, and cannot be un-registered here.
                if (Array.IndexOf(s_InertMisphasedProducers, fullName) >= 0)
                {
                    inert++;
                    log.Warn(
                        $"[ORDER-AUDIT] Mis-phased vanilla ECB producer '{fullName}' present in a simulation phase " +
                        $"(phases: {string.Join(",", kv.Value)}) — an external reorder mod (e.g. RealisticPathFinding) " +
                        "registered it there. Inert: CivicSurvival exposes no sim-loop Deleted+UpdateFrame entity " +
                        "(casualties route through vanilla deathcare, CIVIC522), so it cannot crash via us.");
                    continue;
                }

                violations++;
                log.Error(
                    $"Mis-phased vanilla ECB producer: '{fullName}' is scheduled in a simulation phase " +
                    $"(phases: {string.Join(",", kv.Value)}). It uses a ModificationBarrier and throws " +
                    "'Trying to create EntityCommandBuffer when it's not allowed!' every frame it has work there — " +
                    "the AllowBarrier that reopens its gate runs only in Modification. Trace which RegisterAt/anchor placed it there.");
            }

            if (violations == 0 && inert == 0)
                log.Info("[ORDER-AUDIT] resolved order clean — no vanilla ECB producer in a simulation phase");
            else if (violations == 0)
                log.Info($"[ORDER-AUDIT] no crashing mis-phasing — {inert} producer(s) mis-phased by a reorder mod but inert (casualties route through deathcare, CIVIC522)");

#if CIVIC_DIAG
            DumpSimulationRanges(updateSystem, log);
#endif
        }

        private static bool IsSimulationPhase(SystemUpdatePhase p) =>
            p is SystemUpdatePhase.GameSimulation or SystemUpdatePhase.PreSimulation
              or SystemUpdatePhase.PostSimulation or SystemUpdatePhase.EditorSimulation;

        private static bool ResolveReflection()
        {
            if (s_Resolved)
                return s_UpdatesField != null && s_SystemDataSystemField != null && s_SystemDataPhaseField != null;

            s_Resolved = true;

            // Intentional reflection into vanilla scheduler internals (guarded; read-only).
#pragma warning disable S3011 // Accessibility bypass is intentional and guarded — diagnostic read of vanilla order tables
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            s_UpdatesField = typeof(UpdateSystem).GetField("m_Updates", flags);
            Type? sysDataType = typeof(UpdateSystem).GetNestedType("SystemData", BindingFlags.NonPublic);
            if (sysDataType != null)
            {
                s_SystemDataSystemField = sysDataType.GetField("m_System", flags);
                s_SystemDataPhaseField = sysDataType.GetField("m_Phase", flags);
            }
#pragma warning restore S3011

            return s_UpdatesField != null && s_SystemDataSystemField != null && s_SystemDataPhaseField != null;
        }

#if CIVIC_DIAG
        private static void DumpSimulationRanges(UpdateSystem updateSystem, LogContext log)
        {
#pragma warning disable S3011 // Diagnostic-only reflection (CIVIC_DIAG builds), guarded by the caller's try/catch
            FieldInfo? rangesField = typeof(UpdateSystem).GetField(
                "m_UpdateRanges", BindingFlags.Instance | BindingFlags.NonPublic);
#pragma warning restore S3011
            if (rangesField?.GetValue(updateSystem) is not IList ranges)
                return;
            if (s_UpdatesField!.GetValue(updateSystem) is not IList updates)
                return;

            foreach (SystemUpdatePhase phase in new[]
                { SystemUpdatePhase.PreSimulation, SystemUpdatePhase.GameSimulation, SystemUpdatePhase.PostSimulation })
            {
                int idx = (int)phase;
                if (idx < 0 || idx >= ranges.Count || ranges[idx] is not int2 range)
                    continue;

                var names = new List<string>();
                for (int i = range.x; i < range.y && i < updates.Count; i++)
                {
                    if (s_SystemDataSystemField!.GetValue(updates[i]) is ComponentSystemBase s)
                        names.Add(s.GetType().Name);
                }
                log.Info($"[ORDER-AUDIT][DIAG] {phase} ({names.Count}): {string.Join(", ", names)}");
            }
        }
#endif
    }
}
