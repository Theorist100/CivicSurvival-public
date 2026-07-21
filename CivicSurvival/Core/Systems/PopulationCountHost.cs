using Game.Citizens;
using Game.Common;
using Game.Simulation;
using Unity.Entities;
using CivicSurvival.Core.Adapters;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Infrastructure;

namespace CivicSurvival.Core.Systems
{
    /// <summary>
    /// World-owned host for <see cref="CivicSurvival.Core.Utils.PopulationUtils"/>.
    /// Owns the per-World Citizen EntityQuery.
    /// </summary>
    [FrameworkSystem]
    [ActIndependent]
    public partial class PopulationCountHost : SystemBase
    {
        private EntityQuery m_CitizenQuery;
        [System.NonSerialized] private PopulationCountFacade? m_Facade;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_CitizenQuery = GetEntityQuery(
                ComponentType.ReadOnly<Citizen>(),
                ComponentType.Exclude<Deleted>());

            if (ServiceRegistry.IsInitialized)
            {
                m_Facade = ServiceRegistry.Instance.Get<PopulationCountFacade>();
                if (m_Facade != null) m_Facade.CurrentHost = this;
            }
        }

        protected override void OnDestroy()
        {
            if (m_Facade != null && ReferenceEquals(m_Facade.CurrentHost, this))
                m_Facade.CurrentHost = null;
            m_Facade = null;
            base.OnDestroy();
        }

        protected override void OnUpdate() { /* host carries no per-frame work */ }

        internal int GetCitizenCount() => m_CitizenQuery.CalculateEntityCount();
    }
}
