using System;

namespace CivicSurvival.Core.Components.Lifecycle
{
    /// <summary>
    /// Declares dependency wiring semantics explicitly: retryable optional wiring
    /// for late registry cases, and fail-fast wiring for hard same-feature contracts.
    /// </summary>
    public sealed class CivicDependencyWire
    {
        private readonly string m_Name;

        public CivicDependencyWire(string name)
        {
            m_Name = name;
        }

        public bool Ready { get; private set; }

        public bool EnsureWired(Func<bool> resolve)
        {
            if (Ready)
                return true;

            Ready = resolve();
            return Ready;
        }

        public void Reset()
        {
            Ready = false;
        }

        public T RequireWired<T>(Func<T> resolve)
            where T : class
        {
            var value = resolve();
            if (value == null)
                throw new InvalidOperationException($"{m_Name}: required dependency '{typeof(T).Name}' was not wired.");

            Ready = true;
            return value;
        }
    }
}
