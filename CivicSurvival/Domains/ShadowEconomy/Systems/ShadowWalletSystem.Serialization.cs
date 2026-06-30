using Colossal.Serialization.Entities;
using Unity.Entities;
using CivicSurvival.Core.Components.Domain.Economy;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Serialization;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Domains.ShadowEconomy.Systems
{
    /// <summary>
    /// ShadowWalletSystem - Save/Load serialization (IDefaultSerializable).
    /// Persists wallet balance, frozen state, and locked operations.
    /// </summary>
    #pragma warning disable CIVIC223 // PostLoadValidationSystem invokes ICivicSingletonOwner.OnLoadRestore; Deserialize only buffers payload.
    public partial class ShadowWalletSystem : IDefaultSerializable, IResettable, IBootDefaultsReset
    {
        public void ResetToBootDefaults(ResetReason reason)
        {
            m_HasRestoredWallet = false;

            lock (m_WalletLock)
            {
                m_LockedOperations.Clear();
                m_AppliedIncomeKeys.Clear();
            }
            m_ActiveSlotIdsScratch.Clear();
            m_AppliedIncomeEvents.Clear();
            m_DeductEvents.Clear();
            ResetPendingDeductions();
            InitializeGate();
        }

        [System.NonSerialized] private ShadowWalletSingleton m_RestoredWallet;
        [System.NonSerialized] private bool m_HasRestoredWallet;

        public void SetDefaults(Context context)
            => ResetState();

        void IResettable.ResetState() => ResetState();

        private void ResetState()
        {
            ResetManagedState();
            ResetWalletSingletonToDefault();
        }

    #pragma warning restore CIVIC223
        private void ResetManagedState()
        {
            m_HasRestoredWallet = false;

            lock (m_WalletLock)
            {
                m_LockedOperations.Clear();
                m_AppliedIncomeKeys.Clear();
            }
            m_ActiveSlotIdsScratch.Clear();
            m_AppliedIncomeEvents.Clear();
            m_DeductEvents.Clear();
            // S26-H5 FIX: Clear stale pending deductions from previous session
            ResetPendingDeductions();
            InitializeGate();
        }

        private void ResetWalletSingletonToDefault()
        {
            // Reset singleton to prevent stale/garbage data after deser failure
            lock (m_WalletLock)
            {
                if (m_WalletQuery.TryGetSingletonEntity<ShadowWalletSingleton>(out var entity))
                    EntityManager.SetComponentData(entity, ShadowWalletSingleton.Default);
            }
        }

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SaveVersions.GLOBAL);
            try
            {

                lock (m_WalletLock)
                {
                    ShadowWalletSingleton wallet;
                    if (!m_WalletQuery.TryGetSingleton<ShadowWalletSingleton>(out wallet))
                        wallet = ShadowWalletSingleton.Default;

                    var lockedOperations = new ShadowWalletLockedOperationPersistEntry[m_LockedOperations.Count];
                    int index = 0;
                    foreach (var kvp in m_LockedOperations)
                    {
                        lockedOperations[index++] = new ShadowWalletLockedOperationPersistEntry(kvp.Key, kvp.Value);
                    }

                    var state = new ShadowWalletPersistState(
                        wallet.Balance,
                        wallet.TotalIncome,
                        wallet.TotalExpenses,
                        wallet.FreezeReason,
                        wallet.SanctionsMarkup,
                        lockedOperations,
                        AppliedIncomeKeysSnapshot(),
                        wallet.LockedBalance);
                    ShadowWalletCodec.Write(state, writer);
                }

            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
            SerializationGuard.LogSerialized(nameof(ShadowWalletSystem), SaveVersions.GLOBAL);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            lock (m_WalletLock)
            {
                m_LockedOperations.Clear();
                m_AppliedIncomeKeys.Clear();
            }

            if (!SerializationGuard.TryBeginBlock(reader, SaveVersions.GLOBAL, out _, out var block, nameof(ShadowWalletSystem)))
            {
                ResetToBootDefaults(ResetReason.VersionMismatch);
                return;
            }
            try
            {
                ShadowWalletCodec.Read(reader, out var payload);
                if (payload.SkippedLockedOperationCount > 0)
                    Log.Warn($"Skipped {payload.SkippedLockedOperationCount} locked operations with null/empty id");

                lock (m_WalletLock)
                {
                    for (int i = 0; i < payload.LockedOperations.Count; i++)
                    {
                        m_LockedOperations[payload.LockedOperations[i].OperationId] = payload.LockedOperations[i].Amount;
                    }

                    for (int i = 0; i < payload.AppliedIncomeKeys.Count; i++)
                    {
                        var key = payload.AppliedIncomeKeys[i];
                        if (!string.IsNullOrEmpty(key))
                            m_AppliedIncomeKeys.Add(key);
                    }
                }

                // S26-H5 FIX: Clear stale pending deductions on load
                ResetPendingDeductions();

                // FIX S6-05: Initialize m_LastSanctionsActive from deserialized SanctionsMarkup
                m_LastSanctionsActive = payload.SanctionsMarkup > 0f;

                m_RestoredWallet = new ShadowWalletSingleton
                {
                    Balance = payload.Balance,
                    LockedBalance = payload.LockedBalance,
                    TotalIncome = payload.TotalIncome,
                    TotalExpenses = payload.TotalExpenses,
                    FreezeReason = payload.FreezeReason,
                    SanctionsMarkup = payload.SanctionsMarkup
                };
                m_HasRestoredWallet = true;
                InitializeGate();

                bool frozen = payload.FreezeReason != FreezeReason.None;
                Log.Info($"Deserialized: Balance=${payload.Balance:N0}, Locked=${payload.LockedBalance:N0}, Frozen={frozen} ({payload.FreezeReason})");
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

            // Act gate is reconciled by ActGateController in OnUpdateImpl after load.
            // Do not derive operational state during Deserialize.
        }

        public void OnLoadRestore(EntityManager entityManager)
        {
            ShadowWalletSingleton.EnsureExists(entityManager);
            ShadowImportState.EnsureExists(entityManager);

            lock (m_WalletLock)
            {
                if (m_WalletQuery.TryGetSingletonEntity<ShadowWalletSingleton>(out var entity))
                {
                    entityManager.SetComponentData(entity, m_HasRestoredWallet
                        ? m_RestoredWallet
                        : ShadowWalletSingleton.Default);
                }
            }
            m_HasRestoredWallet = false;
        }

        private string[] AppliedIncomeKeysSnapshot()
        {
            var keys = new string[m_AppliedIncomeKeys.Count];
            int index = 0;
            foreach (var key in m_AppliedIncomeKeys)
                keys[index++] = key;
            return keys;
        }
    }
}
