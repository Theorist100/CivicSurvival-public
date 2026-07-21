using System;

namespace CivicSurvival.Core.Attributes
{
    /// <summary>
    /// Declares the expected lifecycle phases for a singleton owned through
    /// <see cref="SingletonOwnerAttribute"/>.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
    public sealed class OwnedSingletonLifecycleAttribute : Attribute
    {
        public bool Persisted { get; set; }

        public SingletonLifecyclePhase EnsurePhase { get; set; }

        public SingletonLifecyclePhase DisposePhase { get; set; }

        public bool AllowAsymmetry { get; set; }

        public string Justification { get; set; } = string.Empty;
    }

    /// <summary>
    /// Lifecycle phases where an owned singleton can be established, restored,
    /// reconciled, or torn down.
    /// </summary>
    [Flags]
    public enum SingletonLifecyclePhase
    {
        None = 0,
        OnCreate = 1 << 0,
        OnStartRunning = 1 << 1,
        OnLoadRestore = 1 << 2,
        ReconcileAfterLoad = 1 << 3,
        OnDestroy = 1 << 4,
    }
}
