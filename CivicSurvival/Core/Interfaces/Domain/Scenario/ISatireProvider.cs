using System.Collections.Generic;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Interfaces.Domain.Scenario
{
    /// <summary>
    /// Interface for domain-specific satire message providers.
    /// Each domain registers its trigger tags with full configuration.
    ///
    /// DIP: Narrative depends on this interface, not on domain implementations.
    ///
    /// Migration note: Now returns SatireConfig (with AuthorId) instead of just int.
    /// </summary>
    public interface ISatireProvider
    {
        /// <summary>
        /// Domain identifier (e.g., "Blackout", "Corruption").
        /// </summary>
        string Domain { get; }

        /// <summary>
        /// Returns mapping of satire-prefix keys to their full configuration.
        /// Example: { "SATIRE_BLACKOUT" -> SatireConfig("SATIRE_BLACKOUT", 7, "BABCYA", Suffering) }
        /// </summary>
        IReadOnlyDictionary<string, SatireConfig> GetConfigs();
    }
}
