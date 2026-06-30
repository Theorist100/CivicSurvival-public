using System;
using System.Collections.Generic;
using Colossal.UI.Binding;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.UI.DomainState;

namespace CivicSurvival.Core.UI
{
    public delegate TriggerOutcome ScenarioTriggerHandler(in ScenarioGuard guard);
    public delegate TriggerOutcome ScenarioTriggerHandler<in T>(in ScenarioGuard guard, T arg);
    public delegate TriggerOutcome ScenarioTriggerHandler<in T1, in T2>(in ScenarioGuard guard, T1 arg1, T2 arg2);

    public sealed class TriggerRegistry : IDisposable
    {
        public delegate bool ScenarioGuardFactory(out ScenarioGuard guard);

        private readonly string _modName;
        private readonly List<IBinding> _triggers = new();
        private readonly HashSet<string> _names = new();
        private ScenarioGuardFactory _scenarioGuardFactory = null!;

        public TriggerRegistry(string modName = "CivicSurvival")
        {
            _modName = modName;
        }

        public void SetScenarioGuardFactory(ScenarioGuardFactory factory)
        {
            _scenarioGuardFactory = factory;
        }

        public TriggerRegistry Add(string name, FeatureId gate, Action handler)
        {
            AddNameOrThrow(name);
            _triggers.Add(new TriggerBinding(_modName, name, () =>
            {
                if (IsGateOpen(gate))
                    handler();
            }));
            return this;
        }

        public TriggerRegistry Add(string name, FeatureId gate, string resultKey, Func<TriggerOutcome> handler)
        {
            AddNameOrThrow(name);
            _triggers.Add(new TriggerBinding(_modName, name, () =>
                InvokeGated(name, gate, resultKey, () => TriggerDispatch.Invoke(resultKey, name, handler))));
            return this;
        }

        public TriggerRegistry Add(
            string name,
            FeatureId gate,
            string resultKey,
            ActionKey actionKey,
            Func<ActionContext> contextFactory,
            Func<TriggerOutcome> handler)
        {
            AddNameOrThrow(name);
            _triggers.Add(new TriggerBinding(_modName, name, () =>
                InvokeGated(name, gate, resultKey, () =>
                    TriggerDispatch.Invoke(resultKey, name, actionKey, contextFactory, handler))));
            return this;
        }

        public TriggerRegistry Add<T>(string name, FeatureId gate, Action<T> handler)
        {
            AddNameOrThrow(name);
            _triggers.Add(new TriggerBinding<T>(_modName, name, arg =>
            {
                if (IsGateOpen(gate))
                    handler(arg);
            }));
            return this;
        }

        public TriggerRegistry Add<T>(string name, FeatureId gate, string resultKey, Func<T, TriggerOutcome> handler)
        {
            AddNameOrThrow(name);
            _triggers.Add(new TriggerBinding<T>(_modName, name, arg =>
                InvokeGated(name, gate, resultKey, () => TriggerDispatch.Invoke(resultKey, name, () => handler(arg)))));
            return this;
        }

        public TriggerRegistry Add<T>(
            string name,
            FeatureId gate,
            string resultKey,
            ActionKey actionKey,
            Func<T, ActionContext> contextFactory,
            Func<T, TriggerOutcome> handler)
        {
            AddNameOrThrow(name);
            _triggers.Add(new TriggerBinding<T>(_modName, name, arg =>
                InvokeGated(name, gate, resultKey, () =>
                    TriggerDispatch.Invoke(resultKey, name, actionKey, () => contextFactory(arg), () => handler(arg)))));
            return this;
        }

        public TriggerRegistry Add<T>(
            string name,
            FeatureId gate,
            string resultKey,
            ActionKey actionKey,
            Func<ActionContext> contextFactory,
            Func<T, TriggerOutcome> handler)
        {
            AddNameOrThrow(name);
            _triggers.Add(new TriggerBinding<T>(_modName, name, arg =>
                InvokeGated(name, gate, resultKey, () =>
                    TriggerDispatch.Invoke(resultKey, name, actionKey, contextFactory, () => handler(arg)))));
            return this;
        }

        public TriggerRegistry Add<T1, T2>(string name, FeatureId gate, Action<T1, T2> handler)
        {
            AddNameOrThrow(name);
            _triggers.Add(new TriggerBinding<T1, T2>(_modName, name, (arg1, arg2) =>
            {
                if (IsGateOpen(gate))
                    handler(arg1, arg2);
            }));
            return this;
        }

