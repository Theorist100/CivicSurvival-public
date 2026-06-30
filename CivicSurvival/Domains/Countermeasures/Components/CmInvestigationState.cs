using Unity.Collections;
using Unity.Entities;

namespace CivicSurvival.Core.Components.CrossDomain
{
    /// <summary>
    /// Investigation subsystem state (domain-private to Countermeasures).
    /// Lives on the same singleton entity as CountermeasuresCoreFsm.
    ///
    /// Writer: CountermeasuresUpdateSystem (sole writer)
    /// Readers: CountermeasuresUISystem, CountermeasuresHelper (owner domain only)
    /// </summary>
    public struct CmInvestigationState : IComponentData
    {
        /// <summary>Whether investigation is currently active.</summary>
        public bool Active;

        /// <summary>Investigation progress (0-100).</summary>
        public int Progress;

        /// <summary>Last reported milestone (25, 50, 75).</summary>
        public int LastMilestone;

        /// <summary>Hour when investigation started.</summary>
        public float StartHour;

        /// <summary>Name of investigating journalist.</summary>
        public FixedString64Bytes Journalist;

        /// <summary>Current bribe cost to stop investigation.</summary>
        public int BribeCost;

        /// <summary>Whether waiting for player investigation choice.</summary>
        public bool WaitingForChoice;

        /// <summary>Investigation RNG state for determinism.</summary>
        public uint RngState;

        public void SetDefaults()
        {
            Active = false;
            Progress = 0;
            LastMilestone = 0;
            StartHour = 0f;
            Journalist = default;
            BribeCost = 0;
            WaitingForChoice = false;
            RngState = 0x494E5645u; // "INVE" hex
        }

        public static CmInvestigationState CreateDefault()
        {
            var state = new CmInvestigationState();
            state.SetDefaults();
            return state;
        }
    }
}
