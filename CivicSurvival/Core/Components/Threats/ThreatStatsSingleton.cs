using Colossal.Serialization.Entities;
using CivicSurvival.Core.Components.Lifecycle;
using Unity.Entities;

namespace CivicSurvival.Core.Components.Threats
{
    /// <summary>
    /// Global threat statistics as ECS singleton.
    /// Single Source of Truth for active threat counts.
    ///
    /// Access: SystemAPI.GetSingleton&lt;ThreatStatsSingleton&gt;() or GetEntityQuery().GetSingleton()
    /// Modify: SystemAPI.GetSingletonRW&lt;ThreatStatsSingleton&gt;()
    ///
    /// Writer: ThreatTargetSystem (writes stats each frame)
    /// Readers: WaveExecutor (attack phase), UI panels
    /// NOTE: Camera proximity moved to ThreatCameraProximitySingleton (writer: ThreatMovementSystem)
    ///
    /// NOTE: IEmptySerializable (not ISerializable) — stats are recalculated every
    /// frame from active threat entities. Data is derived, not persisted. Colossal's
    /// ComponentDataSerializer throws ComponentSerializerException on an ISerializable
    /// whose Serialize writes zero bytes; IEmptySerializable is the framework's marker
    /// for "intentionally no persisted payload".
    /// </summary>
    public struct ThreatStatsSingleton : IComponentData, IEmptySerializable
    {
        /// <summary>Number of active Shahed drones (not intercepted)</summary>
        public int ActiveShahedCount;

        /// <summary>Number of active ballistic missiles (not intercepted)</summary>
        public int ActiveBallisticCount;

        // Helper properties
        public readonly int TotalActiveCount => ActiveShahedCount + ActiveBallisticCount;
        public readonly bool HasActiveThreats => TotalActiveCount > 0;

        public static ThreatStatsSingleton Default => default;

        public static void EnsureExists(EntityManager em)
        {
            CivicSingleton.Ensure(em, Default);
        }

        // IEmptySerializable is a pure marker (no Serialize/Deserialize): Colossal skips
        // payload read/write for this component but still round-trips the entity.
        public void SetDefaults() { this = Default; }
    }

    public enum ThreatLoadRestorePolicy : byte
    {
        Resume = 0,
        DiscardRestoredThreats = 1
    }

    public static class ThreatLoadRestorePolicyLatch
    {
        private static int s_DiscardRestoredThreats;

        public static void ArmDiscardRestoredThreats()
            => System.Threading.Interlocked.Exchange(ref s_DiscardRestoredThreats, 1);

        public static void Clear()
            => System.Threading.Interlocked.Exchange(ref s_DiscardRestoredThreats, 0);

        public static bool ConsumeDiscardRestoredThreats()
            => System.Threading.Interlocked.Exchange(ref s_DiscardRestoredThreats, 0) != 0;
    }

    /// <summary>
    /// One-shot load reconciliation result written by ThreatLoadRenderReinitSystem.
    /// WaveExecutor reads this instead of independently guessing which restored
    /// threats survived terminal-state routing.
    /// </summary>
    // Intentionally NOT IEmptySerializable / ISerializable: this is transient one-load handoff
    // state, recreated via EnsureExists and reset to Default on every OnGameLoaded. It must NOT
    // persist into the save (a stale ReinitCompleted/count would mislead the next load's resume
    // decision). Do not add a serialization marker.
    public struct ThreatLoadResumeState : IComponentData
    {
        public int LiveShaheds;
        public int LiveBallistics;
        public int PurgedTerminalThreats;
        public bool ReinitCompleted;
        public ThreatLoadRestorePolicy RestorePolicy;

        public readonly int LiveThreats => LiveShaheds + LiveBallistics;

        public static ThreatLoadResumeState Default => default;

        public static void EnsureExists(EntityManager em)
        {
            CivicSingleton.Ensure(em, Default);
        }
    }
}
