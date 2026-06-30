using Game;

namespace CivicSurvival.Core.Interfaces.Core
{
    /// <summary>
    /// Lifecycle root for a gameplay feature.
    /// Each feature implements this to register its ECS systems.
    ///
    /// Phase 1a (rename only): no semantic change. Property `Name` will be
    /// renamed to `Id` in Phase 1b alongside the addition of optional
    /// capability interfaces.
    ///
    /// Priority ranges:
    ///   0-999:    Core (singletons, time, adapters)
    ///   1000-1999: Infrastructure (services, wiring)
    ///   2000-2999: Gameplay (domain logic)
    ///   3000+:    UI (panels, debug)
    ///
    /// Usage:
    ///   public class PowerGridDomain : IFeatureModule
    ///   {
    ///       public string Name => "PowerGrid";
    ///       public int Priority => 2000;
    ///       public void RegisterSystems(UpdateSystem updateSystem) { ... }
    ///   }
    /// </summary>
    public interface IFeatureModule
    {
        /// <summary>
        /// Feature name for logging and identification.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Registration priority. Lower values register first.
        /// Use ranges: 0-999 Core, 1000-1999 Infra, 2000-2999 Gameplay, 3000+ UI.
        /// </summary>
        int Priority { get; }

        /// <summary>
        /// Register all ECS systems for this feature.
        /// Called during Mod.OnLoad() via FeatureRegistry.
        /// </summary>
        /// <param name="updateSystem">CS2 UpdateSystem for system registration</param>
        void RegisterSystems(UpdateSystem updateSystem);
    }
}