        public TriggerRegistry Add<T1, T2>(string name, FeatureId gate, string resultKey, Func<T1, T2, TriggerOutcome> handler)
        {
            AddNameOrThrow(name);
            _triggers.Add(new TriggerBinding<T1, T2>(_modName, name, (arg1, arg2) =>
                InvokeGated(name, gate, resultKey, () => TriggerDispatch.Invoke(resultKey, name, () => handler(arg1, arg2)))));
            return this;
        }

        public TriggerRegistry Add<T1, T2>(
            string name,
            FeatureId gate,
            string resultKey,
            ActionKey actionKey,
            Func<T1, T2, ActionContext> contextFactory,
            Func<T1, T2, TriggerOutcome> handler)
        {
            AddNameOrThrow(name);
            _triggers.Add(new TriggerBinding<T1, T2>(_modName, name, (arg1, arg2) =>
                InvokeGated(name, gate, resultKey, () =>
                    TriggerDispatch.Invoke(resultKey, name, actionKey, () => contextFactory(arg1, arg2), () => handler(arg1, arg2)))));
            return this;
        }

        public TriggerRegistry Add<T1, T2>(
            string name,
            FeatureId gate,
            string resultKey,
            ActionKey actionKey,
            Func<ActionContext> contextFactory,
            Func<T1, T2, TriggerOutcome> handler)
        {
            AddNameOrThrow(name);
            _triggers.Add(new TriggerBinding<T1, T2>(_modName, name, (arg1, arg2) =>
                InvokeGated(name, gate, resultKey, () =>
                    TriggerDispatch.Invoke(resultKey, name, actionKey, contextFactory, () => handler(arg1, arg2)))));
            return this;
        }

        public TriggerRegistry Add<T1, T2, T3>(string name, FeatureId gate, Action<T1, T2, T3> handler)
        {
            AddNameOrThrow(name);
            _triggers.Add(new TriggerBinding<T1, T2, T3>(_modName, name, (arg1, arg2, arg3) =>
            {
                if (IsGateOpen(gate))
                    handler(arg1, arg2, arg3);
            }));
            return this;
        }

        public TriggerRegistry Add<T1, T2, T3>(string name, FeatureId gate, string resultKey, Func<T1, T2, T3, TriggerOutcome> handler)
        {
            AddNameOrThrow(name);
            _triggers.Add(new TriggerBinding<T1, T2, T3>(_modName, name, (arg1, arg2, arg3) =>
                InvokeGated(name, gate, resultKey, () => TriggerDispatch.Invoke(resultKey, name, () => handler(arg1, arg2, arg3)))));
            return this;
        }

        public TriggerRegistry Add<T1, T2, T3>(
            string name,
            FeatureId gate,
            string resultKey,
            ActionKey actionKey,
            Func<ActionContext> contextFactory,
            Func<T1, T2, T3, TriggerOutcome> handler)
        {
            AddNameOrThrow(name);
            _triggers.Add(new TriggerBinding<T1, T2, T3>(_modName, name, (arg1, arg2, arg3) =>
                InvokeGated(name, gate, resultKey, () =>
                    TriggerDispatch.Invoke(resultKey, name, actionKey, contextFactory, () => handler(arg1, arg2, arg3)))));
            return this;
        }

        public TriggerRegistry AddWarTrigger(string name, FeatureId gate, string resultKey, ScenarioTriggerHandler handler)
        {
            return Add(name, gate, resultKey, () => InvokeWar(handler));
        }

        public TriggerRegistry AddWarTrigger<T>(string name, FeatureId gate, string resultKey, ScenarioTriggerHandler<T> handler)
        {
            return Add<T>(name, gate, resultKey, arg => InvokeWar((in ScenarioGuard guard) => handler(in guard, arg)));
        }

        public TriggerRegistry AddScenarioTrigger(string name, FeatureId gate, Act requiredAct, string resultKey, ScenarioTriggerHandler handler)
        {
            return Add(name, gate, resultKey, () => InvokeRequiredAct(requiredAct, handler));
        }

        public TriggerRegistry AddScenarioTrigger<T>(string name, FeatureId gate, Act requiredAct, string resultKey, ScenarioTriggerHandler<T> handler)
        {
            return Add<T>(name, gate, resultKey, arg => InvokeRequiredAct(requiredAct, (in ScenarioGuard guard) => handler(in guard, arg)));
        }

        public TriggerRegistry AddPhaseSafeTrigger<T>(
            string name,
            FeatureId gate,
            string resultKey,
            ActionKey actionKey,
            Func<ActionContext> contextFactory,
            ScenarioTriggerHandler<T> handler)
        {
            return Add<T>(
                name,
                gate,
                resultKey,
                actionKey,
                contextFactory,
                arg => InvokePhaseSafe((in ScenarioGuard guard) => handler(in guard, arg)));
        }

