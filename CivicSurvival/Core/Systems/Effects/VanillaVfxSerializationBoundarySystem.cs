using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Diagnostics;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Core.Systems.Effects
{
    /// <summary>
    /// Serialize-phase boundary system for the runtime VFX anchor.
    ///
    /// Tags <see cref="VanillaVfxSystem"/>'s anchor + dummy with <c>Game.Common.Deleted</c>
    /// before vanilla <c>Game.Serialization.BeginPrefabSerializationSystem</c> drives
    /// its PrefabReferences sub-phase scan. The anchor archetype
    /// <c>{ EnabledEffect, PrefabRef }</c> matches both
    /// <c>Game.Serialization.SerializerSystem</c>'s main query (whitelist via
    /// <c>EnabledEffect</c> = <c>IEmptySerializable</c> and direct <c>PrefabRef</c>)
    /// and <c>Game.Serialization.PrimaryPrefabReferencesSystem</c>'s PrefabRef query.
    /// Both queries exclude <c>Deleted</c> in their <c>None</c> set, so the tag
    /// excludes the anchor from the save snapshot.
    ///
    /// Lifecycle:
    /// 1. Save tick: SaveGameSystem.OnUpdate → UpdateSystem.Update(Serialize).
    /// 2. This system runs first (registered via RegisterBefore&lt;Boundary,
    ///    BeginPrefabSerializationSystem&gt;) and calls
    ///    <c>VanillaVfxSystem.PrepareForSaveSerialization</c>.
    /// 3. BeginPrefabSerializationSystem / SerializerSystem scan, skipping the
    ///    Deleted-tagged anchor + dummy.
    /// 4. Game.Common.CleanUpSystem destroys Deleted entities in
    ///    SystemUpdatePhase.Cleanup AFTER Serialize.
    /// 5. Next frame: VanillaVfxSystem.OnUpdateImpl observes the missing anchor
    ///    and reseeds via RecoverMissingAnchor (m_NextEffectId is preserved).
    ///
    /// Vanilla GameManager.Save bypasses onGamePreload entirely (decompile
    /// Game.SceneFlow/GameManager.cs:904-912 vs :1000-1007), so OnGamePreload-based
    /// save hooks are dead code; CIVIC473 enforces.
    /// </summary>
    [ActIndependent]
    public partial class VanillaVfxSerializationBoundarySystem : CivicSystemBase
    {
        private static readonly LogContext Log = new("VanillaVfxSerializationBoundary");

        private VanillaVfxSystem m_Vfx = null!;

        protected override void OnCreate()
        {
            base.OnCreate();
            // Same-domain sibling resolution: VanillaVfxSystem and this boundary
            // system both register inside EffectsDomain. The boundary cannot
            // function without its owning Vfx system — there is no feature-gate
            // path where one exists without the other. CIVIC400 false positive.
#pragma warning disable CIVIC400
            m_Vfx = World.GetOrCreateSystemManaged<VanillaVfxSystem>();
#pragma warning restore CIVIC400
            Log.Info("Created (Serialize-phase anchor boundary)");
        }

        protected override void OnUpdateImpl()
        {
            // Save is the only Serialize-phase entry point: SaveGameSystem.OnUpdate
            // synchronously calls m_UpdateSystem.Update(SystemUpdatePhase.Serialize)
            // (SaveGameSystem.cs:82) after GameManager.Save assigns the SaveGame
            // purpose context (GameManager.cs:931). Idempotent inside VanillaVfxSystem.
            //
            // Crash-heartbeat: this IS the save entry point (OnGamePreload is dead for save, see the
            // class doc). Mark Saving so a watchdog ANR during a long synchronous save (autosave on a
            // big city) is classified as a save freeze, not an in-game one. The next telemetry pulse
            // returns the phase to ActiveSim (idempotent SetPhase).
            CrashContextProvider.SetPhase(LifecyclePhase.Saving);
            m_Vfx.PrepareForSaveSerialization();
        }
    }
}
