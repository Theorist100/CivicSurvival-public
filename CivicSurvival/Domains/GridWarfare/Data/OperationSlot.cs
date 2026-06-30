using Unity.Mathematics;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Domains.GridWarfare.Data
{
    /// <summary>
    /// State of a prepared operation slot.
    /// Player can have up to 3 operations prepared at once.
    /// </summary>
    public enum OperationState
    {
        /// <summary>Slot is empty, ready for new operation.</summary>
        Idle = 0,

        /// <summary>Operation is being prepared (timer running).</summary>
        Preparing = 1,

        /// <summary>Operation is ready to execute (waiting for Vulnerable window).</summary>
        Ready = 2,

        /// <summary>Execution was accepted and is waiting for ECS-owned effect commit.</summary>
        Executing = 3
    }

    /// <summary>
    /// Represents a single operation slot.
    /// Keep this as a value type: PlayerAttackSystem publishes events from captured
    /// slot snapshots after releasing the slot lock.
    /// </summary>
    public struct OperationSlot
    {
        /// <summary>Attack type ID (drone, blackout, disinfo).</summary>
        public string AttackType;

        /// <summary>Current state of this slot.</summary>
        public OperationState State;

        /// <summary>Shadow money locked for this operation.</summary>
        public long LockedAmount;

        /// <summary>Game time when preparation started.</summary>
        public float PrepareStartTime;

        /// <summary>Total preparation duration in seconds.</summary>
        public float PrepareDuration;

        /// <summary>Unique operation ID for wallet lock tracking.</summary>
        public string OperationId;

        /// <summary>Stable execution intent ID for exactly-once commit.</summary>
        public int ExecutionId;

        /// <summary>Click-time effect snapshot category.</summary>
        public AttackCategory ExecutionCategory;

        /// <summary>Click-time effect snapshot base damage.</summary>
        public float ExecutionBaseDamage;

        /// <summary>Click-time effect snapshot actual damage.</summary>
        public float ExecutionActualDamage;

        /// <summary>Click-time blocked result.</summary>
        public bool ExecutionWasBlocked;

        /// <summary>Click-time vulnerable-window result.</summary>
        public bool ExecutionWasVulnerable;

        /// <summary>Runtime reservation while the ECS effect owner commits this execution.</summary>
        public bool ExecutionClaimed;

        /// <summary>Check if preparation is complete.</summary>
        public bool IsPreparationComplete(float currentTime)
        {
            if (State != OperationState.Preparing) return false;
            return (currentTime - PrepareStartTime) >= PrepareDuration;
        }

        /// <summary>Get preparation progress (0-1).</summary>
        public float GetProgress(float currentTime)
        {
            if (State != OperationState.Preparing) return State == OperationState.Ready ? 1f : 0f;
            // BUG-GW-003 FIX: Guard against division by zero (zero duration = instant complete)
            if (PrepareDuration <= 0f) return 1f;
            float elapsed = currentTime - PrepareStartTime;
            return math.clamp(elapsed / PrepareDuration, 0f, 1f);
        }

        public static OperationSlot Empty => new()
        {
            AttackType = string.Empty,
            State = OperationState.Idle,
            LockedAmount = 0,
            PrepareStartTime = 0,
            PrepareDuration = 0,
            OperationId = string.Empty,
            ExecutionId = 0,
            ExecutionCategory = AttackCategory.Kinetic,
            ExecutionBaseDamage = 0f,
            ExecutionActualDamage = 0f,
            ExecutionWasBlocked = false,
            ExecutionWasVulnerable = false,
            ExecutionClaimed = false
        };
    }
}
