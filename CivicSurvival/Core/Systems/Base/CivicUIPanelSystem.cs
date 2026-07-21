using System;
using System.Diagnostics;
using Colossal.UI.Binding;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Components.Threats;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.UI;
using CivicSurvival.Core.UI.DomainState;
using CivicSurvival.Core.Utils;
using Unity.Entities;

namespace CivicSurvival.Core.Systems.Base
{
    /// <summary>
    /// Base class for UI panel systems migrated from BaseUIPanel.
    ///
    /// Inheritance chain:
    ///   UISystemBase → CivicUISystemBase → ThrottledUISystemBase → CivicUIPanelSystem
    ///
    /// Provides:
    /// - BindingRegistry + TriggerRegistry (same API as BaseUIPanel)
    /// - Throttle (500ms from ThrottledUISystemBase)
    /// - Auto-profiling (from CivicUISystemBase)
    /// - EventBus (lazy-cached from CivicUISystemBase)
    /// - GetEntityQuery (auto-disposed by UISystemBase)
    /// - SystemAPI / ComponentLookup (from SystemBase)
    /// - RequireForUpdate (from SystemBase)
    ///
    /// Migration from BaseUIPanel:
    /// - Constructor logic → OnCreate()
    /// - ConfigureBindings() → ConfigureBindings() (same)
    /// - ConfigureTriggers() → ConfigureTriggers() (same)
    /// - OnUpdate() → OnPanelUpdate()
    /// - Dispose(bool) → OnDestroy() (queries auto-disposed)
    /// - EntityManager.CreateEntityQuery → GetEntityQuery (auto-disposed)
    /// </summary>
    public abstract partial class CivicUIPanelSystem : ThrottledUISystemBase
    {
        protected static readonly ISourceCheck[] NoSourceChecks = Array.Empty<ISourceCheck>();

        private bool m_PanelRegistered;

        /// <summary>
        /// Registry for value bindings. Same API as BaseUIPanel.Bindings.
        /// </summary>
        protected BindingRegistry Bindings { get; private set; } = null!;

        /// <summary>
        /// Registry for trigger bindings. Same API as BaseUIPanel.Triggers.
        /// </summary>
        protected TriggerRegistry Triggers { get; private set; } = null!;

        /// <summary>
        /// Structured logging with panel name prefix.
        /// </summary>
        protected LogContext Log { get; private set; }
        private EntityQuery m_WaveStateQuery;
        protected EntityQuery WaveStateQuery => m_WaveStateQuery;

        protected override void OnCreate()
        {
            base.OnCreate();

            Bindings = new BindingRegistry();
            Triggers = new TriggerRegistry();
            Triggers.SetScenarioGuardFactory(TryCreateScenarioGuard);
            Log = new LogContext(GetType().Name);
            m_WaveStateQuery = GetEntityQuery(ComponentType.ReadOnly<WaveStateSingleton>());
            EnsurePanelRegistered();
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
        }

        private void EnsurePanelRegistered()
        {
            if (m_PanelRegistered)
                return;

            // Template method: subclass configures bindings and triggers after subclass OnCreate completed.
            ConfigureBindings();
            ConfigureTriggers();

            // Register all bindings with UISystemBase via bridge
            var registrar = new SelfRegistrar(this);
            Bindings.RegisterAll(registrar);
            Triggers.RegisterAll(registrar);

            m_PanelRegistered = true;
            if (Log.IsDebugEnabled) Log.Debug($"Registered {Bindings.Count} bindings, {Triggers.Count} triggers");
        }

        /// <summary>
        /// Override to configure value bindings.
        /// Called during OnCreate before bindings are registered.
        /// </summary>
        protected abstract void ConfigureBindings();

        /// <summary>
        /// Override to configure trigger bindings.
        /// Called after ConfigureBindings during OnCreate.
        /// </summary>
        protected virtual void ConfigureTriggers()
        {
            // Optional — panels without triggers don't need to override
        }

        /// <summary>
        /// All panels update every 500ms (throttle only, stagger removed).
        ///
        /// Wraps OnPanelUpdate in try/catch so a panel-level NRE doesn't blow up
        /// the entire UIUpdate phase with a "System update error during
        /// UIUpdate->{panel}" CRITICAL that has no managed stack trace (the
        /// dynamic Update_Patch5 wrapper hides the call site). We log the full
        /// exception via Mod.Log so the offending file/line surfaces — then
        /// rethrow so vanilla still sees the failure.
        /// </summary>
        protected sealed override void OnThrottledUpdate()
        {
            try
            {
                OnPanelUpdate();
            }
            catch (Exception ex)
            {
                Mod.Log.Error($"[{GetType().Name}] OnPanelUpdate threw: {ex}");
                throw;
            }
        }

