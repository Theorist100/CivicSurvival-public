using Unity.Entities;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Components.Domain.Refugees;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.UI;
using CivicSurvival.Core.UI.DomainState;
using CivicSurvival.Core.Utils;
using static CivicSurvival.Core.UI.B;
using CivicSurvival.Core.Attributes;

namespace CivicSurvival.Domains.Attention.UI
{
    /// <summary>
    /// UI system for Attention Economy data.
    /// ECS-Pure: Reads directly from ShockStateSingleton and ExodusStateSingleton.
    ///
    /// Migrated from AttentionUIPanel → CivicUIPanelSystem.
    /// Show-defaults architecture: publishes unconditionally (NoSourceChecks,
    /// no RequireForUpdate gating); emits a default AttentionDto when the
    /// Shock/Exodus state singletons are absent. Consistent with every other
    /// domain UI system — TS renders DEFAULT_ATTENTION_DTO for the default.
    /// </summary>
    [ActIndependent]
    public partial class AttentionUISystem : CivicUIPanelSystem
    {
        private EntityQuery m_ShockQuery;
        private EntityQuery m_ExodusQuery;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_ShockQuery = GetEntityQuery(
                ComponentType.ReadOnly<ShockStateSingleton>());
            m_ExodusQuery = GetEntityQuery(
                ComponentType.ReadOnly<ExodusStateSingleton>());

            // Show-defaults: no RequireForUpdate / no source gating — the panel
            // publishes every tick and emits a default AttentionDto until the
            // state singletons exist (TS shows DEFAULT_ATTENTION_DTO).
            Log.Info("Created");
        }

        protected override void ConfigureBindings()
        {
            Bindings.Add<string>(AttentionState, "{}");
        }

        protected override void OnPanelUpdate()
        {
            PublishWhenComplete(AttentionState, NoSourceChecks, () =>
            {
                // Singletons absent → emit the default AttentionDto (show-defaults
                // data model); TS resolves it via DEFAULT_ATTENTION_DTO.
                if (!m_ShockQuery.TryGetSingleton<ShockStateSingleton>(out var shock) ||
                    !m_ExodusQuery.TryGetSingleton<ExodusStateSingleton>(out var exodus))
                    return default;

                return new AttentionDto
                {
                    ShockLevel = shock.ShockLevel,
                    ShockTier = EnumName<Core.Types.AidTier>.Get(shock.CurrentTier),
                    CasualtiesThisWeek = shock.CasualtiesThisWeek,
                    BuildingsDestroyedThisWeek = shock.BuildingsDestroyedThisWeek,
                    CriticalHitsThisWeek = shock.CriticalHitsThisWeek,
                    TotalCasualties = shock.TotalCasualties,
                    TotalBuildingsDestroyed = shock.TotalBuildingsDestroyed,
                    TotalCivilianBuildingsDestroyed = shock.TotalCivilianBuildingsDestroyed,
                    TotalCriticalHits = shock.TotalCriticalHits,
                    ExodusActive = exodus.IsExodusActive,
                    BaseExodusRatePercentPerDay = exodus.BaseRatePercentPerDay,
                    ExodusRatePercentPerDay = exodus.EffectiveRatePercentPerDay,
                    TotalExodus = exodus.TotalExodus
                };
            });
        }
    }
}
