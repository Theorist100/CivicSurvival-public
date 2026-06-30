using System;
using System.Collections.Generic;
using System.Diagnostics;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Utils;
using Game.Buildings;
using Unity.Entities;
using Unity.Jobs;

namespace CivicSurvival.Core.Services
{
    public sealed class RenderWriteBarrier : IRenderWriteBarrier, IVanillaWriteBarrier
    {
        private static readonly LogContext Log = new("RenderWriteBarrier");
        private const double MicrosecondsPerSecond = 1_000_000.0;
        private readonly object m_Lock = new();
        private readonly List<PublishedWrite> m_PublishedWrites = new();

        public void Publish(JobHandle handle, Type producer, RenderWriteComponentMask mask)
        {
            if (producer == null)
                throw new ArgumentNullException(nameof(producer));

            if (mask == RenderWriteComponentMask.None)
                return;

            lock (m_Lock)
            {
                CompleteAndRemoveCompletedWritesUnsafe();

                m_PublishedWrites.Add(new PublishedWrite(handle, producer, mask));
            }

            if (Log.IsDebugEnabled)
                Log.Debug($"barrier publish (producer={producer.Name}, mask={mask})");
        }

        public RenderWriteTicket Consume(Type reader, RenderWriteComponentMask mask)
        {
            if (reader == null)
                throw new ArgumentNullException(nameof(reader));

            if (mask == RenderWriteComponentMask.None)
                return new RenderWriteTicket(RenderWriteComponentMask.None);

            JobHandle combined = default;
            int matched = 0;

            lock (m_Lock)
            {
                CompleteAndRemoveCompletedWritesUnsafe();

                for (int i = 0; i < m_PublishedWrites.Count; i++)
                {
                    var published = m_PublishedWrites[i];
                    if ((published.Mask & mask) == RenderWriteComponentMask.None)
                        continue;

                    combined = matched == 0
                        ? published.Handle
                        : JobHandle.CombineDependencies(combined, published.Handle);
                    matched++;
                }
            }

            long elapsedTicks = 0;
            if (matched > 0)
            {
                var start = Stopwatch.GetTimestamp();
                combined.Complete();
                elapsedTicks = Stopwatch.GetTimestamp() - start;

                lock (m_Lock)
                {
                    CompleteAndRemoveCompletedWritesUnsafe();
                }
            }

            if (Log.IsDebugEnabled)
            {
                double waitedUs = elapsedTicks * MicrosecondsPerSecond / Stopwatch.Frequency;
                Log.Debug($"barrier consume (reader={reader.Name}, mask={mask}, producers={matched}, waited={waitedUs:F1}us)");
            }

            return new RenderWriteTicket(mask);
        }

        public VanillaWriteTicket Consume(
            EntityManager entityManager,
            Type reader,
            VanillaWriteComponentMask mask)
        {
            if (reader == null)
                throw new ArgumentNullException(nameof(reader));

            if (mask == VanillaWriteComponentMask.None)
                return new VanillaWriteTicket(VanillaWriteComponentMask.None);

            long elapsedTicks = 0;
            var start = Stopwatch.GetTimestamp();

            if ((mask & VanillaWriteComponentMask.ElectricityProducer) != 0)
                entityManager.CompleteDependencyBeforeRO<ElectricityProducer>();

            // Efficiency / ResourceConsumer drains retired 2026-06-12 together with their mask
            // values — PowerCapacityResolverSystem (the only consumer) moved those component
            // accesses into PlantResolveJob, where the job graph orders them without a
            // main-thread wait. See the mask enum note in IRenderWriteBarrier.cs.

            elapsedTicks = Stopwatch.GetTimestamp() - start;

            if (Log.IsDebugEnabled)
            {
                double waitedUs = elapsedTicks * MicrosecondsPerSecond / Stopwatch.Frequency;
                Log.Debug($"vanilla barrier consume (reader={reader.Name}, mask={mask}, waited={waitedUs:F1}us)");
            }

            return new VanillaWriteTicket(mask);
        }

        [CallerHoldsLock("m_Lock")]
        private void CompleteAndRemoveCompletedWritesUnsafe()
        {
            for (int i = m_PublishedWrites.Count - 1; i >= 0; i--)
            {
                var published = m_PublishedWrites[i];
                if (!published.Handle.IsCompleted)
                    continue;

                published.Handle.Complete();
                m_PublishedWrites.RemoveAt(i);
            }
        }

        private readonly struct PublishedWrite
        {
            public PublishedWrite(JobHandle handle, Type producer, RenderWriteComponentMask mask)
            {
                Handle = handle;
                Producer = producer;
                Mask = mask;
            }

            public JobHandle Handle { get; }
            public Type Producer { get; }
            public RenderWriteComponentMask Mask { get; }
        }
    }
}
