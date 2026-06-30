using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Interfaces.Domain.Power
{
    /// <summary>
    /// Read side of the building → live backup mod-entity link map.
    ///
    /// Replaces the old <c>BackupPowerRef</c> IComponentData that hung on vanilla building
    /// entities (each add/remove migrated the building archetype and rotted vanilla render
    /// chunk-caches — see <c>CRASH_DIAGNOSTIC_PLAN.md</c>). The link now lives in a Core service
    /// as a triple-buffered <see cref="NativeHashMap{TKey,TValue}"/> (<c>long buildingKey →
    /// Entity modEntity</c>), so zero components sit on vanilla buildings.
    ///
    /// Main-thread consumers use <see cref="TryGet"/>. Burst jobs (BlackoutJob) take
    /// <see cref="AcquireReadSnapshot"/> and register their handle via <see cref="RegisterReader"/>,
    /// mirroring the BlackoutSystem triple-buffer protocol (writer and reader never touch the same
    /// slot, so no main-thread Complete() / sync point).
    /// </summary>
    [OwnedByFeatureId(FeatureIds.PowerBackupName)]
    public interface IBackupPowerLinkReader
    {
        /// <summary>True if the building has a live (non-Null) backup mod entity. Returns it.</summary>
        bool TryGet(BuildingRef building, out Entity modEntity);

        /// <summary>
        /// Current read buffer for Burst-job consumption. Pass <paramref name="readSlot"/> back to
        /// <see cref="SlotHandle"/> / <see cref="RegisterReader"/>.
        /// </summary>
        NativeHashMap<long, Entity> AcquireReadSnapshot(out int readSlot);

        /// <summary>Last-reader handle for a slot — combine into the consuming job's input deps.</summary>
        JobHandle SlotHandle(int slot);

        /// <summary>Record a job that reads <paramref name="slot"/>, so the owner completes it before overwriting.</summary>
        void RegisterReader(int slot, JobHandle reader);
    }
}
