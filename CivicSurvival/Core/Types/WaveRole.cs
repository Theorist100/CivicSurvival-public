namespace CivicSurvival.Core.Types
{
    /// <summary>
    /// Semantic role of a wave. WaveNumber stays a single public sequence:
    /// 0 = no active wave, 1 = intro wave, 2+ = regular waves.
    /// </summary>
    public enum WaveRole : byte
    {
        None = 0,
        Intro = 1,
        Regular = 2
    }
}
