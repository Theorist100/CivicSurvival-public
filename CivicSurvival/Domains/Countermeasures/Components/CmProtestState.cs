using Unity.Entities;

namespace CivicSurvival.Core.Components.CrossDomain
{
    /// <summary>
    /// Protest subsystem state (domain-private to Countermeasures).
    /// Lives on the same singleton entity as CountermeasuresCoreFsm.
    ///
    /// Writer: CountermeasuresUpdateSystem (sole writer)
    /// Readers: CountermeasuresUISystem (owner domain only)
    /// </summary>
    public struct CmProtestState : IComponentData
    {
        /// <summary>Current number of active protests.</summary>
        public int ActiveProtests;

        /// <summary>Seconds until next protest can spawn.</summary>
        public float CooldownSeconds;

        /// <summary>Seconds accumulated toward next protest decay.</summary>
        public float DecaySeconds;

        /// <summary>Protest RNG state for determinism.</summary>
        public uint RngState;

        public void SetDefaults()
        {
            ActiveProtests = 0;
            CooldownSeconds = 0f;
            DecaySeconds = 0f;
            RngState = 0x50524F54u; // "PROT" hex
        }

        public static CmProtestState CreateDefault()
        {
            var state = new CmProtestState();
            state.SetDefaults();
            return state;
        }
    }
}
