using System;
using System.Reflection;
using Colossal.Serialization.Entities;
using Game;
using Game.Simulation;
using HarmonyLib;
using Unity.Entities;
using Unity.Mathematics;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Core.Systems
{
    /// <summary>
    /// World-owned host for <see cref="CivicSurvival.Patches.CrisisEconomicsPatch.TaxPatch"/>.
    /// Holds the per-World saved tax multiplier and last-touched <c>TaxSystem</c>
    /// ref. OnDestroy restores the original multiplier on the dying world.
    /// </summary>
    [FrameworkSystem]
    [ActIndependent]
    public partial class TaxPatchHandlerSystem : GameSystemBase
    {
        private static readonly LogContext Log = new("TaxPatchHandler");

        private FieldInfo? m_TaxPaidMultiplierField;
        private volatile bool m_FieldCached;
        private volatile bool m_FieldValid;
        private readonly object m_CacheLock = new();
        private volatile bool m_HasSavedMultiplier;
        private float3 m_SavedMultiplier;
        private TaxSystem? m_LastInstance;

        protected override void OnCreate() => base.OnCreate();

        protected override void OnGamePreload(Purpose purpose, GameMode mode)
        {
            base.OnGamePreload(purpose, mode);

            // Vanilla GameManager.Save does NOT invoke onGamePreload (decompile
            // Game.SceneFlow/GameManager.cs:904-912 vs :1000-1007); only the load
            // path reaches this method. No need to gate on purpose.
            _ = purpose;
            RestoreSavedMultiplier();
        }

        protected override void OnDestroy()
        {
            RestoreSavedMultiplier();
            base.OnDestroy();
        }

        /// <summary>
        /// Restore original tax multiplier on the captured TaxSystem and clear cache.
        /// Called from OnDestroy (normal world teardown) and from
        /// <c>CrisisEconomicsPatch.Cleanup</c> (mod hot-reload while world alive).
        /// Idempotent.
        /// </summary>
        internal void RestoreSavedMultiplier()
        {
            lock (m_CacheLock)
            {
                if (m_HasSavedMultiplier && m_LastInstance != null && m_TaxPaidMultiplierField != null)
                {
                    try { m_TaxPaidMultiplierField.SetValue(m_LastInstance, m_SavedMultiplier); }
                    catch (Exception ex) { Log.Warn($"RestoreSavedMultiplier failed: {ex}"); }
                }
                m_TaxPaidMultiplierField = null;
                m_FieldCached = false;
                m_FieldValid = false;
                m_HasSavedMultiplier = false;
                m_SavedMultiplier = default;
                m_LastInstance = null;
            }
        }

        protected override void OnUpdate() { /* handler is event-driven via ApplyTaxMultiplier */ }

        /// <summary>
        /// Called from the Harmony bridge in <c>CrisisEconomicsPatch.TaxPatch.Prefix</c>.
        /// Reflectively writes <c>TaxSystem.m_TaxPaidMultiplier</c>, saving/restoring the
        /// original value across crisis boundaries.
        /// </summary>
        internal void ApplyTaxMultiplier(TaxSystem instance, float multiplier)
        {
            // FIX TOCTOU: Double-checked locking pattern for thread-safe cache initialization
            if (!m_FieldCached)
            {
                bool logCacheFailure = false;
                lock (m_CacheLock)
                {
                    if (!m_FieldCached)
                    {
                        m_TaxPaidMultiplierField = AccessTools.Field(typeof(TaxSystem), "m_TaxPaidMultiplier");
                        m_FieldValid = m_TaxPaidMultiplierField != null;
                        m_FieldCached = true;
                        logCacheFailure = !m_FieldValid;
                    }
                }
                if (logCacheFailure)
                    Log.Error("Could not find m_TaxPaidMultiplier field in TaxSystem!");
            }

            var newMultiplier = new float3(multiplier, multiplier, multiplier);
            lock (m_CacheLock)
            {
                FieldInfo? field = m_FieldValid ? m_TaxPaidMultiplierField : null;
                if (field == null)
                    return;

                if (multiplier >= 1f)
                {
                    if (m_HasSavedMultiplier)
                    {
                        field.SetValue(instance, m_SavedMultiplier);
                        m_HasSavedMultiplier = false;
                        m_LastInstance = null;
                    }
                    return;
                }

                if (!m_HasSavedMultiplier && field.GetValue(instance) is float3 currentMultiplier)
                {
                    m_SavedMultiplier = currentMultiplier;
                    m_HasSavedMultiplier = true;
                    m_LastInstance = instance;
                }

#pragma warning disable CIVIC114
                field.SetValue(instance, newMultiplier);
#pragma warning restore CIVIC114
            }
        }
    }
}
