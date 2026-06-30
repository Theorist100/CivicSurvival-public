using System.Collections.Generic;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Interfaces.Services
{
    /// <summary>
    /// Serialization interface for district state persistence.
    /// Separated from Reader/Writer to keep ISP clean.
    ///
    /// Implemented by: ThreadSafeDistrictState
    /// Used by: BlackoutSystem.Serialization (save/load)
    /// </summary>
    [InfrastructureService]
    public interface IDistrictStateSerialization
    {
        /// <summary>Gets a thread-safe copy of all state for serialization.</summary>
        DistrictSerializationData GetSerializationData(IReadOnlyDictionary<int, DistrictRef> liveDistrictRefs);

        /// <summary>
        /// Replaces all state with deserialized data. Existing entries are cleared before load,
        /// so quick-load over a running session must not merge stale district data.
        /// </summary>
        void LoadSerializationData(in DistrictSerializationData data);
    }
}
