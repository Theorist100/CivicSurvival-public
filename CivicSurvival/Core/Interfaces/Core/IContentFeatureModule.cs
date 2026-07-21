namespace CivicSurvival.Core.Interfaces.Core
{
    /// <summary>
    /// Optional capability interface: feature owns content registrations
    /// (SatireRegistry providers, character registries, content packs).
    ///
    /// FeatureRegistry calls RegisterContent BEFORE RegisterSystems so that
    /// systems can read content during OnCreate without ordering surprises.
    /// </summary>
    public interface IContentFeatureModule
    {
        void RegisterContent();
    }
}
