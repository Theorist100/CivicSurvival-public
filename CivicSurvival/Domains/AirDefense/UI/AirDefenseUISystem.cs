using System;
using Unity.Entities;
using Game;
using Game.Simulation;
using CivicSurvival.Core.Components.Domain.AirDefense;
using CivicSurvival.Core.Components.Domain.Mobilization;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Components.Threats;
using CivicSurvival.Core.Components.Requests;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Interfaces.Threats;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Systems;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.UI;
using CivicSurvival.Core.UI.DomainState;
using CivicSurvival.Core.Utils;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Services;
using static CivicSurvival.Core.UI.B;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Domains.AirDefense.Systems;

namespace CivicSurvival.Domains.AirDefense.UI
{
    /// <summary>
    /// UI system for Air Defense.
    /// Reads from AirDefenseCreditsSingleton, IntelStateSingleton, SpotterStatsSingleton.
    ///
    /// Migrated from AirDefenseUIPanel → CivicUIPanelSystem.
    /// Gains: auto-disposed EntityQueries, proper ECS lifecycle.
    /// </summary>
    [ActIndependent]
    [HandlesRequestKind(RequestKind.PatriotDroneToggle)]
    public partial class AirDefenseUISystem : CivicUIPanelSystem
    {
        private EndFrameBarrier m_EndFrameBarrier = null!;
        private IThreatAudioService m_AudioService = null!;
        private IAAPlacementCommandService m_PlacementCommand = null!;
        private IPatriotDroneToggleCommandService m_PatriotDroneToggleCommand = null!;
        private IAutoResupplyToggleCommandService m_AutoResupplyToggleCommand = null!;

        private AirDefenseStateSystem? m_StateSystem;
        private EntityQuery m_SpotterPenaltyQuery;
        private EntityQuery m_MobilizationQuery;
        [System.NonSerialized] private bool m_LoggedSentinelLeak;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_EndFrameBarrier = World.GetOrCreateSystemManaged<EndFrameBarrier>();
            m_StateSystem = World.GetExistingSystemManaged<AirDefenseStateSystem>();

            m_SpotterPenaltyQuery = GetEntityQuery(
                ComponentType.ReadOnly<SpotterPenaltyState>());
            m_MobilizationQuery = GetEntityQuery(
                ComponentType.ReadOnly<MobilizationStateSingleton>());
            // Show-defaults convention: panel always renders with a neutral DTO when
            // producers are not yet available — no RequireForUpdate, no fail-loud
            // GetSingletonOrDefault (which threw [CRITICAL] producer-not-yet-run).

            Log.Info("Created");
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            m_PlacementCommand = ServiceRegistry.Instance.Require<IAAPlacementCommandService>();
            m_PatriotDroneToggleCommand = ServiceRegistry.Instance.Require<IPatriotDroneToggleCommandService>();
            m_AutoResupplyToggleCommand = ServiceRegistry.Instance.Require<IAutoResupplyToggleCommandService>();
        }

        protected override void ConfigureBindings()
        {
            Bindings.Add<string>(AirDefenseState, "{}");
        }

        protected override void ConfigureTriggers()
        {
            // Emergency resupply and policy changes are ordinary gameplay requests:
            // the UI only enqueues intent and the owning ECS systems validate/apply it.
            Triggers.Add<int>(EmergencyResupply, FeatureIds.AirDefense, RequestResultBridge.EmergencyResupply, OnEmergencyResupply);
            Triggers.Add<int>(SetDefensePolicy, FeatureIds.AirDefense, RequestResultBridge.DefensePolicy, OnSetDefensePolicy);
            Triggers.Add<bool>(TogglePatriotDroneIntercept, FeatureIds.AirDefense, RequestResultBridge.PatriotDroneToggle, OnTogglePatriotDroneIntercept);
            // Auto-resupply rule: synchronous sync-service toggle (no request lifecycle needed —
            // the command applies on the UI thread, pause-safe via the command service). Mirrors
            // the bridge-less settings toggles; the SegmentButton reflects the DTO state directly.
            Triggers.Add<bool>(ToggleAutoResupply, FeatureIds.AirDefense, OnToggleAutoResupply);

            // Placement is different from the request-only buttons above. It must
            // synchronously activate the vanilla placement tool from the UI callback
            // so the player can start building while GameSimulation is paused.
            Triggers.Add<string>(PlaceAABuilding, FeatureIds.AirDefense, RequestResultBridge.AirDefensePlacement, OnPlaceAABuilding);
        }