        /// <summary>
        /// Override to update bindings. Called every ~500ms (throttle).
        /// Replaces BaseUIPanel.OnUpdate().
        /// </summary>
        protected abstract void OnPanelUpdate();

        /// <summary>
        /// Publishes a domain DTO only when every source required to build it exists this frame.
        /// Binding presence is the readiness signal; incomplete source sets do not emit default DTOs.
        /// </summary>
        protected bool PublishWhenComplete<TDto>(
            string bindingKey,
            ReadOnlySpan<ISourceCheck> sources,
            Func<TDto> build)
            where TDto : struct, IDomainDto
        {
            if (!SourcesAvailable(sources))
                return false;

            bool prof = PerformanceProfiler.Enabled;
            long t0 = prof ? Stopwatch.GetTimestamp() : 0L;
            var dto = build();
            var sb = DomainJsonHelper.GetBuilder();
            dto.WriteTo(sb);
            string json = sb.ToString();
            if (prof) BindingRegistry.RecordBuildTime(bindingKey, Stopwatch.GetTimestamp() - t0);
            Bindings.Update(bindingKey, json);
            return true;
        }

        /// <summary>
        /// Publishes a scalar binding only when every source required to compute it exists this frame.
        /// </summary>
        protected bool PublishValueWhenComplete<T>(
            string bindingKey,
            ReadOnlySpan<ISourceCheck> sources,
            Func<T> build)
        {
            if (!SourcesAvailable(sources))
                return false;

            bool prof = PerformanceProfiler.Enabled;
            long t0 = prof ? Stopwatch.GetTimestamp() : 0L;
            var value = build();
            if (prof) BindingRegistry.RecordBuildTime(bindingKey, Stopwatch.GetTimestamp() - t0);
            Bindings.Update(bindingKey, value);
            return true;
        }

        /// <summary>
        /// Publishes prebuilt generated DTO JSON when every source required to build it exists this frame.
        /// </summary>
        protected bool PublishJsonWhenComplete(
            string bindingKey,
            ReadOnlySpan<ISourceCheck> sources,
            Func<string> buildJson)
        {
            if (!SourcesAvailable(sources))
                return false;

            bool prof = PerformanceProfiler.Enabled;
            long t0 = prof ? Stopwatch.GetTimestamp() : 0L;
            string json = buildJson();
            if (prof) BindingRegistry.RecordBuildTime(bindingKey, Stopwatch.GetTimestamp() - t0);
            Bindings.Update(bindingKey, json);
            return true;
        }

        private bool SourcesAvailable(ReadOnlySpan<ISourceCheck> sources)
        {
            for (int i = 0; i < sources.Length; i++)
            {
                if (!sources[i].IsAvailable(EntityManager))
                    return false;
            }

            return true;
        }

        private bool TryCreateScenarioGuard(out ScenarioGuard guard)
        {
            // NO_MIGRATE: scenario guard cannot be created without actual CurrentAct state.
            if (!SystemAPI.TryGetSingleton<CurrentActSingleton>(out var actSingleton))
            {
                guard = default;
                return false;
            }

            GamePhase phase = (m_WaveStateQuery.TryGetSingleton<WaveStateSingleton>(out var ws)
                    ? ws : WaveStateSingleton.Default)
                .CurrentPhase;

            guard = new ScenarioGuard(actSingleton.CurrentAct, phase);
            return true;
        }

        protected override void OnDestroy()
        {
            // BindingRegistry/TriggerRegistry clear their references
            // Actual binding disposal is handled by UISystemBase
            Bindings?.Dispose();
            Triggers?.Dispose();
            m_PanelRegistered = false;
            base.OnDestroy();
        }

        /// <summary>
        /// Bridge that forwards BindingRegistry/TriggerRegistry calls to UISystemBase.
        /// Enables reuse of existing registry API without changes.
        /// </summary>
        private sealed class SelfRegistrar : IBindingRegistrar
        {
            private readonly CivicUIPanelSystem m_System;

            public SelfRegistrar(CivicUIPanelSystem system) => m_System = system;

            public void AddBinding(IBinding binding) => m_System.AddBinding(binding);

            public void AddUpdateBinding(IUpdateBinding binding) => m_System.AddUpdateBinding(binding);
        }
    }
}
