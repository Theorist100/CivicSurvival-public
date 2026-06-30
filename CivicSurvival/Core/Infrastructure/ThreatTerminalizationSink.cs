using System;
using System.Collections.Generic;
using CivicSurvival.Core.Interfaces.Threats;
using CivicSurvival.Core.Types;
using Unity.Entities;

namespace CivicSurvival.Core.Infrastructure
{
    public sealed class ThreatTerminalizationSink : IThreatTerminalizationSink, IDisposable
    {
        private readonly List<ThreatTerminalOutcome> m_Outcomes = new(32);
        private readonly Dictionary<Entity, int> m_IndexByEntity = new();
        private bool m_Disposed;

        public bool HasPending => !m_Disposed && m_Outcomes.Count > 0;
        public int PendingCount => m_Disposed ? 0 : m_Outcomes.Count;

        public void Queue(in ThreatTerminalOutcome outcome)
        {
            EnsureLive();
            if (outcome.Entity == Entity.Null)
                return;

            if (m_IndexByEntity.TryGetValue(outcome.Entity, out int index))
            {
                if (outcome.Priority > m_Outcomes[index].Priority)
                    m_Outcomes[index] = outcome;
                return;
            }

            m_IndexByEntity.Add(outcome.Entity, m_Outcomes.Count);
            m_Outcomes.Add(outcome);
        }

        public void Drain(List<ThreatTerminalOutcome> destination)
        {
            EnsureLive();
            destination.Clear();
            destination.AddRange(m_Outcomes);
            Clear();
        }

        public void Clear()
        {
            if (m_Disposed)
                return;

            m_Outcomes.Clear();
            m_IndexByEntity.Clear();
        }

        public void Dispose()
        {
            if (m_Disposed)
                return;

            Clear();
            m_Disposed = true;
        }

        private void EnsureLive()
        {
            if (m_Disposed)
                throw new ObjectDisposedException(nameof(ThreatTerminalizationSink));
        }
    }
}
