using CivicSurvival.Core.Serialization;
using Colossal.Serialization.Entities;
using CivicSurvival.Core.Features.CrossDomain.ThreatsAirDefense;
using Unity.Entities;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Core.Components.Threats
{
    internal static class BallisticInterceptStateLog
    {
        public static readonly LogContext Log = new("BallisticInterceptState");
    }

    /// <summary>
    /// BDS-owned intercept state for Ballistic missiles.
    /// Separated from Ballistic struct to prevent ECB full-struct stomp:
    /// BDS writes this via InterceptBarrier ECB; BallisticMovementJobEntity writes Ballistic (movement).
    ///
    /// Written by: BallisticDefenseSystem (IsIntercepted)
    /// Read by: BallisticMovementJobEntity, ThreatMovementSystem, ThreatTargetSystem, BallisticDefenseSystem
    /// </summary>
    public struct BallisticInterceptState : IComponentData, ISerializable
    {
        /// <summary>True if intercepted (rare, requires Patriot).</summary>
        public bool IsIntercepted;
        /// <summary>
        /// True if the per-wave leak floor let this missile through. Excluded from AA
        /// targeting but still impacts — distinct from IsIntercepted (which suppresses impact).
        /// </summary>
        public bool IsLeaked;
        /// <summary>
        /// True when this missile was intercepted by a Patriot SAM and is COASTING — decided dead
        /// (IsIntercepted=true, no damage, no re-target) but kept flying until the visible interceptor
        /// reaches it, then terminalized. Ballistic interception is always Patriot, so this is set
        /// unconditionally with IsIntercepted at fire success. Cleared by
        /// InterceptProcessingSystem.MarkLeaked on a leak-floor rollback. Render-only deferral — PvP-safe.
        /// </summary>
        public bool AwaitingInterceptorImpact;
        /// <summary>
        /// Leak-attribution funnel bit 1: at least once during descent a READY Patriot-capable
        /// AA (ammo, crew, off cooldown) had this missile inside its range — same semantics as
        /// <see cref="ShahedCombatState.WasCandidate"/>, whose pipeline is also readiness-gated.
        /// Set by BallisticDefenseSystem's range scan; telemetry-only, no gameplay reads.
        /// </summary>
        public bool WasCandidate;
        /// <summary>
        /// Leak-attribution funnel bit 2: at least one Patriot shot (hit or miss) was resolved
        /// against this missile. Set by TryInterceptBallistic; telemetry-only, no gameplay reads.
        /// </summary>
        public bool WasEngaged;

        private const byte SAVE_VERSION = 1;

        public void SetDefaults()
        {
            this = default;
        }

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SAVE_VERSION);
            try
            {
                KeyedSerializer.WriteBlockHeader(writer, 5);
                KeyedSerializer.WriteField(writer, "intc", IsIntercepted);
                KeyedSerializer.WriteField(writer, "leak", IsLeaked);
                KeyedSerializer.WriteField(writer, "await", AwaitingInterceptorImpact);
                KeyedSerializer.WriteField(writer, "cand", WasCandidate);
                KeyedSerializer.WriteField(writer, "eng", WasEngaged);
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SAVE_VERSION, out var version, out var block, nameof(BallisticInterceptState)))
            { SetDefaults(); return; }
            try
            {
                if (version >= 1)
                {
                    int fc = KeyedSerializer.ReadBlockFieldCount(reader);
                    for (int i = 0; i < fc; i++)
                    {
                        var tag = KeyedSerializer.ReadFieldHeader(reader, out var key);
                        switch (key)
                        {
                            case "intc": IsIntercepted = KeyedSerializer.ReadBool(reader, tag, "intc"); break;
                            case "leak": IsLeaked = KeyedSerializer.ReadBool(reader, tag, "leak"); break;
                            case "await": AwaitingInterceptorImpact = KeyedSerializer.ReadBool(reader, tag, "await"); break;
                            case "cand": WasCandidate = KeyedSerializer.ReadBool(reader, tag, "cand"); break;
                            case "eng": WasEngaged = KeyedSerializer.ReadBool(reader, tag, "eng"); break;
                            default: KeyedSerializer.Skip(reader, tag); break;
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                BallisticInterceptStateLog.Log.Error($"Deserialize {nameof(BallisticInterceptState)} failed: {ex}");
                SetDefaults();
            }
            finally { SerializationGuard.EndBlock(reader, block); }
        }
    }
}


