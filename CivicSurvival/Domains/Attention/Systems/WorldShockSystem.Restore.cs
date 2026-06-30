using Unity.Entities;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Domains.Attention.Data;

namespace CivicSurvival.Domains.Attention.Systems
{
    public partial class WorldShockSystem
    {
        public void OnLoadRestore(EntityManager entityManager)
        {
            var stateEntity = EnsureStateEntity();

            WorldShockState state;
            if (m_HasSerializedState)
            {
                state = m_SerializedState;
                entityManager.SetComponentData(stateEntity, state);
                m_LastTier = state.CurrentTier;
                m_HasSerializedState = false;
            }
            else
            {
                state = entityManager.GetComponentData<WorldShockState>(stateEntity);
            }

            ShockStateSingleton.EnsureExists(entityManager);
            m_ShockSingletonLookup.Update(this);
            UpdateShockSingleton(state);
        }
    }
}
