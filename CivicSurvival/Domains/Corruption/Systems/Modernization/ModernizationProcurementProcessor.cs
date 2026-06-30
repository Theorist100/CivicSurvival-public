using System.Collections.Generic;
using Game;
using Game.Areas;
using Game.Buildings;
using Game.Common;
using Game.Simulation;
using Unity.Collections;
using Unity.Entities;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Components.Domain.Economy;
using CivicSurvival.Core.Components.Domain.Power;
using CivicSurvival.Core.Components.Lifecycle;
using CivicSurvival.Core.Components.Requests;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Events;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Interfaces.Domain.Economy;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.Systems;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Domains.Corruption.Systems.Modernization
{
    /// <summary>
    /// Procurement pipeline orchestrator: activate (queue durable intent + budget
    /// request), drain (apply confirmed / refund failed), and stale-district cleanup
    /// that crosses Store + Cleanup. Owns the cooldown anchor and the same-frame
    /// reentrancy latch.
    ///
    /// Not a SystemBase — receives ECS access via <see cref="IModernizationProcurementHost"/>.
    /// </summary>
    internal sealed class ModernizationProcurementProcessor
    {
        public const int INITIAL_PROCUREMENT_DAY = -999;

        private static readonly LogContext Log = new("DistrictModernization.Processor");

        private readonly IModernizationProcurementHost m_Host;
        private readonly ModernizationProgramStore m_Store;
        private readonly CounterfeitCleanupService m_Cleanup;
        private readonly ModernizationEligibilityPolicy m_Policy;
        private readonly CounterfeitEquipmentInstaller m_Installer;

        // F-13 (ACC-04): single-pending invariant derives from the durable
        // DistrictModernizationIntent query across frames; this frame-scoped
        // latch only closes the same-frame window before the GameSimulationEndBarrier
        // materializes that entity. NOT a serialized-state mirror.
        [System.NonSerialized] private bool m_ProcurementQueuedThisFrame;

        // Reusable list for stale cleanup (avoids allocation during iteration)
        [System.NonSerialized] private readonly List<int> m_StaleKeys = new();

        public ModernizationProcurementProcessor(
            IModernizationProcurementHost host,
            ModernizationProgramStore store,
            CounterfeitCleanupService cleanup,
            ModernizationEligibilityPolicy policy,
            CounterfeitEquipmentInstaller installer)
        {
            m_Host = host;
            m_Store = store;
            m_Cleanup = cleanup;
            m_Policy = policy;
            m_Installer = installer;
        }

        public int LastProcurementDay { get; set; } = INITIAL_PROCUREMENT_DAY;

        public bool HasPendingProcurement
            => m_ProcurementQueuedThisFrame || !m_Host.PendingModernizationBudgetQuery.IsEmpty;

        public int DaysUntilNextProcurement
        {
            get
            {
                var timeSystem = m_Host.ResolveTimeSystem();
                if (timeSystem == null)
                {
                    Log.Warn("TimeSystem unavailable — cannot compute procurement cooldown");
                    return 0;
                }

                int currentDay = timeSystem.Current.CurrentDay;
                int daysSince = currentDay - LastProcurementDay;
                int remaining = BalanceConfig.Current.ShadowProcurement.CooldownDays - daysSince;
                return remaining > 0 ? remaining : 0;
            }
        }

        /// <summary>Reset reentrancy latch — system calls at the start of each
        /// request-pump tick before iterating ModernizationRequest entities.</summary>
        public void BeginFrameLatchReset() => m_ProcurementQueuedThisFrame = false;

        public void Reset()
        {
            LastProcurementDay = INITIAL_PROCUREMENT_DAY;
            m_ProcurementQueuedThisFrame = false;
            m_StaleKeys.Clear();
        }

#pragma warning disable CIVIC231 // [ActIndependent] — procurement available in all acts (UI gating handles availability)
        public (bool Success, string FailReason) ActivateProcurement(
            int districtIndex, ContractorType contractor, RequestMeta requestMeta)
        {
            // Invariant: only one pending procurement at a time. Cross-frame:
            // the durable DistrictModernizationIntent query. Same-frame (before
            // the GameSimulationEndBarrier materializes that entity): the reentrancy latch.
            if (HasPendingProcurement)
            {
                Log.Warn("Procurement blocked: a procurement is already pending confirmation");
                return (false, "ProcurementPending");
            }

            int targetCount = ComputeProcurementTargetCount(districtIndex, out bool replacingCorrupt);

            var spCfgLocal = BalanceConfig.Current.ShadowProcurement;
            int totalCost = (int)System.Math.Round(targetCount * spCfgLocal.CostPerBuilding);

            var eligibility = m_Policy.GetEligibility(
                contractor,
                hasPendingProcurement: false,
                daysUntilNextProcurement: DaysUntilNextProcurement,
                targetBuildingCount: targetCount,
                totalCost: totalCost,
                world: m_Host.World);
            if (!eligibility.CanRun)
            {
                Log.Warn($"Procurement blocked for district {districtIndex}: {eligibility.LockedReasonId}");
                m_Cleanup.ClearScratch();
                return (false, eligibility.LockedReasonId);
            }

            var timeSystem = m_Host.ResolveTimeSystem();
            if (timeSystem == null)
            {
                Log.Error("TimeSystem unavailable");
                m_Cleanup.ClearScratch();
                return (false, "SystemError");
            }

            int kickback = 0;
            if (contractor == ContractorType.YourGuy)
            {
                kickback = (int)System.Math.Round(totalCost * spCfgLocal.CorruptKickbackPercent);
                var wallet = m_Host.ResolveWalletService();
                if (!wallet.IsOperational || wallet.IsFrozen)
                {
                    kickback = 0;
                    Log.Info($"District {districtIndex}: Kickback blocked - wallet inactive or frozen");
                }
            }

            int currentDay = timeSystem.Current.CurrentDay;

            var ecb = m_Host.CreateCommandBuffer();
            string operationKey = OperationKey(districtIndex, contractor, currentDay, targetCount, totalCost);
            var budgetEntity = ecb.QueuePendingOperation(new DistrictModernizationIntent
            {
                OperationKey = new FixedString128Bytes(operationKey),
                DistrictId = districtIndex,
                Contractor = contractor,
                BuildingCount = targetCount,
                TotalCost = totalCost,
                Kickback = kickback,
                ReplacingCorrupt = replacingCorrupt,
                ActivationDay = currentDay
            });
            if (!BudgetEmitter.TryQueueDeductOnEntity(
                    m_Host.World,
                    ecb,
                    budgetEntity,
                    totalCost,
                    BudgetCategory.Procurement,
                    BudgetPriority.PlayerAction,
                    "Modernization.Procurement",
                    out _,
                    requestMeta,
                    BudgetResultMode.RetainResult))
            {
                ecb.DestroyEntity(budgetEntity);
                Log.Warn($"Cannot afford procurement: ${totalCost}");
                m_Cleanup.ClearScratch();
                return (false, "InsufficientFunds");
            }
            m_Host.RegisterECBProducer();
            m_ProcurementQueuedThisFrame = true;
            m_Cleanup.ClearScratch();

            Log.Info($"District {districtIndex}: {contractor} procurement queued, pending budget confirmation (${totalCost})");
            return (true, "");
        }
#pragma warning restore CIVIC231

        private int ComputeProcurementTargetCount(int districtIndex, out bool replacingCorrupt)
        {
            var (unprotected, replacing) = m_Policy.Count(
                districtIndex,
                m_Host.BuildingsWithDistrictQuery,
                m_Host.CurrentDistrictLookup,
                m_Host.BackupPowerLinks);
            replacingCorrupt = replacing;

            m_Cleanup.ClearScratch();
            if (!replacing)
                return unprotected;

            m_Cleanup.PreScan(
                districtIndex,
                m_Host.CounterfeitQuery,
                m_Host.CurrentDistrictLookup,
                m_Host.BackupPowerLinks,
                collectEntities: false);
            return unprotected + m_Cleanup.ScratchKeyCount;
        }

        /// <summary>
        /// Apply confirm-or-rollback for a single resolved budget entity. Caller
        /// (system OnUpdate) provides the ECB and the RefRW/RefRO values directly —
        /// keeps SystemAPI.Query iteration where it belongs.
        /// </summary>
        public void ProcessResolvedBudget(
            EntityCommandBuffer ecb,
            EntityCommandBuffer durableEcb,
            Entity entity,
            ref DistrictModernizationIntent intent,
            in BudgetDeductResult result,
            ref PendingPhase phase,
            bool hasMeta,
            RequestMeta meta,
            bool destroyAfterTerminal = true)
        {
            ResolveOperationKey(ref intent);

            if (phase.Value == PendingPhaseValue.Applied || phase.Value == PendingPhaseValue.Confirmed)
            {
                if (intent.TerminalEmitted
                    && (intent.KickbackRequestDurable || intent.EffectiveKickback <= 0)
                    && destroyAfterTerminal)
                {
                    ecb.DestroyEntity(entity);
                }
                return;
            }

            if (!result.Succeeded)
            {
                RollbackProcurement(intent);
                intent.ChargeFailed = true;
                intent.DomainRejected = true;
                phase.Value = PendingPhaseValue.Confirmed;
                if (hasMeta)
                    RequestResultEmitter.Emit(durableEcb, meta, RequestKind.Modernization, RequestStatus.Failed, ReasonIds.BackupModernizationInsufficientFunds, m_Host.ElapsedTime);
                intent.TerminalEmitted = true;
                ecb.DestroyEntity(entity);
                return;
            }

            if (!intent.BudgetSucceeded)
            {
                intent.BudgetSucceeded = true;
                intent.DomainApplied = false;
                intent.DomainRejected = false;
            }

            if (!intent.ProgramCommitted)
            {
                if (!intent.InstallQueued)
                    QueueInstallCommands(ref intent);

                if (intent.InstallCommandCount == 0)
                {
                    EmitNoTargetsFailure(durableEcb, ref intent, ref phase, hasMeta, meta);
                    if (destroyAfterTerminal)
                        ecb.DestroyEntity(entity);
                    return;
                }

                if (!VerifyInstallReceipts(ref intent))
                    return;

                CommitVerifiedInstall(ref intent, durableEcb);
            }

            if (intent.ProgramCommitted)
                EnsureKickbackRequestDurable(ref intent, durableEcb);

            if (intent.ProgramCommitted
                && !intent.TerminalEmitted
                && (intent.KickbackRequestDurable || intent.EffectiveKickback <= 0))
            {
                if (hasMeta)
                    RequestResultEmitter.EmitSuccess(durableEcb, meta, RequestKind.Modernization, m_Host.ElapsedTime);
                intent.TerminalEmitted = true;
                intent.DomainApplied = true;
                phase.Value = PendingPhaseValue.Applied;
            }

            if (intent.ProgramCommitted
                && intent.TerminalEmitted
                && (intent.KickbackRequestDurable || intent.EffectiveKickback <= 0)
                && destroyAfterTerminal)
            {
                ecb.DestroyEntity(entity);
            }
        }

        public void ReconcileAfterLoad(ref DistrictModernizationIntent intent, ref PendingPhase phase, EntityCommandBuffer durableEcb)
        {
            ResolveOperationKey(ref intent);

            if (intent.BudgetSucceeded && !intent.ProgramCommitted)
            {
                if (!VerifyInstallReceipts(ref intent))
                {
                    intent.InstallQueued = false;
                    intent.InstallVerified = false;
                    intent.InstallCommandCount = 0;
                    intent.ActualInstalled = 0;
                    return;
                }

                CommitVerifiedInstall(ref intent, durableEcb);
            }

            if (intent.ProgramCommitted && intent.TerminalEmitted)
                phase.Value = PendingPhaseValue.Applied;
        }

        private void QueueInstallCommands(ref DistrictModernizationIntent durableIntent)
        {
            var intent = ToRuntimeIntent(durableIntent);

            bool isCorrupt = intent.Contractor == ContractorType.YourGuy;

            var wallet = m_Host.ResolveWalletService();
            durableIntent.EffectiveKickback = (isCorrupt && (!wallet.IsOperational || wallet.IsFrozen))
                ? 0
                : intent.Kickback;

#pragma warning disable CIVIC145 // ECB used unconditionally in both isCorrupt branches below
            var ecb = m_Host.CreateCommandBuffer();
#pragma warning restore CIVIC145

            m_Cleanup.ClearScratch();

            if (intent.ReplacingCorrupt)
            {
                m_Cleanup.PreScan(
                    intent.DistrictIndex,
                    m_Host.CounterfeitQuery,
                    m_Host.CurrentDistrictLookup,
                    m_Host.BackupPowerLinks,
                    collectEntities: true);
                m_Cleanup.QueueExactCleanup(
                    ecb,
                    m_Host.CounterfeitBatteryLookup,
                    m_Host.BackupPowerLookup,
                    m_Host.DeletedLookup);
                Log.Info($"District {intent.DistrictIndex}: queued counterfeit cleanup before {intent.Contractor} install");
            }

            int commandCount = isCorrupt
                ? m_Installer.InstallCounterfeit(
                    durableIntent.OperationKey,
                    intent.DistrictIndex,
                    intent.BuildingCount,
                    intent.ActivationDay,
                    intent.TotalCost,
                    ecb,
                    m_Host.BuildingsWithDistrictQuery,
                    m_Host.CurrentDistrictLookup,
                    m_Host.BackupPowerLinks)
                : m_Installer.InstallHonest(
                    durableIntent.OperationKey,
                    intent.DistrictIndex,
                    intent.BuildingCount,
                    intent.ActivationDay,
                    intent.TotalCost,
                    ecb,
                    m_Host.BuildingsWithDistrictQuery,
                    m_Host.CurrentDistrictLookup,
                    m_Host.BackupPowerLinks);

            durableIntent.InstallQueued = true;
            durableIntent.InstallCommandCount = commandCount;
            durableIntent.ActualInstalled = 0;
            durableIntent.InstallVerified = false;

            m_Host.RegisterECBProducer();
            m_Cleanup.ClearScratch();
        }

        private bool VerifyInstallReceipts(ref DistrictModernizationIntent intent)
        {
            if (intent.InstallCommandCount <= 0)
                return false;

            int backupReceipts = 0;
            int counterfeitReceipts = 0;
            string operationKey = intent.OperationKey.ToString();
            var receipts = m_Host.ModernizationInstallReceiptQuery.ToComponentDataArray<ModernizationInstallReceipt>(Allocator.Temp);
            try
            {
                for (int i = 0; i < receipts.Length; i++)
                {
                    var receipt = receipts[i];
                    if (receipt.OperationKey.ToString() != operationKey
                        || receipt.DistrictId != intent.DistrictId
                        || receipt.Contractor != intent.Contractor
                        || receipt.ActivationDay != intent.ActivationDay
                        || receipt.TotalCost != intent.TotalCost)
                    {
                        continue;
                    }

                    if (receipt.Kind == ModernizationReceiptKind.BackupPower)
                        backupReceipts++;
                    else if (receipt.Kind == ModernizationReceiptKind.CounterfeitBattery)
                        counterfeitReceipts++;
                    else
                    {
                        // Other receipt kinds do not participate in install verification counts.
                    }
                }
            }
            finally
            {
                if (receipts.IsCreated) receipts.Dispose();
            }

            bool isCorrupt = intent.Contractor == ContractorType.YourGuy;
            bool verified = isCorrupt
                ? backupReceipts >= intent.InstallCommandCount && counterfeitReceipts >= intent.InstallCommandCount
                : backupReceipts >= intent.InstallCommandCount && counterfeitReceipts == 0;

            if (!verified)
            {
                intent.InstallVerified = false;
                return false;
            }

            intent.InstallVerified = true;
            intent.ActualInstalled = backupReceipts;
            return true;
        }

        private void CommitVerifiedInstall(ref DistrictModernizationIntent durableIntent, EntityCommandBuffer durableEcb)
        {
            if (!durableIntent.InstallVerified || durableIntent.ActualInstalled <= 0)
                return;

            var intent = ToRuntimeIntent(durableIntent);

            var spCfg = BalanceConfig.Current.ShadowProcurement;
            bool isCorrupt = intent.Contractor == ContractorType.YourGuy;
            int actualInstalled = durableIntent.ActualInstalled;
            int effectiveKickback = durableIntent.EffectiveKickback;

            if (isCorrupt)
            {
                EnsureKickbackRequestDurable(ref durableIntent, durableEcb);

                m_Host.ReputationService.ModifyTrust(spCfg.ReputationCorrupt, "District Modernization (corrupt)");
                Log.Info($"District {intent.DistrictIndex}: CORRUPT confirmed, {actualInstalled} buildings, kickback ${effectiveKickback}");
            }
            else
            {
                durableIntent.KickbackRequestDurable = true;
                m_Host.ReputationService.ModifyTrust(spCfg.ReputationHonest, "District Modernization (honest)");
                Log.Info($"District {intent.DistrictIndex}: Honest confirmed, {actualInstalled} buildings");
            }

            // FIX W1-M9: Preserve fire investigation history across re-modernization
            int prevFireCount = 0;
            int prevLastFire = intent.ActivationDay;
            int prevKickbackEarned = 0;
            if (m_Store.TryGetProgram(intent.DistrictIndex, out var prevProgram) && prevProgram.HasProgram)
            {
                prevFireCount = prevProgram.FireCount;
                prevLastFire = prevProgram.LastFireDay;
                prevKickbackEarned = prevProgram.KickbackEarned;
            }

            // FIX W9-M6: Use actual installed count — buildings may have changed between intent and confirm
            m_Store.SetProgram(intent.DistrictIndex, new DistrictModernizationData
            {
                HasProgram = true,
                Contractor = intent.Contractor,
                ActivationDay = intent.ActivationDay,
                BuildingCount = actualInstalled,
                TotalCost = intent.TotalCost,
                KickbackEarned = prevKickbackEarned + effectiveKickback,
                ExpectedKickback = intent.Kickback,
                LastFireDay = prevLastFire,
                FireCount = prevFireCount
            });

            LastProcurementDay = intent.ActivationDay;
            durableIntent.ProgramCommitted = true;
            durableIntent.DomainApplied = true;

            m_Host.EventBus?.SafePublish(new ShadowNarrativeEvent(
                ShadowNarrativeEventType.Procurement,
                DistrictIndex: intent.DistrictIndex,
                BuildingCount: actualInstalled,
                Cost: intent.TotalCost,
                IsCorrupt: isCorrupt,
                KickbackAmount: effectiveKickback
            ), "DistrictModernizationSystem");
        }

        private void EnsureKickbackRequestDurable(ref DistrictModernizationIntent intent, EntityCommandBuffer durableEcb)
        {
            if (intent.KickbackRequestDurable)
                return;

            if (intent.Contractor != ContractorType.YourGuy || intent.EffectiveKickback <= 0)
            {
                intent.KickbackRequestDurable = true;
                return;
            }

            string incomeKey = KickbackOperationKey(intent.OperationKey, intent.ActualInstalled);
            if (EnsureKickbackRequestDurable(durableEcb, intent.EffectiveKickback, incomeKey))
                intent.KickbackRequestDurable = true;
        }

        private bool EnsureKickbackRequestDurable(EntityCommandBuffer durableEcb, int amount, string operationKey)
        {
            if (amount <= 0 || string.IsNullOrEmpty(operationKey))
                return true;

            var wallet = m_Host.ResolveWalletService();
            if (!wallet.IsOperational || wallet.IsFrozen)
                return false;

            var entity = durableEcb.CreateEntity();
#pragma warning disable CIVIC404 // Phase J: durable terminality requires the retained income request to be materialized before program terminal cleanup; ShadowEconomyEmitter only queues through a later barrier.
            durableEcb.AddComponent(entity, new ShadowIncomeRequest
            {
                Amount = amount,
                Reason = new FixedString64Bytes("DistrictModernization"),
                OperationKey = new FixedString128Bytes(operationKey)
            });
#pragma warning restore CIVIC404
            durableEcb.AddComponent(entity, new RequestMeta
            {
                RequestId = RequestRegistrar.NextRequestId(),
                CreatedTime = m_Host.ElapsedTime,
                CreatedFrame = 0u,
                DiscriminatorKind = new FixedString32Bytes(nameof(ShadowIncomeRequest)),
                DiscriminatorValue = new FixedString64Bytes("ModernizationKickback")
            });
            return true;
        }

        private void EmitNoTargetsFailure(
            EntityCommandBuffer durableEcb,
            ref DistrictModernizationIntent intent,
            ref PendingPhase phase,
            bool hasMeta,
            RequestMeta meta)
        {
            bool isCorrupt = intent.Contractor == ContractorType.YourGuy;
            Log.Warn($"District {intent.DistrictId}: 0 buildings available at install queue time — procurement void");
            m_Host.EventBus?.SafePublish(new ShadowNarrativeEvent(
                ShadowNarrativeEventType.ProcurementFailed,
                DistrictIndex: intent.DistrictId,
                BuildingCount: 0,
                Cost: intent.TotalCost,
                IsCorrupt: isCorrupt,
                KickbackAmount: 0
            ), "DistrictModernizationSystem");

            intent.DomainRejected = true;
            intent.TerminalEmitted = true;
            intent.KickbackRequestDurable = true;
            phase.Value = PendingPhaseValue.Confirmed;
            if (hasMeta)
                RequestResultEmitter.Emit(durableEcb, meta, RequestKind.Modernization, RequestStatus.Failed, ReasonIds.BackupModernizationNoTargets, m_Host.ElapsedTime);
        }

        /// <summary>Clear pending intent — no equipment was installed, nothing to undo.
        /// Publishes ProcurementFailed narrative so UI can notify the player.</summary>
        private void RollbackProcurement(DistrictModernizationIntent durableIntent)
        {
            var intent = ToRuntimeIntent(durableIntent);

            m_Host.EventBus?.SafePublish(new ShadowNarrativeEvent(
                ShadowNarrativeEventType.ProcurementFailed,
                DistrictIndex: intent.DistrictIndex,
                BuildingCount: intent.BuildingCount,
                Cost: intent.TotalCost,
                IsCorrupt: intent.Contractor == ContractorType.YourGuy,
                KickbackAmount: intent.Kickback
            ), "DistrictModernizationSystem");

            Log.Warn($"Procurement rollback: district {intent.DistrictIndex}, ${intent.TotalCost} — budget insufficient");
        }

        private static string OperationKey(
            int districtIndex,
            ContractorType contractor,
            int activationDay,
            int buildingCount,
            int totalCost)
            => $"Modernization:v1:{districtIndex}:{(byte)contractor}:{activationDay}:{buildingCount}:{totalCost}";

        private static string KickbackOperationKey(FixedString128Bytes operationKey, int actualInstalled)
            => $"ModernizationKickback:{operationKey.ToString()}:{actualInstalled}";

        private static void ResolveOperationKey(ref DistrictModernizationIntent intent)
        {
            if (intent.OperationKey.Length != 0)
                return;

            intent.OperationKey = new FixedString128Bytes(OperationKey(
                intent.DistrictId,
                intent.Contractor,
                intent.ActivationDay,
                intent.BuildingCount,
                intent.TotalCost));
        }

        /// <summary>Remove programs for districts that no longer have any buildings.
        /// For each removed corrupt district, queue counterfeit cleanup.</summary>
        public void CleanupStaleDistricts()
        {
            if (m_Store.Count == 0) return;

            var activeDistricts = new NativeHashSet<int>(32, Allocator.Temp);
            var entities = m_Host.BuildingsWithDistrictQuery.ToEntityArray(Allocator.Temp);
            try
            {
                var currentDistrictLookup = m_Host.CurrentDistrictLookup;
                for (int i = 0; i < entities.Length; i++)
                {
                    if (!currentDistrictLookup.TryGetComponent(entities[i], out var district))
                        continue;
#pragma warning disable CIVIC097 // CurrentDistrict.m_District.Index is a logical district id, not an entity index.
                    activeDistricts.Add(district.m_District.Index);
#pragma warning restore CIVIC097
                }
            }
            finally
            {
                if (entities.IsCreated) entities.Dispose();
            }

            m_StaleKeys.Clear();
            foreach (var kvp in m_Store.Enumerate())
            {
                if (!activeDistricts.Contains(kvp.Key))
                    m_StaleKeys.Add(kvp.Key);
            }
            if (activeDistricts.IsCreated) activeDistricts.Dispose();

            if (m_StaleKeys.Count == 0)
                return;

            for (int i = 0; i < m_StaleKeys.Count; i++)
            {
                int key = m_StaleKeys[i];
                bool wasCorrupt = m_Store.TryGetProgram(key, out var program) && program.Contractor == ContractorType.YourGuy;
                m_Store.RemoveProgram(key);   // self-publishes
                if (wasCorrupt)
                {
                    if (m_Cleanup.MarkDistrictPending(key, m_Host.CounterfeitQuery, m_Host.DeletedLookup))
                        m_Store.Publish();
                }
                if (Log.IsDebugEnabled) Log.Debug($"Cleaned up stale program for district {key}");
            }
        }

        private static ProcurementIntent ToRuntimeIntent(DistrictModernizationIntent intent)
        {
            return new ProcurementIntent
            {
                DistrictIndex = intent.DistrictId,
                Contractor = intent.Contractor,
                BuildingCount = intent.BuildingCount,
                TotalCost = intent.TotalCost,
                Kickback = intent.Kickback,
                ReplacingCorrupt = intent.ReplacingCorrupt,
                ActivationDay = intent.ActivationDay
            };
        }

        private struct ProcurementIntent
        {
            public int DistrictIndex;
            public ContractorType Contractor;
            public int BuildingCount;
            public int TotalCost;
            public int Kickback;
            public bool ReplacingCorrupt;
            public int ActivationDay;
        }
    }
}
