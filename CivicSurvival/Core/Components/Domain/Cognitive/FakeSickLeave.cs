using CivicSurvival.Core.Serialization;
using CivicSurvival.Core.Utils;
using Colossal.Serialization.Entities;
using Unity.Entities;

namespace CivicSurvival.Core.Components.Domain.Cognitive
{
    internal static class FakeSickLeaveLog
    {
        public static readonly LogContext Log = new("FakeSickLeave");
    }

    /// <summary>
    /// Marker on a vanilla Citizen entity: this citizen is on FAKE sick leave — a draft-fear
    /// reaction to a landed enemy FakeVideo raid, not a real disease. The wave writer
    /// (<c>SickLeaveWaveSystem</c>) pairs this marker with a vanilla
    /// <c>HealthProblem(Sick|NoHealthcare)</c>: the citizen stays home (vanilla
    /// <c>GoToWorkJob</c> excludes <c>HealthProblem</c>) and counts into the workplace's
    /// <c>SickEmployees</c> efficiency factor, while <c>NoHealthcare</c> keeps the healthcare
    /// pipeline inert (no ambulance request, no hospital trip, no notification icon).
    ///
    /// MUST persist (unlike <see cref="PsyImpact.HasPsyState"/>): vanilla serializes the paired
    /// <c>HealthProblem</c> with the citizen, so a marker lost to a load would orphan the fake
    /// sickness forever — no reconcile pass would ever clear it.
    /// </summary>
    public struct FakeSickLeave : IComponentData, ISerializable
    {
        /// <summary>Absolute game-time stamp (TotalGameHours) when the fake sick leave ends.</summary>
        public float ExpiryHours;

        private const byte SAVE_VERSION = 1;

        public void SetDefaults() => this = default;

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SAVE_VERSION);
            try
            {
                KeyedSerializer.WriteBlockHeader(writer, 1);
                KeyedSerializer.WriteField(writer, "exp", ExpiryHours);
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            SetDefaults();
            if (!SerializationGuard.TryBeginBlock(reader, SAVE_VERSION, out var version, out var block, nameof(FakeSickLeave)))
            { return; }
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
                            // Corrupt/negative expiry → 0: the conservative default is "already
                            // expired", so the reconcile pass clears the fake sickness instead of
                            // leaving a citizen home forever.
                            case "exp": ExpiryHours = KeyedSerializer.ReadSafeFloatUnclamped(reader, tag, "exp", 0f); break;
                            default: KeyedSerializer.Skip(reader, tag); break;
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                FakeSickLeaveLog.Log.Error($"Deserialize {nameof(FakeSickLeave)} failed: {ex}");
                SetDefaults();
            }
            finally { SerializationGuard.EndBlock(reader, block); }
        }
    }

    /// <summary>
    /// Tag on a <see cref="PsyOpsAttack"/> entity: its one-shot sick-leave wave has been
    /// emitted. Kept as a separate component so <c>PsyOpsLaunchSystem</c> stays the sole
    /// writer of <see cref="PsyOpsAttack"/> (CIVIC175) while <c>SickLeaveWaveSystem</c> owns
    /// this latch. IEmptySerializable: the latch must round-trip — a save between land and
    /// expiry would otherwise re-fire the wave on load.
    /// </summary>
    public struct PsySickWaveEmitted : IComponentData, IEmptySerializable
    {
        public void SetDefaults() => this = default;
    }
}
