using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Components.Lifecycle;
using CivicSurvival.Core.Systems.Base;

namespace CivicSurvival.Core.Features.Wellbeing
{
    /// <summary>
    /// Runs the deferred <see cref="WellbeingResolverSystem"/> citizen write after
    /// vanilla <c>CitizenHappinessSystem</c>, without giving WRS a second scheduler
    /// anchor. Depending on vanilla phase ordering, this may apply the latest prepared
    /// snapshot one simulation tick after WRS prepared it.
    /// </summary>
    [ActIndependent]
    public partial class WellbeingCitizenApplySystem : CivicSystemBase
    {
        [System.NonSerialized] private CivicDependencyWire m_DependencyWire = null!;
        private WellbeingResolverSystem? m_Resolver;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_DependencyWire = new CivicDependencyWire(nameof(WellbeingCitizenApplySystem));
        }

        protected override void OnUpdateImpl()
        {
            if (!TryWireResolver())
                return;

            m_Resolver!.TrySchedulePendingCitizenWrite();
        }

        private bool TryWireResolver()
        {
            return m_DependencyWire.EnsureWired(() =>
            {
                m_Resolver ??= World.GetExistingSystemManaged<WellbeingResolverSystem>();
                return m_Resolver != null;
            });
        }
    }
}
