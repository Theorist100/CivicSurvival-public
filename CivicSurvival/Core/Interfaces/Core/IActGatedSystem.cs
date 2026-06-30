using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Interfaces.Core
{
    public interface IActGatedSystem
    {
        Act MinActiveAct { get; }

        ActGateState GateState { get; }
    }
}