        protected override void OnPanelUpdate()
        {
            var balance = BalanceConfig.Current;
            var heritageP = AAParams.ForType(balance, AAType.HeritageBofors);
            var boforsP = AAParams.ForType(balance, AAType.Bofors40mm);
            var gepardP = AAParams.ForType(balance, AAType.Gepard);
            var patriotP = AAParams.ForType(balance, AAType.PatriotSAM);
            var dto = new AirDefenseDto
            {
                PatriotResupplyCost = patriotP.ResupplyCost,
                HeritageCrew = heritageP.CrewRequired,
                BoforsCrew = boforsP.CrewRequired,
                GepardCrew = gepardP.CrewRequired,
                BoforsPrice = boforsP.Price,
                GepardPrice = gepardP.Price,
                PatriotPrice = patriotP.Price,
                PatriotCrew = patriotP.CrewRequired,
                EmergencyResupplyRequestJson = RequestResultBridge.Get(RequestResultBridge.EmergencyResupply).ToJson(),
                DefensePolicyRequestJson = RequestResultBridge.Get(RequestResultBridge.DefensePolicy).ToJson(),
                PatriotDroneToggleRequestJson = RequestResultBridge.Get(RequestResultBridge.PatriotDroneToggle).ToJson(),
                AirDefensePlacementRequestJson = RequestResultBridge.Get(RequestResultBridge.AirDefensePlacement).ToJson()
            };

            if (m_StateSystem != null)
            {
                // AirDefenseStateSystem owns the pause-safe live AA read model; keep
                // this before resupply eligibility because that gate reads ammo totals.
                var stats = m_StateSystem.GetUiStatsSnapshot();
                dto.AaStations = stats.AaStations;
                dto.AaAmmo = stats.AaAmmo;
                dto.AaMaxAmmo = stats.AaMaxAmmo;
                dto.HeritageBoforsCount = stats.HeritageBoforsCount;
                dto.BoforsCount = stats.BoforsCount;
                dto.GepardCount = stats.GepardCount;
                dto.PatriotCount = stats.PatriotCount;
                dto.PatriotAmmo = stats.GetAmmo(AAType.PatriotSAM);
                dto.PatriotMaxAmmo = stats.GetMaxAmmo(AAType.PatriotSAM);
                dto.BoforsAmmo = stats.GetAmmo(AAType.Bofors40mm);
                dto.BoforsMaxAmmo = stats.GetMaxAmmo(AAType.Bofors40mm);
                dto.HeritageAmmo = stats.GetAmmo(AAType.HeritageBofors);
                dto.HeritageMaxAmmo = stats.GetMaxAmmo(AAType.HeritageBofors);
                dto.GepardAmmo = stats.GetAmmo(AAType.Gepard);
                dto.GepardMaxAmmo = stats.GetMaxAmmo(AAType.Gepard);

                var adState = m_StateSystem.GetCreditsSnapshot();
                dto.HeritageCredits = adState.HeritageCredits;
                dto.HeritageCreditsMax = adState.HeritageCreditsMax;
                dto.DonorPatriotCredits = adState.DonorPatriotCredits;
                dto.PatriotInterceptsDrones = adState.PatriotInterceptsDrones;
                dto.AutoResupplyEnabled = adState.AutoResupplyEnabled;
                // Show-defaults convention: panel always renders with a working
                // business default. The Unavailable sentinel is reserved for the
                // cross-domain NullDefensePolicyReader null-object and MUST NOT
                // leak into UI DTO — sanitize to HumanitarianShield, but log
                // once so an owner regression (m_CreditsLatest zero-init,
                // ResetState miss, etc.) stays visible.
                var uiPolicy = SanitizePolicyForUi(adState.CurrentPolicy);
                dto.DefensePolicyId = ToUiDefensePolicyId(uiPolicy);
                dto.DefensePolicyName = uiPolicy switch
                {
                    DefensePolicy.HumanitarianShield => "Humanitarian Shield",
                    DefensePolicy.GridIntegrity => "Grid Integrity",
                    // SanitizePolicyForUi above maps Unavailable to
                    // HumanitarianShield, so this branch documents the
                    // invariant for CIVIC210 (exhaustive switch) and surfaces
                    // any future bypass loudly instead of silently throwing
                    // from default.
                    DefensePolicy.Unavailable => throw new InvalidOperationException("SanitizePolicyForUi must have mapped Unavailable to HumanitarianShield before this switch."),
                    _ => throw new InvalidOperationException($"Unhandled DefensePolicy: {uiPolicy}")
                };
            }
            FillEmergencyResupplyEligibility(ref dto);
            FillAAPlacementEligibility(ref dto);

            SpotterPenaltyState penalty;
            using (PerformanceProfiler.Measure("SP:ADUI.SpotterPenalty"))
            {
                penalty = m_SpotterPenaltyQuery.TryGetSingleton<SpotterPenaltyState>(out var sp)
                    ? sp : SpotterPenaltyState.Default;
            }
            dto.SpotterPenaltyPercent = (int)Math.Round(penalty.GlobalPenalty * 100);

            m_AudioService = ServiceRegistryFeatureExtensions.TryGetOrNullObject(NullThreatAudioService.Instance);
            dto.SirenActive = m_AudioService.GetAudioState().sirenActive;

            PublishWhenComplete(AirDefenseState, NoSourceChecks, () => dto);
        }

