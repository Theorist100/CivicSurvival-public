using Unity.Collections;
using Unity.Entities;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Interfaces.Domain.Power
{
    /// <summary>
    /// Write side of the building → live backup mod-entity link map. Only the owner
    /// (<c>BackupPowerDistributionSystem</c>) rebuilds the link buffer; it does so wholesale each
    /// throttled tick from the live <c>BackupPower</c> entities (which it already scans), so there
    /// is no per-entity link mutation and no structural change ever touches a vanilla building.
    /// </summary>
    [OwnedByFeatureId(FeatureIds.PowerBackupName)]
    public interface IBackupPowerLinkWriter
    {
        /// <summary>
        /// Begin a rebuild: completes the write slot's last reader (noop in the N-2 case) and
        /// clears it. Fill the returned map with <c>building.Packed → modEntity</c> (live links
        /// only), then call <see cref="CommitWrite"/>. The returned struct is a handle into the
        /// service-owned buffer.
        /// </summary>
        NativeHashMap<long, Entity> BeginWrite();

        /// <summary>Publish the freshly filled write buffer as the new read buffer and advance the write slot.</summary>
        void CommitWrite();

        /// <summary>Drop all link buffers — world reset / pre-load.</summary>
        void Clear();
    }
}
