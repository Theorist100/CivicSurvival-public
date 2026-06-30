using Colossal.Serialization.Entities;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Components.Domain.Cognitive;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Serialization;
using CivicSurvival.Core.Systems;

namespace CivicSurvival.Domains.Cognitive.Ops.Systems
{
    /// <summary>
    /// Serialization partial for TelemarathonSystem.
    /// Handles save/load of TelemarathonRuntimeState singleton.
    /// TelemarathonConfig is excluded — rebuilt from BalanceConfig on load.
    /// </summary>
    public partial class TelemarathonSystem : IDefaultSerializable, IResettable, IBootDefaultsReset
    {
#pragma warning disable CIVIC324 // Ephemeral boot-reset gate; armed by ResetToBootDefaults and consumed in the next ValidateAfterLoad pass.
        [System.NonSerialized] private bool m_PendingBootDefaultRuntimeRestore;
#pragma warning restore CIVIC324

        public void SetDefaults(Context context) => ResetState();
        void IResettable.ResetState() => ResetState();

        private void ResetState()
        {
            EnsureRuntimeStateExists();
            if (m_StateQuery.TryGetSingletonEntity<TelemarathonRuntimeState>(out var entity))
                EntityManager.SetComponentData(entity, TelemarathonRuntimeState.Default);
            RebuildConfig();
        }

        public void ResetToBootDefaults(ResetReason reason)
        {
            m_PendingBootDefaultRuntimeRestore = true;

            Log.Info($"[BOOT-RESET] TelemarathonSystem reason={reason}");
        }

        public void ValidateAfterLoad()
        {
            EnsureRuntimeStateExists();
            RebuildConfig();
#pragma warning disable CIVIC212 // Telemarathon owns this runtime singleton; post-load recomputes derived fields from loaded state
            if (m_PendingBootDefaultRuntimeRestore
                && m_StateQuery.TryGetSingletonEntity<TelemarathonRuntimeState>(out var stateEntity))
            {
                var state = TelemarathonRuntimeState.Default;
                state.ShockEndHour = 0f;
                state.ShockHoursRemaining = 0f;
                EntityManager.SetComponentData(stateEntity, state);
            }
            m_PendingBootDefaultRuntimeRestore = false;

            if (m_ConfigQuery.TryGetSingleton<TelemarathonConfig>(out var rebuiltCfg)
                && m_StateQuery.TryGetSingletonRW<TelemarathonRuntimeState>(out var loadedRef))
            {
                PublishDerivedFacts(ref loadedRef.ValueRW, in rebuiltCfg);
            }
#pragma warning restore CIVIC212
        }

        private void EnsureRuntimeStateExists()
        {
            if (m_StateQuery.IsEmpty)
            {
                var entity = EntityManager.CreateEntity();
                EntityManager.AddComponentData(entity, TelemarathonRuntimeState.Default);
                EntityManager.SetName(entity, nameof(TelemarathonRuntimeState));
            }

            if (m_ConfigQuery.IsEmpty)
            {
                var entity = EntityManager.CreateEntity();
                EntityManager.AddComponentData(entity, BuildConfig());
                EntityManager.SetName(entity, nameof(TelemarathonConfig));
            }
        }