        private void FillEmergencyResupplyEligibility(ref AirDefenseDto dto)
        {
            // War-time gate (Act >= Crisis) applies to every per-type button — same gate the
            // request processor enforces server-side. Below the gate, each type is evaluated
            // against its own live installations, ammo deficit, fixed cost and resupply cooldown.
            bool preCrisis = !SystemAPI.TryGetSingleton<CurrentActSingleton>(out var actSingleton)
                || actSingleton.CurrentAct < Act.Crisis;

            var credits = m_StateSystem != null ? m_StateSystem.GetCreditsSnapshot() : AirDefenseCreditsSingleton.Default;
            var cfg = BalanceConfig.Current;

            // Patriot is gated to one resupply per wave (the gun types refill freely). Same wave gate
            // the request processor enforces, so the button disables in lockstep with the backend.
            int currentWave = SystemAPI.TryGetSingleton<WaveStateSingleton>(out var waveState)
                ? waveState.WaveNumber
                : 0;
            bool patriotOnCooldown = AirDefenseEligibility.IsResupplyWaveCooldownActive(
                currentWave,
                credits.LastResupplyWavePatriot,
                AAParams.ForType(cfg, AAType.PatriotSAM).ResupplyCooldownWaves);
            dto.CanResupplyPatriot = ResolveResupplyGate(
                preCrisis, dto.PatriotMaxAmmo, dto.PatriotAmmo, patriotOnCooldown, dto.PatriotResupplyCost,
                AirDefenseEligibility.CanResupplyPatriot, out var patriotReason);
            dto.ResupplyPatriotLockedReasonId = patriotReason;

            // The three gun types are restocked together by one button: aggregate their live/deficit
            // state and sum the flat cost of just the deficit types (the same AAResupplyGroups helper
            // the request processor charges from), so the displayed price equals the charged price.
            // Deficits are read into locals first — dto is a ref param and cannot be captured in the
            // cost lambda; the locals can.
            bool hasLiveGuns = GunHasLive(in dto);
            bool boforsDeficit = UiTypeHasDeficit(in dto, AAType.Bofors40mm);
            bool gepardDeficit = UiTypeHasDeficit(in dto, AAType.Gepard);
            bool heritageDeficit = UiTypeHasDeficit(in dto, AAType.HeritageBofors);
            bool hasGunDeficit = boforsDeficit || gepardDeficit || heritageDeficit;
            int gunsCost = AAResupplyGroups.GunsResupplyCost(cfg, t => t switch
            {
                AAType.Bofors40mm => boforsDeficit,
                AAType.Gepard => gepardDeficit,
                AAType.HeritageBofors => heritageDeficit,
                AAType.PatriotSAM => false,
                _ => throw new InvalidOperationException($"Unhandled AAType: {t}")
            });
            dto.GunsResupplyCost = gunsCost;
            if (preCrisis)
            {
                dto.CanResupplyGuns = false;
                dto.ResupplyGunsLockedReasonId = ReasonIds.AirDefensePreCrisis;
            }
            else
            {
                dto.CanResupplyGuns = AirDefenseEligibility.CanResupplyGuns(
                    hasLiveGuns, hasGunDeficit, gunsCost, World, out var gunsReason);
                dto.ResupplyGunsLockedReasonId = gunsReason;
            }
        }

        private static bool GunHasLive(in AirDefenseDto dto)
            => dto.BoforsMaxAmmo > 0 || dto.GepardMaxAmmo > 0 || dto.HeritageMaxAmmo > 0;

