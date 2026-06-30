using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using CivicSurvival.Core.Interfaces.Domain.Power;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Infrastructure
{
    /// <summary>
    /// Holds the building → live backup mod-entity link map as a triple-buffered
    /// <see cref="NativeHashMap{TKey,TValue}"/> (<c>long buildingKey → Entity modEntity</c>).
    ///
    /// Isolation-restoring replacement for the <c>BackupPowerRef</c> IComponentData, which used to
    /// hang on vanilla building entities. Every add/remove of that component migrated the building
    /// archetype and invalidated a vanilla render chunk-cache read synchronously by a Burst job on
    /// the main thread → null-chunk <c>c0000005</c> (see <c>CRASH_DIAGNOSTIC_PLAN.md</c>). With the
    /// link in a side map, zero mod components sit on vanilla buildings → zero archetype churn.
    ///
    /// Owned and created by <c>BackupPowerDistributionSystem</c> (registered into ServiceRegistry in
    /// its OnCreate, disposed in OnDestroy — same lifetime as the world). Lives in Core so the
    /// PowerBackup domain can instantiate it (a Domain cannot import the Services layer, CIVIC182).
    ///
    /// Triple-buffer protocol (mirror of <c>BlackoutSystem.m_Buffers[3]</c>): the owner rebuilds the
    /// write slot wholesale each throttled tick, then publishes it as the read slot. Burst readers
    /// (BlackoutJob) and main-thread readers (Corruption / Cleanup / Maintenance) read the read slot;
    /// the owner never writes the slot currently being read. No main-thread <c>Complete()</c> /
    /// sync point, no torn read — writer and active reader are always on different slots, and a slot
    /// is only reused for writing two commits later, by which time its registered reader job is done.
    /// </summary>
    public sealed class BackupPowerLinkMap : IBackupPowerLinkReader, IBackupPowerLinkWriter, IDisposable
    {
        private const int INITIAL_CAPACITY = 256;
        private const int SLOTS = 3;

        private NativeHashMap<long, Entity>[] m_Buffers = null!;
        private JobHandle[] m_SlotHandles = null!;
        private int m_WriteSlot;
        private int m_ReadSlot;
        private bool m_Initialized;

        public void Initialize()
        {
            if (m_Initialized) return;
            m_Buffers = new NativeHashMap<long, Entity>[SLOTS];
            m_SlotHandles = new JobHandle[SLOTS];
            for (int i = 0; i < SLOTS; i++)
                m_Buffers[i] = new NativeHashMap<long, Entity>(INITIAL_CAPACITY, Allocator.Persistent);
            m_WriteSlot = 0;
            m_ReadSlot = 0;
            m_Initialized = true;
        }

        // ---- IBackupPowerLinkWriter (owner only) ----

        public NativeHashMap<long, Entity> BeginWrite()
        {
            // Slot last used by a reader two commits ago — long since done. Complete is a noop in the
            // normal case, the guarantee under worst-case scheduling.
            m_SlotHandles[m_WriteSlot].Complete();
            ref var wb = ref m_Buffers[m_WriteSlot];
            wb.Clear();
            return wb;
        }

        public void CommitWrite()
        {
            m_ReadSlot = m_WriteSlot;
            m_WriteSlot = (m_WriteSlot + 1) % SLOTS;
        }

        public void Clear()
        {
            if (!m_Initialized) return;
            for (int i = 0; i < SLOTS; i++)
            {
                m_SlotHandles[i].Complete();
                if (m_Buffers[i].IsCreated) m_Buffers[i].Clear();
                m_SlotHandles[i] = default;
            }
            m_WriteSlot = 0;
            m_ReadSlot = 0;
        }

        // ---- IBackupPowerLinkReader ----

        public bool TryGet(BuildingRef building, out Entity modEntity)
        {
            modEntity = Entity.Null;
            if (!m_Initialized) return false;
            if (!m_Buffers[m_ReadSlot].TryGetValue(building.Packed, out var e)) return false;
            if (e == Entity.Null) return false;
            modEntity = e;
            return true;
        }

        public NativeHashMap<long, Entity> AcquireReadSnapshot(out int readSlot)
        {
            readSlot = m_ReadSlot;
            return m_Buffers[m_ReadSlot];
        }

        public JobHandle SlotHandle(int slot) => m_SlotHandles[slot];

        public void RegisterReader(int slot, JobHandle reader)
            => m_SlotHandles[slot] = JobHandle.CombineDependencies(m_SlotHandles[slot], reader);

        // ---- lifecycle ----

        public void Dispose()
        {
            if (!m_Initialized) return;
            for (int i = 0; i < SLOTS; i++)
            {
                m_SlotHandles[i].Complete();
                if (m_Buffers[i].IsCreated) m_Buffers[i].Dispose();
            }
            m_Initialized = false;
        }
    }
}
