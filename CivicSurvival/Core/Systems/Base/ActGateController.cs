using System;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Types;
using Unity.Entities;

namespace CivicSurvival.Core.Systems.Base
{
    public sealed class ActGateController
    {
        private readonly Func<Act, bool> m_IsOpenFor;
        private readonly Action<ActGateState, ActGateState, bool> m_OnTransition;

        public ActGateState State { get; private set; } = ActGateState.AwaitingActState;

        public ActGateController(
            Func<Act, bool> isOpenFor,
            Action<ActGateState, ActGateState, bool> onTransition)
        {
            m_IsOpenFor = isOpenFor ?? throw new ArgumentNullException(nameof(isOpenFor));
            m_OnTransition = onTransition ?? throw new ArgumentNullException(nameof(onTransition));
        }

        public void ReconcileFromSingleton(EntityQuery currentActQuery)
        {
            if (!currentActQuery.TryGetSingleton<CurrentActSingleton>(out var singleton))
                return;

            ApplyState(m_IsOpenFor(singleton.CurrentAct)
                ? ActGateState.Active
                : ActGateState.Inactive);
        }

        public void ApplyExternalState(bool isOpen)
        {
            ApplyState(isOpen ? ActGateState.Active : ActGateState.Inactive);
        }

        private void ApplyState(ActGateState next)
        {
            var old = State;
            if (next == old)
                return;

            State = next;
            var isInitial = old == ActGateState.AwaitingActState;
            m_OnTransition(old, next, isInitial);
        }
    }
}
