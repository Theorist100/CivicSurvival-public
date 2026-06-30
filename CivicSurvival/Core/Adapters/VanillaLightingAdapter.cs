using System;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Types.Snapshots;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Core.Adapters
{
    [ActIndependent]
    public partial class VanillaLightingAdapter : CivicSystemBase
    {
        private static readonly LogContext Log = new("VanillaLightingAdapter");

        private Game.Rendering.LightingSystem? m_LightingSystem;
        [NonSerialized] private LightingPhaseState? m_State;
        [NonSerialized] private readonly VersionedView<LightingPhaseSnapshot> m_SnapshotView =
            new(LightingPhaseSnapshot.Default);
        [NonSerialized] private string? m_UnavailableReason;

        internal bool HasReadySnapshot => CurrentPhase != LightingPhase.Unknown;
        internal string? UnavailableReason => m_UnavailableReason;
        internal LightingPhase CurrentPhase => GetCurrentSnapshot().Phase;
        internal bool IsDawnOrDuskLaunchWindow => GetCurrentSnapshot().IsDawnOrDuskLaunchWindow;

        protected override void OnCreate()
        {
            base.OnCreate();

            try
            {
                m_LightingSystem = World.GetOrCreateSystemManaged<Game.Rendering.LightingSystem>();
            }
            catch (Exception ex)
            {
                m_UnavailableReason = $"LightingSystem unavailable: {ex.Message}";
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
            if (m_LightingSystem == null)
                return;
#pragma warning restore CIVIC256

            m_SnapshotView.Publish(new LightingPhaseSnapshot(MapPhase(m_LightingSystem.state)));
        }

        internal void BindFacade(LightingPhaseState facade)
        {
            m_State = facade;
            facade.CurrentHost = this;
        }

        private LightingPhaseSnapshot GetCurrentSnapshot()
        {
            int observerVersion = -1;
            return m_SnapshotView.Observe(ref observerVersion).Value;
        }

        private static LightingPhase MapPhase(Game.Rendering.LightingSystem.State state)
        {
#pragma warning disable CIVIC019 // Unknown future vanilla states should degrade to reader-not-ready Unknown
            return state switch
            {
                Game.Rendering.LightingSystem.State.Dawn => LightingPhase.Dawn,
                Game.Rendering.LightingSystem.State.Sunrise => LightingPhase.Sunrise,
                Game.Rendering.LightingSystem.State.Day => LightingPhase.Day,
                Game.Rendering.LightingSystem.State.Sunset => LightingPhase.Sunset,
                Game.Rendering.LightingSystem.State.Dusk => LightingPhase.Dusk,
                Game.Rendering.LightingSystem.State.Night => LightingPhase.Night,
                _ => LightingPhase.Unknown,
            };
#pragma warning restore CIVIC019
        }
    }
}