        private static bool UiTypeHasDeficit(in AirDefenseDto dto, AAType type) => type switch
        {
            AAType.Bofors40mm => dto.BoforsMaxAmmo > 0 && dto.BoforsAmmo < dto.BoforsMaxAmmo,
            AAType.Gepard => dto.GepardMaxAmmo > 0 && dto.GepardAmmo < dto.GepardMaxAmmo,
            AAType.HeritageBofors => dto.HeritageMaxAmmo > 0 && dto.HeritageAmmo < dto.HeritageMaxAmmo,
            AAType.PatriotSAM => false,
            _ => throw new InvalidOperationException($"Unhandled AAType: {type}")
        };

        private delegate bool ResupplyPredicate(bool hasLiveOfType, bool hasDeficitOfType, bool onCooldownOfType, long cost, World world, out string reasonId);

        private bool ResolveResupplyGate(
            bool preCrisis,
            int maxAmmoOfType,
            int ammoOfType,
            bool onCooldownOfType,
            int cost,
            ResupplyPredicate predicate,
            out string reasonId)
        {
            if (preCrisis)
            {
                reasonId = ReasonIds.AirDefensePreCrisis;
                return false;
            }

            return predicate(
                hasLiveOfType: maxAmmoOfType > 0,
                hasDeficitOfType: maxAmmoOfType > 0 && ammoOfType < maxAmmoOfType,
                onCooldownOfType,
                cost,
                World,
                out reasonId);
        }

        private void FillAAPlacementEligibility(ref AirDefenseDto dto)
        {
            var balance = BalanceConfig.Current;
            var heritageP = AAParams.ForType(balance, AAType.HeritageBofors);
            var boforsP = AAParams.ForType(balance, AAType.Bofors40mm);
            var gepardP = AAParams.ForType(balance, AAType.Gepard);
            var patriotP = AAParams.ForType(balance, AAType.PatriotSAM);
            int availableManpower;
            using (PerformanceProfiler.Measure("SP:ADUI.Mobilization"))
            {
                availableManpower = (m_MobilizationQuery.TryGetSingleton<MobilizationStateSingleton>(out var mob)
                        ? mob : MobilizationStateSingleton.Default)
                    .AvailableManpower;
            }

            dto.CanPlaceHeritageBofors = AirDefenseEligibility.CanPlaceHeritageBofors(
                dto.HeritageCredits,
                availableManpower,
                heritageP.CrewRequired,
                out var heritageReason);
            dto.HeritageBoforsLockedReasonId = heritageReason;

            dto.CanPlaceDonorPatriot = AirDefenseEligibility.CanPlaceDonorPatriot(
                dto.DonorPatriotCredits,
                availableManpower,
                patriotP.CrewRequired,
                out var donorReason);
            dto.DonorPatriotLockedReasonId = donorReason;

            dto.CanPlacePaidBofors = AirDefenseEligibility.CanPlacePaidBofors(
                boforsP.Price,
                availableManpower,
                boforsP.CrewRequired,
                World,
                out var paidReason);
            dto.PaidBoforsLockedReasonId = paidReason;

            dto.PaidBoforsAffordableCount = AirDefenseEligibility.AffordablePaidAACount(
                boforsP.Price,
                availableManpower,
                boforsP.CrewRequired,
                World);

            dto.CanPlacePaidGepard = AirDefenseEligibility.CanPlacePaidGepard(
                gepardP.Price,
                availableManpower,
                gepardP.CrewRequired,
                World,
                out var paidGepardReason);
            dto.PaidGepardLockedReasonId = paidGepardReason;

            dto.PaidGepardAffordableCount = AirDefenseEligibility.AffordablePaidAACount(
                gepardP.Price,
                availableManpower,
                gepardP.CrewRequired,
                World);

            dto.CanPlacePaidPatriot = AirDefenseEligibility.CanPlacePaidPatriot(
                patriotP.Price,
                availableManpower,
                patriotP.CrewRequired,
                World,
                out var paidPatriotReason);
            dto.PaidPatriotLockedReasonId = paidPatriotReason;

            dto.PaidPatriotAffordableCount = AirDefenseEligibility.AffordablePaidAACount(
                patriotP.Price,
                availableManpower,
                patriotP.CrewRequired,
                World);
        }

