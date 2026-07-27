using Unity.Entities;
using Unity.Mathematics;

namespace CivicSurvival.Core.Types
{
    public enum ThreatTerminalOutcomeKind : byte
    {
        DirectHit = 0,
        ShahedIntercepted = 1,
        ShahedExhausted = 2,
        BallisticIntercepted = 3,
        BallisticExhaustedImpact = 4,
        BallisticExhaustedDeleteOnly = 5,
        DebugDeleteOnly = 6
    }

    public struct ThreatTerminalOutcome
    {
        private const int ImmediateImpactPriority = 400;
        private const int AcceptedInterceptPriority = 300;
        private const int ExhaustedDebrisPriority = 200;
        private const int DebugDeletePriority = 100;

        public Entity Entity;
        public ThreatTerminalOutcomeKind Kind;
        public float3 Position;
        public float3 EventPosition;
        public bool IsBallistic;
        public float ImpactRadius;
        public float DamageSeverity;
        public float DebrisFallTime;
        public int ThreatGeneration;

        public int Priority => Kind switch
        {
            ThreatTerminalOutcomeKind.DirectHit => ImmediateImpactPriority,
            ThreatTerminalOutcomeKind.BallisticExhaustedImpact => ImmediateImpactPriority,
            ThreatTerminalOutcomeKind.ShahedIntercepted => AcceptedInterceptPriority,
            ThreatTerminalOutcomeKind.BallisticIntercepted => AcceptedInterceptPriority,
            ThreatTerminalOutcomeKind.ShahedExhausted => ExhaustedDebrisPriority,
            ThreatTerminalOutcomeKind.BallisticExhaustedDeleteOnly => ExhaustedDebrisPriority,
            ThreatTerminalOutcomeKind.DebugDeleteOnly => DebugDeletePriority,
            _ => 0
        };

        public bool CreatesDebris =>
            Kind == ThreatTerminalOutcomeKind.ShahedIntercepted
            || Kind == ThreatTerminalOutcomeKind.ShahedExhausted;

        public bool EmitsImmediateImpact =>
            Kind == ThreatTerminalOutcomeKind.DirectHit
            || Kind == ThreatTerminalOutcomeKind.BallisticExhaustedImpact;

        public bool IsAcceptedIntercept =>
            Kind == ThreatTerminalOutcomeKind.ShahedIntercepted
            || Kind == ThreatTerminalOutcomeKind.BallisticIntercepted;

        public bool IsCrashedArrival =>
            Kind == ThreatTerminalOutcomeKind.ShahedExhausted
            || Kind == ThreatTerminalOutcomeKind.BallisticExhaustedImpact
            || Kind == ThreatTerminalOutcomeKind.BallisticExhaustedDeleteOnly;

        /// <summary>
        /// Builds the terminal outcome for a Patriot-intercepted threat that has finished coasting
        /// (the interceptor reached it, the interceptor despawned first, or it coasted to its target).
        /// Identical shape to the immediate-intercept queue InterceptProcessingSystem produced before
        /// deferral — explosion + render delete + intercept sound, NO city damage. DRY for the three
        /// resolution sites (InterceptorMovementSystem, InterceptorCleanupSystem, ThreatArrivalSystem).
        /// DebrisFallTime is passed in because Core types do not read BalanceConfig (callers supply it;
        /// ballistic passes 0f).
        /// </summary>
        public static ThreatTerminalOutcome Intercept(
            Entity entity, float3 position, bool isBallistic, int threatGeneration, float debrisFallTime)
        {
            return new ThreatTerminalOutcome
            {
                Entity = entity,
                Kind = isBallistic
                    ? ThreatTerminalOutcomeKind.BallisticIntercepted
                    : ThreatTerminalOutcomeKind.ShahedIntercepted,
                Position = position,
                EventPosition = position,
                IsBallistic = isBallistic,
                DebrisFallTime = isBallistic ? 0f : debrisFallTime,
                ThreatGeneration = threatGeneration
            };
        }
    }
}
