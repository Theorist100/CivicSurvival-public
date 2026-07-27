using System;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Types.Snapshots;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Core.Adapters
{
    [ActIndependent]
    public partial class VanillaAreasAdapter : CivicSystemBase
    {
        private static readonly LogContext Log = new("VanillaAreasAdapter");

        private Game.Areas.UpdateCollectSystem? m_UpdateCollectSystem;
        [NonSerialized] private AreaCollectState? m_State;
        [NonSerialized] private readonly VersionedView<AreaCollectSnapshot> m_SnapshotView =
            new(AreaCollectSnapshot.Default);
        [NonSerialized] private string? m_UnavailableReason;
        [NonSerialized] private bool m_HasObservedUpdate;

        internal bool HasObservedUpdate => m_HasObservedUpdate;
        internal string? UnavailableReason => m_UnavailableReason;
        internal bool DistrictsUpdated => GetCurrentSnapshot().DistrictsUpdated;

        protected override void OnCreate()
        {
            base.OnCreate();

            try
            {
                m_UpdateCollectSystem = World.GetOrCreateSystemManaged<Game.Areas.UpdateCollectSystem>();
            }
            catch (Exception ex)
            {
                m_UnavailableReason = $"UpdateCollectSystem unavailable: {ex.Message}";
                Log.Warn(m_UnavailableReason);
            }
        }

        protected override void OnDestroy()
        {
            if (m_State != null && ReferenceEquals(m_State.CurrentHost, this))
                m_State.CurrentHost = null;
            m_State = null;

            base.OnDestroy();
        }

        protected override void OnUpdateImpl()
        {
#pragma warning disable CIVIC256 // Failure was logged in OnCreate; adapter remains unavailable until the next world host
            if (m_UpdateCollectSystem == null)
                return;
#pragma warning restore CIVIC256

            m_HasObservedUpdate = true;
            m_SnapshotView.Publish(new AreaCollectSnapshot(m_UpdateCollectSystem.districtsUpdated));
        }

        internal void BindFacade(AreaCollectState facade)
        {
            m_State = facade;
            facade.CurrentHost = this;
        }

        private AreaCollectSnapshot GetCurrentSnapshot()
        {
            int observerVersion = -1;
            return m_SnapshotView.Observe(ref observerVersion).Value;
        }
    }
}
