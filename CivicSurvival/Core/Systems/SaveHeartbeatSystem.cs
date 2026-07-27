using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Diagnostics;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Core.Systems
{
    /// <summary>
    /// Serialize-phase crash heartbeat. Save is the only Serialize-phase entry point:
    /// SaveGameSystem.OnUpdate synchronously calls
    /// m_UpdateSystem.Update(SystemUpdatePhase.Serialize) (SaveGameSystem.cs:82) after
    /// GameManager.Save assigns the SaveGame purpose context (GameManager.cs:931).
    /// Vanilla GameManager.Save bypasses onGamePreload entirely (decompile
    /// Game.SceneFlow/GameManager.cs:904-912 vs :1000-1007), so this phase hook is the
    /// only reliable save marker; CIVIC473 enforces the OnGamePreload ban.
    ///
    /// Marks the lifecycle phase as Saving so a watchdog ANR during a long synchronous
    /// save (autosave on a big city) is classified as a save freeze, not an in-game one.
    /// The next telemetry pulse returns the phase to ActiveSim (idempotent SetPhase).
    ///
    /// Registered before Game.Serialization.BeginPrefabSerializationSystem so the phase
    /// flips before any heavy serialization work starts.
    /// </summary>
    [ActIndependent]
    public partial class SaveHeartbeatSystem : CivicSystemBase
    {
        private static readonly LogContext Log = new("SaveHeartbeat");

        protected override void OnCreate()
        {
            base.OnCreate();
            Log.Info("Created (Serialize-phase save heartbeat)");
        }

        protected override void OnUpdateImpl()
        {
            CrashContextProvider.SetPhase(LifecyclePhase.Saving);
        }
    }
}
