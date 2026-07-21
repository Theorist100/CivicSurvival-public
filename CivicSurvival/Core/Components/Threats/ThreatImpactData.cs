using Unity.Mathematics;
using CivicSurvival.Core.Interfaces.Threats;

namespace CivicSurvival.Core.Components.Threats
{
    public enum ImpactType : byte { DirectHit = 0, Ballistic, Debris }

    public struct ThreatImpactData
    {
        public float3 Position;
        public ImpactType Type;
        public float Severity;
        public float Radius;
        /// <summary>
        /// Threat generation carried from the arriving threat / debris. 0 =
        /// unstamped/invalid. ThreatDamageSystem drops impacts whose epoch is 0 or
        /// not from the current loaded-world generation (post-load zombie).
        /// </summary>
        public int ThreatGeneration;

        /// <summary>
        /// Burst-safe factory — copies the arrival's epoch so the stamp cannot be
        /// dropped on the threat→impact hop (no default arg ⇒ cannot re-introduce 0).
        /// Position is the real contact point; TargetPosition is the building anchor.
        /// </summary>
        public static ThreatImpactData FromArrival(in ThreatArrivalInfo arrival, float severity, float radius) =>
            new()
            {
                Position = arrival.Position,
                Type = arrival.IsBallistic ? ImpactType.Ballistic : ImpactType.DirectHit,
                Severity = severity,
                Radius = radius,
                ThreatGeneration = arrival.ThreatGeneration
            };

        /// <summary>
        /// Burst-safe factory for a debris-landing impact — requires the source
        /// threat's epoch (carried on <see cref="FallingDebris"/>) so the stamp
        /// cannot be dropped on the debris→impact hop (no default arg ⇒ no 0).
        /// </summary>
        public static ThreatImpactData FromDebris(float3 position, float severity, float radius, int threatGeneration) =>
            new()
            {
                Position = position,
                Type = ImpactType.Debris,
                Severity = severity,
                Radius = radius,
                ThreatGeneration = threatGeneration
            };
    }
}
