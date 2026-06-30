using Unity.Entities;

namespace CivicSurvival.Core.Components.CrossDomain
{
    /// <summary>
    /// Police subsystem state (domain-private to Countermeasures).
    /// Lives on the same singleton entity as CountermeasuresCoreFsm.
    ///
    /// Writer: CountermeasuresUpdateSystem (sole writer)
    /// Note: External "is police active?" check uses CoreFSM.CurrentPhase.IsPoliceActive()
    /// </summary>
    public struct CmPoliceState : IComponentData
    {
        /// <summary>Whether police investigation is active.</summary>
        public bool Active;

        /// <summary>Hour when police investigation started.</summary>
        public float StartHour;

        /// <summary>Whether waiting for player police choice.</summary>
        public bool WaitingForChoice;

        /// <summary>Police charges count.</summary>
        public int ChargesCount;

        /// <summary>Police RNG state for determinism.</summary>
        public uint RngState;

        public void SetDefaults()
        {
            Active = false;
            StartHour = 0f;
            WaitingForChoice = false;
            ChargesCount = 0;
            RngState = 0x504F4C49u; // "POLI" hex
        }

        public static CmPoliceState CreateDefault()
        {
            var state = new CmPoliceState();
            state.SetDefaults();
            return state;
        }
    }
}
