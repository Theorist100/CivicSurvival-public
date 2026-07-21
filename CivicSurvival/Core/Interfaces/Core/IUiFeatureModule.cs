using Game;

namespace CivicSurvival.Core.Interfaces.Core
{
    /// <summary>
    /// Optional capability interface: feature owns UI system registrations.
    ///
    /// FeatureRegistry calls RegisterUI AFTER RegisterSystems so that UI
    /// systems can take ComponentLookups on simulation systems already
    /// created in this lifecycle pass.
    ///
    /// All features that own UI systems must implement this interface
    /// (no mixed registration where some UI systems live in RegisterSystems).
    /// </summary>
    public interface IUiFeatureModule
    {
        void RegisterUI(UpdateSystem updateSystem);
    }
}
