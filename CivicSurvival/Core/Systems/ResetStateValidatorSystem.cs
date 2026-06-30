#if DEBUG
using System;
using System.Collections.Generic;
using System.Reflection;
using Colossal.Serialization.Entities;
using Game;
using Game.Simulation;
using Unity.Entities;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Core.Systems
{
    /// <summary>
    /// DEBUG-only lint system: warns if any CivicSurvival system implements
    /// IDefaultSerializable but neither IResettable nor IBootDefaultsReset
    /// (forgotten state reset risk).
    /// Runs once on startup, then disables itself.
    /// </summary>
    // Registered in SystemRegistrar (DEBUG block) — class-level UpdateInGroup attribute alone does NOT
    // auto-create a mod GameSystemBase in CS2 (W2 row 421). OrderLast keeps the
    // one-shot lint after sibling systems have constructed.
    public partial class ResetStateValidatorSystem : GameSystemBase
    {
        private static readonly LogContext Log = new("ResetStateValidator");
        private bool m_HasRun;

        protected override void OnUpdate()
        {
            if (m_HasRun)
            {
                Enabled = false;
                return;
            }
            m_HasRun = true;

            var ourAssembly = typeof(ResetStateValidatorSystem).Assembly;
            var serializableType = typeof(IDefaultSerializable);
            var resettableType = typeof(IResettable);
            var bootDefaultsResetType = typeof(IBootDefaultsReset);

#pragma warning disable CIVIC050 // One-shot system (runs once, then disables)
            var violations = new List<Type>();
#pragma warning restore CIVIC050
            try
            {
                foreach (var t in ourAssembly.GetTypes())
                    ScanType(t, serializableType, resettableType, bootDefaultsResetType, violations);
            }
            catch (ReflectionTypeLoadException ex)
            {
                Log.Warn($"LINT: ReflectionTypeLoadException — {ex.LoaderExceptions.Length} type(s) failed to load. Scanning loaded subset.");
                foreach (var loaderException in ex.LoaderExceptions)
                {
                    if (loaderException != null)
                        Log.Warn($"LINT: Type load failure: {loaderException}");
                }

                foreach (var t in ex.Types)
                    ScanType(t, serializableType, resettableType, bootDefaultsResetType, violations);
            }

            if (violations.Count == 0)
            {
                if (Log.IsDebugEnabled) Log.Debug($"All IDefaultSerializable types implement IResettable or IBootDefaultsReset");
                return;
            }

            foreach (var type in violations)
            {
                Log.Warn($"LINT: {type.Name} implements IDefaultSerializable but neither IResettable nor IBootDefaultsReset — state may leak between loads");
            }

            Log.Warn($"LINT: {violations.Count} type(s) missing reset recovery. See above.");
        }

        private static void ScanType(
            Type? t,
            Type serializableType,
            Type resettableType,
            Type bootDefaultsResetType,
            List<Type> violations)
        {
            if (t == null || t.IsAbstract || t.IsInterface) return;
            if (!serializableType.IsAssignableFrom(t)) return;
            if (resettableType.IsAssignableFrom(t)) return;
            if (bootDefaultsResetType.IsAssignableFrom(t)) return;

            violations.Add(t);
        }
    }
}
#endif