        private TriggerOutcome OnEmergencyResupply(int targetTypeId)
        {
            // Normal request button: no vanilla tool activation here, so ECS handoff is
            // intentional. The request processor owns budget/ammo validation and apply.
            // The guns sentinel restocks every gun type in one batch; any other id is a
            // single-type request (only Patriot uses this path now).
            bool gunsMode = targetTypeId == AAResupplyGroups.GunsResupplyTypeId;
            var target = AAType.HeritageBofors;
            if (!gunsMode && !TryDecodeAaType(targetTypeId, out target))
            {
                Log.Warn($"Emergency resupply rejected: invalid AAType id={targetTypeId}");
                return TriggerOutcome.Reject(ReasonIds.AirDefenseUnknownResupply);
            }

            if (TriggerOutcome.IsSimulationPaused(World))
            {
                Log.Info("Emergency resupply rejected: budget pipeline requires unpaused simulation");
                return TriggerOutcome.Reject(ReasonIds.GamePaused);
            }

            var ecb = m_EndFrameBarrier.CreateCommandBuffer();
            var entity = ecb.CreateEntity();
            ecb.AddComponent(entity, new EmergencyResupplyRequest
            {
                Kind = gunsMode ? EmergencyResupplyKind.EmergencyGuns : EmergencyResupplyKind.Emergency,
                Target = target
            });
            Log.Info($"Emergency resupply requested [{(gunsMode ? "Guns" : target.ToString())}]");
            return TriggerOutcome.HandOffToEcs(ecb, entity, SystemAPI.Time.ElapsedTime, TriggerOutcome.CurrentSimulationFrame(World));
        }

        private static bool TryDecodeAaType(int typeId, out AAType type)
        {
            switch (typeId)
            {
                case (int)AAType.HeritageBofors:
                    type = AAType.HeritageBofors;
                    return true;
                case (int)AAType.Bofors40mm:
                    type = AAType.Bofors40mm;
                    return true;
                case (int)AAType.Gepard:
                    type = AAType.Gepard;
                    return true;
                case (int)AAType.PatriotSAM:
                    type = AAType.PatriotSAM;
                    return true;
                default:
                    type = AAType.HeritageBofors;
                    return false;
            }
        }

        private TriggerOutcome OnSetDefensePolicy(int policyId)
        {
            // Normal request button: policy apply belongs to AirDefensePolicySystem,
            // not the UI callback. Unlike placement, this does not need immediate
            // vanilla tool state before returning.
            if (!TryFromUiDefensePolicyId(policyId, out var policy))
            {
                Log.Warn($"OnSetDefensePolicy: invalid policyId={policyId}, ignoring");
                return TriggerOutcome.Reject(ReasonIds.DefenseInvalidPolicy);
            }

            if (TriggerOutcome.IsSimulationPaused(World))
            {
                Log.Info("Defense policy rejected: request pipeline requires unpaused simulation");
                return TriggerOutcome.Reject(ReasonIds.GamePaused);
            }

            var ecb = m_EndFrameBarrier.CreateCommandBuffer();
            var entity = ecb.CreateEntity();
            ecb.AddComponent(entity, new DefensePolicyRequest
            {
                Policy = policy
            });
            if (Log.IsDebugEnabled) Log.Debug($"Created DefensePolicyRequest: {policy}");
            return TriggerOutcome.HandOffToEcs(ecb, entity, SystemAPI.Time.ElapsedTime, TriggerOutcome.CurrentSimulationFrame(World));
        }

        private TriggerOutcome OnTogglePatriotDroneIntercept(bool enabled)
        {
            // Pause-safe defence setting (AXIOM 14 route 3: sync service-host call on the UI
            // thread). The owner — AirDefenseStateSystem, the single writer of the persisted
            // flag — applies the value synchronously here, so the toggle takes effect even
            // while the simulation is paused. The previous ECB → request-entity →
            // GameSimulation consumer never ran in pause, so the button hung "Processing…"
            // until the next unpause. SyncSuccess completes the request in this callback,
            // so the UI never shows a lingering pending state.
            m_PatriotDroneToggleCommand.SetPatriotDroneInterceptImmediate(enabled);
            if (Log.IsDebugEnabled) Log.Debug($"Patriot drone interception set: {enabled}");
            return TriggerOutcome.SyncSuccess();
        }

        // Pause-safe AA auto-resupply toggle (AXIOM 14 route 3: sync service-host call on the UI
        // thread). The owner (AirDefenseStateSystem) applies the rule synchronously, so it takes
        // effect even while paused; the DTO snapshot reflects it on the next push. No request
        // lifecycle — the bridge-less trigger fires this void handler directly.
        private void OnToggleAutoResupply(bool enabled)
        {
            // The command (AirDefenseStateSystem.SetAutoResupplyImmediate) logs the ON/OFF at Info,
            // mirroring the Patriot toggle — no duplicate handler-side log here.
            m_AutoResupplyToggleCommand.SetAutoResupplyImmediate(enabled);
        }