        // FIX W2-H5: Config must be rebuilt on BOTH reset and load — was only built in OnCreate
        private void RebuildConfig()
        {
            if (m_ConfigQuery.TryGetSingletonEntity<TelemarathonConfig>(out var cfgEntity))
                EntityManager.SetComponentData(cfgEntity, BuildConfig());
        }

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SaveVersions.GLOBAL);
            try
            {

                TelemarathonRuntimeState state;
                if (m_StateQuery.TryGetSingletonEntity<TelemarathonRuntimeState>(out var entity))
                    state = EntityManager.GetComponentData<TelemarathonRuntimeState>(entity);
                else
                    state = TelemarathonRuntimeState.Default;

                float currentHour = GetCurrentHourForSerialization();
                float shockHoursRemaining;
                if (currentHour < 0f)
                {
                    // GameTimeSystem unavailable: cannot recompute remaining time from
                    // ShockEndHour anchor. Persist the snapshot-stored remaining value
                    // verbatim — Deserialize will re-anchor it on the next load.
                    shockHoursRemaining = state.ShockHoursRemaining;
                }
                else
                {
                    shockHoursRemaining = state.ShockEndHour > 0f
                        ? Unity.Mathematics.math.max(0f, state.ShockEndHour - currentHour)
                        : state.ShockHoursRemaining;
                }
                shockHoursRemaining = Unity.Mathematics.math.min(TelemarathonCodec.MaxShockHoursRemaining, shockHoursRemaining);

                var persistState = new TelemarathonPersistState(
                    state.IsActive,
                    state.Mode,
                    state.Trust,
                    state.LastModeChangeHour,
                    shockHoursRemaining,
                    state.ShockEndHour,
                    state.AudienceFatigue,
                    state.ShockCooldownEndHour);
                TelemarathonCodec.Write(persistState, writer);

            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
            SerializationGuard.LogSerialized(nameof(TelemarathonSystem), SaveVersions.GLOBAL);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SaveVersions.GLOBAL, out _, out var block, nameof(TelemarathonSystem)))
            {
                ResetToBootDefaults(ResetReason.VersionMismatch);
                return;
            }
            try
            {
                TelemarathonCodec.Read(reader, out var persistState);

                // Start from Default to hydrate EffectivenessMult and other precomputed fields,
                // then overwrite persisted gameplay fields.
                var state = TelemarathonRuntimeState.Default;
                state.IsActive = persistState.IsActive;
                state.Mode = persistState.Mode;
                state.Trust = persistState.Trust;
                state.LastModeChangeHour = persistState.LastModeChangeHour;
                state.AudienceFatigue = persistState.AudienceFatigue;
                state.ShockCooldownEndHour = persistState.ShockCooldownEndHour;

                float currentHour = GetCurrentHourForSerialization();
                if (currentHour < 0f)
                {
                    // GameTimeSystem not yet activated — cannot resolve ShockEndHour vs
                    // current time. Restore the persisted remaining value as-is; the
                    // anchor (ShockEndHour) will be recomputed by the consumer on the
                    // first runtime tick when current time is finally available.
                    state.ShockHoursRemaining = Unity.Mathematics.math.min(TelemarathonCodec.MaxShockHoursRemaining, persistState.ShockHoursRemaining);
                    state.ShockEndHour = persistState.ShockEndHour;
                }
                else if (persistState.ShockEndHour > currentHour)
                {
                    state.ShockEndHour = persistState.ShockEndHour;
                    state.ShockHoursRemaining = Unity.Mathematics.math.min(TelemarathonCodec.MaxShockHoursRemaining, persistState.ShockEndHour - currentHour);
                }
                else if (persistState.ShockHoursRemaining > 0f)
                {
                    state.ShockEndHour = currentHour + persistState.ShockHoursRemaining;
                    state.ShockHoursRemaining = persistState.ShockHoursRemaining;
                }

                EnsureRuntimeStateExists();
                if (m_StateQuery.TryGetSingletonEntity<TelemarathonRuntimeState>(out var entity))
                {
                    EntityManager.SetComponentData(entity, state);
                }

                // FIX W2-H5: Rebuild config from current BalanceConfig on every load
                RebuildConfig();

                // FIX W3-M1: Recompute derived facts (EffectivenessMult, SpotterBonus, StressRate)
                // from deserialized state — without this, readers see Default values for 1 frame
                if (m_ConfigQuery.TryGetSingleton<TelemarathonConfig>(out var rebuiltCfg)
                    && m_StateQuery.TryGetSingletonRW<TelemarathonRuntimeState>(out var loadedRef))
                {
                    PublishDerivedFacts(ref loadedRef.ValueRW, in rebuiltCfg);
                }

                Log.Info($"Deserialized: IsActive={persistState.IsActive}, Mode={persistState.Mode}, Trust={persistState.Trust:F2}");
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

        /// <summary>
        /// Returns current game time hours, or -1f sentinel when GameTimeSystem isn't
        /// yet activated (vanilla invokes Serialize/Deserialize before OnGameLoaded).
        /// Callers MUST handle the sentinel — see Serialize / Deserialize paths.
        /// </summary>
        private float GetCurrentHourForSerialization()
        {
            if (!GameTimeSystem.TryGetGameHours(out var hours))
            {
                Log.Warn("GameTimeSystem unavailable during Telemarathon serialization; using -1 sentinel");
                return -1f;
            }
            return hours;
        }
    }
}
