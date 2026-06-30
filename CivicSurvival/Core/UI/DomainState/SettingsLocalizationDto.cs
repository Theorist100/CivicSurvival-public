using CivicSurvival.Core.UI;

namespace CivicSurvival.Core.UI.DomainState
{
    /// <summary>
    /// Large localization payload for UI text lookup.
    /// Emitted only when the locale content changes.
    /// </summary>
    public partial struct SettingsLocalizationDto : IDomainDto
    {
        public string CurrentLocale;
        public string LocalizationStrings;
        public int LocaleVersion;
    }
}