        public TriggerRegistry AddPhaseSafeTrigger<T1, T2>(
            string name,
            FeatureId gate,
            string resultKey,
            ActionKey actionKey,
            Func<ActionContext> contextFactory,
            ScenarioTriggerHandler<T1, T2> handler)
        {
            return Add<T1, T2>(
                name,
                gate,
                resultKey,
                actionKey,
                contextFactory,
                (arg1, arg2) => InvokePhaseSafe((in ScenarioGuard guard) => handler(in guard, arg1, arg2)));
        }

        public TriggerRegistry AddUngated(string name, Action handler)
        {
            AddNameOrThrow(name);
            _triggers.Add(new TriggerBinding(_modName, name, handler));
            return this;
        }

        public TriggerRegistry AddUngated<T>(string name, Action<T> handler)
        {
            AddNameOrThrow(name);
            _triggers.Add(new TriggerBinding<T>(_modName, name, handler));
            return this;
        }

        public TriggerRegistry AddUngated<T1, T2>(string name, Action<T1, T2> handler)
        {
            AddNameOrThrow(name);
            _triggers.Add(new TriggerBinding<T1, T2>(_modName, name, handler));
            return this;
        }

        public TriggerRegistry AddUngated<T1, T2, T3>(string name, Action<T1, T2, T3> handler)
        {
            AddNameOrThrow(name);
            _triggers.Add(new TriggerBinding<T1, T2, T3>(_modName, name, handler));
            return this;
        }

        public void RegisterAll(IBindingRegistrar registrar)
        {
            foreach (var trigger in _triggers)
                registrar.AddBinding(trigger);
        }

        public IEnumerable<IBinding> All => _triggers;
        public int Count => _triggers.Count;

        public void Dispose()
        {
            _triggers.Clear();
            _names.Clear();
        }

        private static void InvokeGated(string triggerName, FeatureId gate, string resultKey, Action dispatch)
        {
            if (IsGateOpen(gate))
            {
                dispatch();
                return;
            }

            Mod.Log.Warn($"[TriggerRegistry] Rejected trigger '{triggerName}' because feature '{gate}' is closed");
            RequestResultBridge.RejectNew(resultKey, ReasonIds.ToggleFeatureClosed);
        }

        private TriggerOutcome InvokeWar(ScenarioTriggerHandler handler)
        {
            if (!TryRequireScenarioGuard(out var guard, out var reasonId))
                return TriggerOutcome.RejectRuntime(reasonId);

            if (guard.Act == Act.PreWar)
                return TriggerOutcome.Reject(ReasonIds.PreWarLocked);

            return handler(in guard);
        }

        private TriggerOutcome InvokeRequiredAct(Act requiredAct, ScenarioTriggerHandler handler)
        {
            if (!TryRequireScenarioGuard(out var guard, out var reasonId))
                return TriggerOutcome.RejectRuntime(reasonId);

            if (guard.Act < requiredAct)
                return TriggerOutcome.Reject(ReasonIds.ActLockedFor(requiredAct));

            return handler(in guard);
        }

        private TriggerOutcome InvokePhaseSafe(ScenarioTriggerHandler handler)
        {
            if (!TryRequireScenarioGuard(out var guard, out var reasonId))
                return TriggerOutcome.RejectRuntime(reasonId);

            if (guard.Phase == GamePhase.Attack || guard.Phase == GamePhase.Alert)
                return TriggerOutcome.Reject(ReasonIds.RepairBlockedDuringWave);

            return handler(in guard);
        }

        private bool TryRequireScenarioGuard(out ScenarioGuard guard, out string reasonId)
        {
            if (_scenarioGuardFactory == null || !_scenarioGuardFactory(out guard))
            {
                guard = default;
                reasonId = ReasonIds.ScenarioUnavailable;
                return false;
            }

            reasonId = string.Empty;
            return true;
        }

        private static bool IsGateOpen(FeatureId gate)
        {
            if (gate.IsEmpty)
                return true;

            // Lifecycle layer: gameplay features need a loaded city. Menu-safe
            // surface (settings panel, global toasts, JS log, mod settings) keeps
            // working in cold-boot menu via the shared "UI" feature id — Settings,
            // Toast and UI all map to the same FeatureId.Of("UI"), so a single
            // comparison covers the entire menu-safe whitelist.
            if (gate != FeatureIds.UI && !CivicGameLifecycle.IsGameplayReady)
                return false;

            if (!FeatureRegistry.IsInitialized)
                return true;

            return FeatureRegistry.Instance.IsAvailable(gate);
        }

        private void AddNameOrThrow(string name)
        {
            if (!_names.Add(name))
                throw new InvalidOperationException($"Trigger '{name}' already registered in TriggerRegistry");
        }
    }
}