        private static int ToUiDefensePolicyId(DefensePolicy policy)
            => policy switch
            {
                DefensePolicy.HumanitarianShield => 0,
                DefensePolicy.GridIntegrity => 1,
                // Defensive: SanitizePolicyForUi above should never let
                // Unavailable reach here. If a future caller bypasses sanitize,
                // fall back to id=0 (HumanitarianShield) so the UI radio
                // doesn't get a "no selection" state. Throw is reserved for
                // genuine unknown enum values.
                DefensePolicy.Unavailable => 0,
                _ => throw new InvalidOperationException($"Unhandled DefensePolicy: {policy}")
            };

        private static bool TryFromUiDefensePolicyId(int policyId, out DefensePolicy policy)
        {
            switch (policyId)
            {
                case 0:
                    policy = DefensePolicy.HumanitarianShield;
                    return true;
                case 1:
                    policy = DefensePolicy.GridIntegrity;
                    return true;
                default:
                    policy = DefensePolicy.Unavailable;
                    return false;
            }
        }

        /// <summary>
        /// Map the cross-domain null-object sentinel onto the owner business
        /// default before exposing the value to the UI DTO. Unavailable is a
        /// boundary value for <see cref="IDefensePolicyReader"/> consumers
        /// (fail-closed); the owner snapshot must never carry it to the user.
        /// Warn once per session so an owner regression (zero-init mirror,
        /// missing ResetState/Deserialize uplift) stays diagnosable without
        /// per-frame log spam.
        /// </summary>
        private DefensePolicy SanitizePolicyForUi(DefensePolicy policy)
        {
            if (policy != DefensePolicy.Unavailable)
                return policy;

            if (!m_LoggedSentinelLeak)
            {
                m_LoggedSentinelLeak = true;
                Log.Warn("DefensePolicy.Unavailable leaked from owner snapshot — sanitizing to HumanitarianShield for UI. " +
                         "Check AirDefenseStateSystem.m_CreditsLatest seed and AirDefensePolicySystem.ResetState/Deserialize uplift.");
            }
            return DefensePolicy.HumanitarianShield;
        }

        /// <summary>
        /// Activate placement tool for the AA prop prefab (StaticObjectPrefab, not a vanilla building).
        /// Called from React UI via trigger("CivicSurvival", "placeAABuilding", payload).
        /// This path is intentionally synchronous unlike the other AirDefense
        /// buttons: build placement starts from pause, so delaying tool activation
        /// or <see cref="AAPlacementPending"/> publication to an ECS update would
        /// make the click appear to do nothing until unpause.
        /// </summary>
        private TriggerOutcome OnPlaceAABuilding(string payload)
        {
            var placement = DecodeAAPlacementPayload(payload);
            Log.Info($"OnPlaceAABuilding called with: {placement.PrefabName}, mode={placement.Mode}");

            return TriggerOutcome.Supersede(ReasonIds.AaCancelled, token =>
                ActivateAAPlacementImmediately(placement, token));
        }

        private void ActivateAAPlacementImmediately(AAPlacementPayload placement, RequestToken token)
        {
            var result = m_PlacementCommand.TryActivatePlacement(
                placement.PrefabName,
                placement.Mode,
                token);

            if (!result.Activated)
                RequestResultBridge.Complete(token, RequestStatus.Failed, result.ReasonId.ToString());
        }

        private static AAPlacementPayload DecodeAAPlacementPayload(string payload)
        {
            if (string.IsNullOrWhiteSpace(payload))
                return new AAPlacementPayload(string.Empty, AAPlacementMode.Paid);

            var parts = payload.Split('|');
            if (parts.Length < 2)
                return new AAPlacementPayload(payload, AAPlacementMode.Paid);

            var mode = Enum.TryParse<AAPlacementMode>(parts[1], ignoreCase: false, out var parsedMode)
                ? parsedMode
                : AAPlacementMode.Paid;
            return new AAPlacementPayload(parts[0], mode);
        }

        private readonly struct AAPlacementPayload
        {
            public readonly string PrefabName;
            public readonly AAPlacementMode Mode;

            public AAPlacementPayload(string prefabName, AAPlacementMode mode)
            {
                PrefabName = prefabName;
                Mode = mode;
            }
        }

    }
}
