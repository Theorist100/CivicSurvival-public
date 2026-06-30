using Colossal.Serialization.Entities;
using Unity.Mathematics;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Components.Domain.Spotters;
using CivicSurvival.Core.Components.Threats;
using CivicSurvival.Core.Serialization;
using CivicSurvival.Core.Interfaces.Services;

namespace CivicSurvival.Domains.Spotters.Systems
{
    /// <summary>
    /// Serialization partial for SpotterAggregateSystem.
    /// Handles save/load of simulation state and SpotterCountermeasuresState singleton.
    /// </summary>
    public partial class SpotterAggregateSystem : IDefaultSerializable, IBootDefaultsReset
    {
        public void ResetToBootDefaults(ResetReason reason) => ResetState();

        public void SetDefaults(Context context)
        {
            ResetState();
            ResetSingletonState();
        }

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SaveVersions.GLOBAL);
            try
            {

                SpotterCountermeasuresState cmState = default;
                int internetLen = 0, returnLen = 0;
                bool hasSingleton = m_CountermeasuresStateQuery.TryGetSingletonEntity<SpotterCountermeasuresState>(out var singletonEntity);
                if (hasSingleton)
                {
                    cmState = EntityManager.GetComponentData<SpotterCountermeasuresState>(singletonEntity);
                    internetLen = EntityManager.HasBuffer<InternetDisabledBuffer>(singletonEntity)
                        ? EntityManager.GetBuffer<InternetDisabledBuffer>(singletonEntity, true).Length : 0;
                    returnLen = EntityManager.HasBuffer<EvacuatedReturnBuffer>(singletonEntity)
                        ? EntityManager.GetBuffer<EvacuatedReturnBuffer>(singletonEntity, true).Length : 0;
                }

                var internetDistricts = new int[internetLen];
                if (hasSingleton && internetLen > 0)
                {
                    var buf = EntityManager.GetBuffer<InternetDisabledBuffer>(singletonEntity, true);
                    for (int i = 0; i < buf.Length; i++)
                    {
                        internetDistricts[i] = buf[i].DistrictIndex;
                    }
                }

                var returnTimes = new double[returnLen];
                if (hasSingleton && returnLen > 0)
                {
                    var buf = EntityManager.GetBuffer<EvacuatedReturnBuffer>(singletonEntity, true);
                    for (int i = 0; i < buf.Length; i++)
                    {
                        returnTimes[i] = buf[i].ReturnTime;
                    }
                }

                var state = new SpotterAggregatePersistState(
                    m_LastDailyTick,
                    cmState.TotalSBUVisits,
                    cmState.TotalEvacuations,
                    cmState.CounterOSINTActive,
                    cmState.RandomState.state,
                    internetDistricts,
                    returnTimes);
                SpotterAggregateCodec.Write(state, writer);
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
            SerializationGuard.LogSerialized(nameof(SpotterAggregateSystem), SaveVersions.GLOBAL);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SaveVersions.GLOBAL, out _, out var block, nameof(SpotterAggregateSystem)))
            {
                ResetToBootDefaults(ResetReason.VersionMismatch);
                return;
            }
            try
            {
                SpotterAggregateCodec.Read(reader, out var payload);
                ResetTransientRuntimeState();
                m_LastDailyTick = payload.LastDailyTick;

                m_Initialized = true;
                uint randomState = payload.RandomState;
                if (randomState == 0)
                {
                    Log.Info("Deserialize: randomState was 0 (missing or corrupt) — reseeding SpotterCountermeasuresState");
                    randomState = 0x53504F54; // "SPOT"
                }

                var state = new SpotterCountermeasuresState
                {
                    TotalSBUVisits = payload.TotalSbuVisits,
                    TotalEvacuations = payload.TotalEvacuations,
                    CounterOSINTActive = payload.CounterOsintActive,
#pragma warning disable CIVIC007 // Seed provided via object initializer from deserialized state
                    RandomState = new Random { state = randomState }
#pragma warning restore CIVIC007
                };

                if (m_CountermeasuresStateQuery.TryGetSingletonEntity<SpotterCountermeasuresState>(out var singletonEntity))
                {
                    EntityManager.SetComponentData(singletonEntity, state);

                    // Restore InternetDisabledBuffer
                    if (EntityManager.HasBuffer<InternetDisabledBuffer>(singletonEntity))
                    {
                        var internetBuffer = EntityManager.GetBuffer<InternetDisabledBuffer>(singletonEntity);
                        internetBuffer.Clear();
                        foreach (int districtIndex in payload.InternetDisabledDistricts)
                        {
                            internetBuffer.Add(new InternetDisabledBuffer { DistrictIndex = districtIndex });
                        }
                    }
                    else if (payload.InternetDisabledDistricts.Count > 0)
                    {
                        Log.Error($"Deserialize: InternetDisabledBuffer missing on singleton — {payload.InternetDisabledDistricts.Count} disabled districts lost");
                    }

                    // Restore EvacuatedReturnBuffer
                    if (EntityManager.HasBuffer<EvacuatedReturnBuffer>(singletonEntity))
                    {
                        var returnBuffer = EntityManager.GetBuffer<EvacuatedReturnBuffer>(singletonEntity);
                        returnBuffer.Clear();
                        foreach (double returnTime in payload.EvacuatedReturnTimes)
                        {
                            returnBuffer.Add(new EvacuatedReturnBuffer { ReturnTime = returnTime });
                        }
                    }
                    else if (payload.EvacuatedReturnTimes.Count > 0)
                    {
                        Log.Error($"Deserialize: EvacuatedReturnBuffer missing on singleton — {payload.EvacuatedReturnTimes.Count} pending returns lost");
                    }

                }
                else
                {
                    Log.Error("Deserialize: SpotterCountermeasuresState singleton missing — internet/return buffer data lost");
                }

                m_PendingGhostSweep = true; // H16: sweep deferred to OnThrottledUpdate

                // Derived penalty state is recomputed on the first aggregate tick after load.

                Log.Info($"Deserialized: SBU={payload.TotalSbuVisits}, Evac={payload.TotalEvacuations}, Counter-OSINT={payload.CounterOsintActive}, " +
                         $"InternetOff={payload.InternetDisabledDistricts.Count}, Returns={payload.EvacuatedReturnTimes.Count}");
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
        }

        private void ResetTransientRuntimeState()
        {
            m_Initialized = false;
            m_RawPenalty = 0f;
            m_GlobalPenalty = 0f;
            m_ActiveSpotterCount = 0;
            m_ActionableSpotterCount = 0;
            m_TotalSpotterCount = 0;
            m_PendingGhostSweep = false;
            if (m_CommandQueue.IsCreated) m_CommandQueue.Clear();
        }

        private void ResetSingletonState()
        {
            if (!m_CountermeasuresStateQuery.TryGetSingletonEntity<SpotterCountermeasuresState>(out var singletonEntity))
                return;

            var state = EntityManager.GetComponentData<SpotterCountermeasuresState>(singletonEntity);
            state.TotalSBUVisits = 0;
            state.TotalEvacuations = 0;
            state.CounterOSINTActive = false;
            state.RandomState = Random.CreateFromIndex((uint)(World.GetHashCode() ^ 0x5B077E5));
            EntityManager.SetComponentData(singletonEntity, state);

            if (EntityManager.HasBuffer<InternetDisabledBuffer>(singletonEntity))
                EntityManager.GetBuffer<InternetDisabledBuffer>(singletonEntity).Clear();

            if (EntityManager.HasBuffer<EvacuatedReturnBuffer>(singletonEntity))
                EntityManager.GetBuffer<EvacuatedReturnBuffer>(singletonEntity).Clear();

            // Reset penalty singleton
            if (m_PenaltyStateQuery.TryGetSingletonEntity<SpotterPenaltyState>(out var penaltyEntity))
                EntityManager.SetComponentData(penaltyEntity, SpotterPenaltyState.Default);

            Log.Info("Singleton state reset");
        }
    }
}
