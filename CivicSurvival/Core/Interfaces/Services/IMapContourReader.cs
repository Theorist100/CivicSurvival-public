using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Interfaces.Core;

namespace CivicSurvival.Core.Interfaces.Services
{
    /// <summary>
    /// Reads the city coastline contour (land↔water boundary) as ready-to-wire JSON.
    /// The contour is static: computed once per loaded city and cached, so this is a
    /// pure reader with no per-frame work.
    /// </summary>
    [InfrastructureService]
    public interface IMapContourReader : IVanillaDependencyStatus
    {
        /// <summary>
        /// Returns the cached contour JSON (flat array of world-space X/Z polylines)
        /// when it has been computed. Returns false until terrain/water are ready and
        /// the one-shot computation has run.
        /// </summary>
        bool TryGetContourJson(out string contourJson);
    }
}
