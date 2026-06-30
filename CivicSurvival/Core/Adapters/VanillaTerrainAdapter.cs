using System;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Systems;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Types.Snapshots;
using CivicSurvival.Core.Utils;
using Unity.Mathematics;

namespace CivicSurvival.Core.Adapters
{
    [ActIndependent]
    public partial class VanillaTerrainAdapter : ThrottledSystemBase
    {
        private static readonly LogContext Log = new("VanillaTerrainAdapter");

        protected override int UpdateInterval => Engine.Timing.UPDATE_INTERVAL_1_SECOND;

        private Game.Simulation.TerrainSystem? m_TerrainSystem;
        [NonSerialized] private MapBoundsState? m_State;
        [NonSerialized] private readonly VersionedView<MapBoundsSnapshot> m_SnapshotView =
            new(MapBoundsSnapshot.Default);
        [NonSerialized] private string? m_UnavailableReason;
        [NonSerialized] private bool m_TerrainSystemMissingSampleWarned;

        internal bool HasPublishedSnapshot => m_SnapshotView.Version > 0;
        internal string? UnavailableReason => m_UnavailableReason;

        protected override void OnCreate()
        {
            base.OnCreate();

            try
            {
                m_TerrainSystem = World.GetOrCreateSystemManaged<Game.Simulation.TerrainSystem>();
            }
            catch (Exception ex)
            {
                m_UnavailableReason = $"TerrainSystem unavailable: {ex.Message}";
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

        protected override void OnThrottledUpdate()
        {
#pragma warning disable CIVIC256 // Failure was logged in OnCreate; adapter remains unavailable until the next world host
            if (m_TerrainSystem == null)
                return;
#pragma warning restore CIVIC256

            m_SnapshotView.Publish(BuildSnapshot(m_TerrainSystem));
        }

        internal void BindFacade(MapBoundsState facade)
        {
            m_State = facade;
            facade.CurrentHost = this;
        }

        internal bool TryGetSnapshot(out MapBoundsSnapshot snapshot, out uint version)
        {
            int observerVersion = -1;
            var view = m_SnapshotView.Observe(ref observerVersion);
            snapshot = view.Value;
            version = (uint)view.Version;
            return version > 0;
        }

        internal bool TrySampleHeight(float3 worldPosition, out float height)
        {
            height = 0f;
            if (m_TerrainSystem == null)
            {
                if (!m_TerrainSystemMissingSampleWarned)
                {
                    m_TerrainSystemMissingSampleWarned = true;
                    Log.Warn("TrySampleHeight skipped because TerrainSystem is unavailable");
                }
                return false;
            }

            var heightData = m_TerrainSystem.GetHeightData(waitForPending: true);
            if (!heightData.isCreated)
                return false;

            height = Game.Simulation.TerrainUtils.SampleHeight(ref heightData, worldPosition);
            return true;
        }

        private static MapBoundsSnapshot BuildSnapshot(Game.Simulation.TerrainSystem terrain)
        {
            var offset = terrain.playableOffset;
            var area = terrain.playableArea;
            return new MapBoundsSnapshot(
                playableOffset: new float3(offset.x, 0f, offset.y),
                playableArea: area);
        }
    }
}
