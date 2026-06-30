using System;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Core.Adapters
{
    [ActIndependent]
    public partial class VanillaPlanetaryClockAdapter : CivicSystemBase
    {
        private static readonly LogContext Log = new("VanillaPlanetaryClockAdapter");

        private Game.Simulation.PlanetarySystem? m_PlanetarySystem;
        [NonSerialized] private PlanetaryClockState? m_State;
        [NonSerialized] private string? m_UnavailableReason;

        internal bool IsBound => m_PlanetarySystem != null;
        internal string? UnavailableReason => m_UnavailableReason;

        protected override void OnCreate()
        {
            base.OnCreate();

            try
            {
                m_PlanetarySystem = World.GetOrCreateSystemManaged<Game.Simulation.PlanetarySystem>();
            }
            catch (Exception ex)
            {
                m_UnavailableReason = $"PlanetarySystem unavailable: {ex.Message}";
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
            // Read on demand through PlanetaryClockState.
        }

        internal void BindFacade(PlanetaryClockState facade)
        {
            m_State = facade;
            facade.CurrentHost = this;
        }

        internal bool TryReadClock(out int dayOfYear, out float currentHour)
        {
            if (m_PlanetarySystem == null)
            {
                dayOfYear = 0;
                currentHour = 0f;
                return false;
            }

            float yearDay = m_PlanetarySystem.dayOfYear;
            dayOfYear = Math.Max(0, (int)Math.Floor(yearDay));
            currentHour = Math.Max(0f, (yearDay - dayOfYear) * GameRate.HOURS_PER_DAY);
            return true;
        }
    }
}
