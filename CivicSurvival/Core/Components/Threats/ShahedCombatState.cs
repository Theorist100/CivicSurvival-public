using CivicSurvival.Core.Serialization;
using Colossal.Serialization.Entities;
using Unity.Entities;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Core.Components.Threats
{
    internal static class ShahedCombatStateLog
    {
        public static readonly LogContext Log = new("ShahedCombatState");
    }

    /// <summary>
    /// ADO-owned combat state for Shahed drones (intercept + evasion tracking).
    /// Separated from Shahed struct to prevent ECB full-struct stomp:
    /// ADO writes this via ECB SetComponent; TMS writes Shahed (movement) via ComponentLookup.
    /// Two writers never collide because they write different components.
    ///
    /// Written by: AirDefenseOrchestrator (MissedShotsCount, IsIntercepted)
    /// Read by: ThreatMovementSystem, AirDefenseOrchestrator, ThreatTargetSystem, ThreatRadarSystem
    /// </summary>
    public struct ShahedCombatState : IComponentData, ISerializable
    {
        /// <summary>True if shot down by air defense.</summary>
        public bool IsIntercepted;
        /// <summary>
        /// True if the per-wave leak floor let this drone through. Excluded from AA
        /// targeting (so defenses stop wasting ammo on an un-killable leaker) but still
        /// impacts normally — distinct from IsIntercepted, which suppresses the impact.
        /// </summary>
        public bool IsLeaked;
        /// <summary>
        /// True if this drone was spawned as part of a concentrated focus cluster. Such a
        /// coordinated strike saturates point defense — the per-wave leak floor lets the whole
        /// cluster through so it actually demolishes its target instead of being thinned to a
        /// single survivor by interception.
        /// </summary>
        public bool IsFocusStrike;
        /// <summary>
        /// True when this drone was intercepted by a Patriot SAM and is COASTING — decided dead
        /// (IsIntercepted=true, no damage, no re-target) but kept flying/rendering until the visible
        /// interceptor missile reaches it, at which point the existing terminalization runs
        /// (explosion + render delete). Set together with IsIntercepted at fire success for Patriot
        /// only; guns leave it false → instant freeze + explode as before. Cleared by
        /// InterceptProcessingSystem.MarkLeaked on a leak-floor rollback so a neutralized leaker
        /// resumes normal flight + damage. Render-only deferral — PvP-safe (kill decided in the
        /// firing frame by the formula).
        /// </summary>
        public bool AwaitingInterceptorImpact;
        /// <summary>Evasive maneuvers: each miss increases dodge chance.</summary>
        public int MissedShotsCount;

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
                KeyedSerializer.WriteField(writer, "foc", IsFocusStrike);
                KeyedSerializer.WriteField(writer, "await", AwaitingInterceptorImpact);
                KeyedSerializer.WriteField(writer, "miss", MissedShotsCount);
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SAVE_VERSION, out var version, out var block, nameof(ShahedCombatState)))
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
                            case "foc": IsFocusStrike = KeyedSerializer.ReadBool(reader, tag, "foc"); break;
                            case "await": AwaitingInterceptorImpact = KeyedSerializer.ReadBool(reader, tag, "await"); break;
                            case "miss": MissedShotsCount = KeyedSerializer.ReadBoundedInt(reader, tag, "miss", 0, 10000, 0); break;
                            default: KeyedSerializer.Skip(reader, tag); break;
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                ShahedCombatStateLog.Log.Error($"Deserialize {nameof(ShahedCombatState)} failed: {ex}");
                SetDefaults();
            }
            finally { SerializationGuard.EndBlock(reader, block); }
        }
    }
}


