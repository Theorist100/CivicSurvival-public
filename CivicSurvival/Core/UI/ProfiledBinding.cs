using System.Collections.Generic;
using System.Diagnostics;
using Colossal.UI.Binding;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Core.UI
{
    /// <summary>
    /// Wrapper around ValueBinding that adds per-binding profiling to PERF.log.
    /// Drop-in replacement: same Update() API, use .Binding for AddBinding().
    /// </summary>
    public sealed class ProfiledBinding<T>
    {
        private readonly ValueBinding<T> _inner;
        private readonly string _key;

        public ProfiledBinding(string group, string key, T defaultValue)
        {
            _inner = new ValueBinding<T>(group, key, defaultValue);
            _key = key;
        }

        public ProfiledBinding(string group, string key, T defaultValue, IWriter<T> writer)
        {
            _inner = new ValueBinding<T>(group, key, defaultValue, writer);
            _key = key;
        }

        public ProfiledBinding(string group, string key, T defaultValue, IWriter<T> writer, EqualityComparer<T> comparer)
        {
            _inner = new ValueBinding<T>(group, key, defaultValue, writer, comparer);
            _key = key;
        }

        /// <summary>
        /// Inner binding for AddBinding() registration.
        /// </summary>
        public IBinding Binding => _inner;

        /// <summary>
        /// Current value (read-only access to underlying ValueBinding.value).
        /// </summary>
        public T Value => _inner.value;

        /// <summary>
        /// Update binding value with profiling.
        /// </summary>
        public void Update(T value)
        {
            bool profile = PerformanceProfiler.Enabled;
            if (profile)
            {
                long start = Stopwatch.GetTimestamp();
                _inner.Update(value);
                long elapsed = Stopwatch.GetTimestamp() - start;
                if (PerformanceProfiler.Enabled)
                    BindingRegistry.RecordExternalUpdate(_key, elapsed);
            }
            else
            {
                _inner.Update(value);
            }
        }

        /// <summary>
        /// Implicit conversion to ValueBinding for method parameters (Dismiss, DismissModal).
        /// </summary>
#pragma warning disable CA2225 // Named alternative not needed — .Binding covers AddBinding, implicit is for rare method params
        public static implicit operator ValueBinding<T>(ProfiledBinding<T> p) => p._inner;
#pragma warning restore CA2225
    }
}
