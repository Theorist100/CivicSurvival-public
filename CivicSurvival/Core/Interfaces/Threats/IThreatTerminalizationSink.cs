using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Interfaces.Threats
{
    [InfrastructureService]
    public interface IThreatTerminalizationSink
    {
        void Queue(in ThreatTerminalOutcome outcome);
        bool HasPending { get; }
        int PendingCount { get; }
        void Drain(System.Collections.Generic.List<ThreatTerminalOutcome> destination);
        void Clear();
    }
}
