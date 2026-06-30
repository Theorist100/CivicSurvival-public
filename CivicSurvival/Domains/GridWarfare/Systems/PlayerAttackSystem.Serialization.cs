using Colossal.Serialization.Entities;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Serialization;
using CivicSurvival.Domains.GridWarfare.Data;

namespace CivicSurvival.Domains.GridWarfare.Systems
{
    /// <summary>
    /// PlayerAttackSystem - Save/Load serialization (IDefaultSerializable).
    /// Persists operation slots and counter across save/load.
    /// </summary>
    public partial class PlayerAttackSystem : IDefaultSerializable, IResettable, IBootDefaultsReset
    {
        public void ResetToBootDefaults(ResetReason reason) => ResetState();

        /// <summary>
        /// Called when starting a new game (not loading a save).
        /// </summary>
        public void SetDefaults(Context context) => ResetState();
        void IResettable.ResetState() => ResetState();

        private void ResetState()
        {
            m_NextOperationId = PlayerAttackCodec.DefaultNextOperationId;
            m_NextExecutionId = PlayerAttackCodec.DefaultNextExecutionId;
            lock (m_SlotsLock)
            {
                for (int i = 0; i < MAX_SLOTS; i++)
                {
                    m_Slots[i] = OperationSlot.Empty;
                }
                PublishSlotsLocked();
            }
            m_LastSeenAct = default;
            m_HasSeenAct = false;
            m_ActTransitionUnlockIds.Clear();
            m_CancelledEvents.Clear();
            m_PendingSlotsPublish = false;
            m_StabilityDiscount = 0f;
            Log.Info("SetDefaults: Starting fresh");
        }

        /// <summary>
        /// Serialize operation state to save file.
        /// </summary>
        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SaveVersions.GLOBAL);
            try
            {

                // Lock covers both m_NextOperationId and the slot snapshot
                // to prevent race with PrepareOperation/GetSlots on UI thread
                PlayerOperationSlotPersistState[] slotsCopy;
                int nextOperationId;
                int nextExecutionId;
                lock (m_SlotsLock)
                {
                    nextOperationId = m_NextOperationId;
                    nextExecutionId = m_NextExecutionId;
                    slotsCopy = new PlayerOperationSlotPersistState[MAX_SLOTS];
                    for (int i = 0; i < MAX_SLOTS; i++)
                    {
                        var slot = m_Slots[i];
                        slotsCopy[i] = new PlayerOperationSlotPersistState(
                            (int)slot.State,
                            slot.AttackType ?? string.Empty,
                            slot.LockedAmount,
                            slot.PrepareStartTime,
                            slot.PrepareDuration,
                            slot.OperationId ?? string.Empty,
                            slot.ExecutionId,
                            slot.ExecutionCategory,
                            slot.ExecutionBaseDamage,
                            slot.ExecutionActualDamage,
                            slot.ExecutionWasBlocked,
                            slot.ExecutionWasVulnerable);
                        // ExecutionClaimed is an in-frame runtime reservation. Persisting it
                        // would reload an Executing intent without an active commit owner.
                    }
                }

                var state = new PlayerAttackPersistState(nextOperationId, slotsCopy, nextExecutionId);
                PlayerAttackCodec.Write(state, writer);

            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
            SerializationGuard.LogSerialized(nameof(PlayerAttackSystem), SaveVersions.GLOBAL);
        }

        /// <summary>
        /// Deserialize operation state from save file.
        /// </summary>
        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SaveVersions.GLOBAL, out var version, out var block, nameof(PlayerAttackSystem)))
            {
                ResetToBootDefaults(ResetReason.VersionMismatch);
                return;
            }
            try
            {
                PlayerAttackCodec.Read(reader, MAX_SLOTS, out var state);

                // M50 FIX: Build into temp array first, then lock-assign atomically
                var loadedSlots = new OperationSlot[MAX_SLOTS];
                for (int i = 0; i < state.Slots.Count && i < MAX_SLOTS; i++)
                {
                    var slot = state.Slots[i];
                    loadedSlots[i] = new OperationSlot
                    {
                        State = slot.State switch
                        {
                            1 => OperationState.Preparing,
                            2 => OperationState.Ready,
                            3 => OperationState.Executing,
                            _ => OperationState.Idle,
                        },
                        AttackType = slot.AttackType,
                        LockedAmount = slot.LockedAmount,
                        PrepareStartTime = slot.PrepareStartTime,
                        PrepareDuration = slot.PrepareDuration,
                        OperationId = slot.OperationId,
                        ExecutionId = slot.ExecutionId,
                        ExecutionCategory = slot.ExecutionCategory,
                        ExecutionBaseDamage = slot.ExecutionBaseDamage,
                        ExecutionActualDamage = slot.ExecutionActualDamage,
                        ExecutionWasBlocked = slot.ExecutionWasBlocked,
                        ExecutionWasVulnerable = slot.ExecutionWasVulnerable
                    };
                }

                lock (m_SlotsLock)
                {
                    m_NextOperationId = state.NextOperationId;
                    m_NextExecutionId = state.NextExecutionId;
                    System.Array.Copy(loadedSlots, m_Slots, MAX_SLOTS);
                    RebaseNextOperationIdLocked();
                    RebaseNextExecutionIdLocked();
                    PublishSlotsLocked();
                }

                Log.Info($"Deserialized v{version}: NextId={m_NextOperationId}, Slots={MAX_SLOTS}");
            }
            catch (System.Exception ex)
            {
                Log.Error($"Deserialize failed: {ex}");
                ResetToBootDefaults(ResetReason.DeserializeFailed);
            }
            finally
            {
                SerializationGuard.EndBlock(reader, block);
            }
        }
    }
}
