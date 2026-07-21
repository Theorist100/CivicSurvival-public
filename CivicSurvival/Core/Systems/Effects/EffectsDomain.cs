using Game;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Systems.CameraTracking;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Core.Systems.Effects
{
    /// <summary>
    /// Effects domain - VFX, SFX, audio.
    /// Priority 2050 registers effects systems before threat domains. Effect prefab readiness
    /// remains asynchronous; consumers must use EffectCacheSystem.IsInitialized/TryGetEffect.
    /// </summary>
    public class EffectsDomain : IFeatureModule
    {
        private const int PRIORITY = 2050;

        private static readonly LogContext Log = new("EffectsDomain");

        public string Name => "Effects";
        public int Priority => PRIORITY;


        public void RegisterSystems(UpdateSystem updateSystem)
        {
            Log.Info("Registering systems...");

            // Effect cache - caches vanilla SFX/VFX prefabs for lookup by name
            updateSystem.RegisterAt<EffectCacheSystem>(SystemUpdatePhase.GameSimulation);

            // Vanilla VFX explosions (replaces ParticleSystem-based ExplosionVfxService).
            // VFXSystem is a Rendering-phase vanilla system; same-phase registration can
            // only enforce our GameSimulation cache/control dependencies.
            updateSystem.RegisterAfter<VanillaVfxSystem, EffectCacheSystem>(SystemUpdatePhase.GameSimulation);

            // No save-side boundary system: the anchor and dummy carry vanilla EffectInstance
            // from creation, which both serialization scanners exclude (SerializerSystem
            // None-set, PrimaryPrefabReferencesSystem PrefabRef query) — the pair never leaks
            // into a save snapshot and is never torn down on the save path. The old Deleted-tag
            // boundary poisoned the VFX instance↔index mapping (see VanillaEffectAnchor doc).
            // The save crash-heartbeat it also carried lives in Core SaveHeartbeatSystem now.

            // Late-phase owner-attach drain. The exhaust controllers collect re-attach requests in
            // GameSimulation (render-gated) and enqueue them on VanillaVfxSystem; this driver drains
            // the queue once per frame in CompleteRendering — AFTER PreCulling (EffectControlSystem)
            // and Rendering (EffectTransformSystem) have run this frame, so the EnabledData
            // deps.Complete() is a noop instead of a real city-graph wait. Pause-gated inside the
            // system (CompleteRendering ticks in pause; GameSimulation producers do not).
            updateSystem.RegisterAt<VanillaVfxLateAttachSystem>(SystemUpdatePhase.CompleteRendering);

            // Camera tracking - follows threats smoothly (runs every frame)
            updateSystem.RegisterAt<CameraTrackingSystem>(SystemUpdatePhase.Rendering);

            Log.Info("Systems registered");
        }
    }
}
