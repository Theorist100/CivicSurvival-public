using System;
using System.Text;
using Unity.Entities;
using Game;
using Game.Simulation;
using CivicSurvival.Core.Components.Domain.AirDefense;
using CivicSurvival.Core.Components.Domain.Mobilization;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Components.Threats;
using CivicSurvival.Core.Components.Requests;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces.Domain.Economy;
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
    [HandlesRequestKind(RequestKind.DefensePolicy)]
    public partial class AirDefenseUISystem : CivicUIPanelSystem
    {
        private EndFrameBarrier m_EndFrameBarrier = null!;
        private IThreatAudioService m_AudioService = null!;
        private IBuildingPlacementService m_PlacementCommand = null!;
        private IPatriotDroneToggleCommandService m_PatriotDroneToggleCommand = null!;
        private IAutoResupplyToggleCommandService m_AutoResupplyToggleCommand = null!;
        private IDefensePolicyCommandService m_DefensePolicyCommand = null!;

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
            m_PlacementCommand = ServiceRegistry.Instance.Require<IBuildingPlacementService>();
            m_PatriotDroneToggleCommand = ServiceRegistry.Instance.Require<IPatriotDroneToggleCommandService>();
            m_AutoResupplyToggleCommand = ServiceRegistry.Instance.Require<IAutoResupplyToggleCommandService>();
            m_DefensePolicyCommand = ServiceRegistry.Instance.Require<IDefensePolicyCommandService>();
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
            // Cancel rides no request bridge of its own: the open placement request is
            // completed by the lifecycle system once the tool returns to Default (the
            // same terminal path Esc takes), so a second lifecycle here would double-close.
            Triggers.Add(CancelAAPlacement, FeatureIds.AirDefense, OnCancelAAPlacement);
#if DEBUG
            Triggers.Add(DebugDrainAAAmmo, FeatureIds.AirDefense, OnDebugDrainAAAmmo);
#endif
        }

#if DEBUG
        /// <summary>
        /// DEBUG: zero every AA installation's ammo so the pause-safe emergency
        /// resupply route can be tested on demand (dev panel button).
        /// </summary>
        private void OnDebugDrainAAAmmo()
        {
            int drained = 0;
            foreach (var aa in SystemAPI.Query<RefRW<AirDefenseInstallation>>())
            {
                if (aa.ValueRO.CurrentAmmo > 0)
                {
                    aa.ValueRW.CurrentAmmo = 0;
                    drained++;
                }
            }
            Log.Info($"DEBUG: drained ammo on {drained} AA installations");
        }
#endif

        protected override void OnPanelUpdate()
        {
            var dto = new AirDefenseDto
            {
                // PatriotResupplyCost (the rockets-button price) is the summed group cost, set in
                // FillEmergencyResupplyEligibility once the live per-type deficits are known.
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
                // By munition kind, not by gun type: these two pairs are exactly what the two
                // restock buttons have always priced. A new AA type needs no line here.
                dto.PatriotAmmo = stats.GetAmmo(AmmoKind.Rockets);
                dto.PatriotMaxAmmo = stats.GetMaxAmmo(AmmoKind.Rockets);
                dto.GunsAmmo = stats.GetAmmo(AmmoKind.Shells);
                dto.GunsMaxAmmo = stats.GetMaxAmmo(AmmoKind.Shells);

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
            // After the credits are copied above: the roster gates every grant-funded option on
            // HeritageCredits / DonorPatriotCredits, and reading them early would offer the grant
            // rows as permanently exhausted.
            FillAAPlacementRoster(ref dto);

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

            var stats = m_StateSystem != null ? m_StateSystem.GetUiStatsSnapshot() : AirDefenseUiStatsSnapshot.Empty;

            // Missile types (Patriot, Hawk) share ONE "restock rockets" button — the rocket-kind
            // mirror of the guns button. Each is gated to one resupply per wave; a type is eligible
            // iff it has a deficit AND is off its wave cooldown. The button's price sums only the
            // eligible types, and it enables iff at least one is eligible — same predicate the
            // request processor enforces, so the button disables in lockstep with the backend.
            int currentWave = SystemAPI.TryGetSingleton<WaveStateSingleton>(out var waveState)
                ? waveState.WaveNumber
                : 0;
            bool RocketEligibleUi(AAType t) => TypeHasDeficit(in stats, t)
                && !AirDefenseEligibility.IsResupplyWaveCooldownActive(
                    currentWave, credits.GetLastResupplyWave(t), AAParams.ForType(cfg, t).ResupplyCooldownWaves);
            bool hasLiveRockets = stats.GetMaxAmmo(AmmoKind.Rockets) > 0;
            bool hasRocketEligible = false;
            for (int i = 0; i < AirDefenseUiStatsSnapshot.TypeCount; i++)
            {
                var t = (AAType)i;
                if (t.AmmoKind() == AmmoKind.Rockets && RocketEligibleUi(t)) { hasRocketEligible = true; break; }
            }
            int rocketsCost = AAResupplyGroups.RocketsResupplyCost(cfg, RocketEligibleUi);
            dto.PatriotResupplyCost = rocketsCost;
            if (preCrisis)
            {
                dto.CanResupplyPatriot = false;
                dto.ResupplyPatriotLockedReasonId = ReasonIds.AirDefensePreCrisis;
            }
            else
            {
                dto.CanResupplyPatriot = AirDefenseEligibility.CanResupplyGuns(
                    hasLiveRockets, hasRocketEligible, rocketsCost, World, out var patriotReason);
                dto.ResupplyPatriotLockedReasonId = patriotReason;
            }

            // Every shell-eating type is restocked together by one button: aggregate their
            // live/deficit state and sum the flat cost of just the deficit types (the same
            // AAResupplyGroups helper the request processor charges from), so the displayed price
            // equals the charged price. Membership is decided by the type's munition kind, so a new
            // gun is priced here the day it exists — no case to add. (stats resolved above.)
            bool hasLiveGuns = stats.GetMaxAmmo(AmmoKind.Shells) > 0;
            bool hasGunDeficit = false;
            for (int i = 0; i < AirDefenseUiStatsSnapshot.TypeCount; i++)
            {
                var t = (AAType)i;
                if (t.AmmoKind() == AmmoKind.Shells && TypeHasDeficit(in stats, t))
                {
                    hasGunDeficit = true;
                    break;
                }
            }
            int gunsCost = AAResupplyGroups.GunsResupplyCost(
                cfg, t => t.AmmoKind() == AmmoKind.Shells && TypeHasDeficit(in stats, t));
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


        /// <summary>
        /// Deficit of one AA type, read from the stats snapshot rather than the DTO: the wire now
        /// carries munition-kind totals, but the price is charged per type (only the types actually
        /// short are billed), so the per-type read model stays the source here.
        /// </summary>
        private static bool TypeHasDeficit(in AirDefenseUiStatsSnapshot stats, AAType type)
        {
            int max = stats.GetMaxAmmo(type);
            return max > 0 && stats.GetAmmo(type) < max;
        }

        /// <summary>Sentinel for AaPlacementOptionEntry.CreditsLeft: this option is bought with
        /// money, so no grant counter applies.</summary>
        private const int NotCreditFunded = -1;

        /// <summary>
        /// Build the placement roster: one row per catalogue option, each priced, gated and
        /// counted on its own. The panel renders whatever this returns, so an AA added to
        /// <see cref="AAPlacementCatalog"/> shows up with no C# and no TypeScript touched here.
        /// </summary>
        private void FillAAPlacementRoster(ref AirDefenseDto dto)
        {
            var balance = BalanceConfig.Current;
            int availableManpower;
            using (PerformanceProfiler.Measure("SP:ADUI.Mobilization"))
            {
                availableManpower = (m_MobilizationQuery.TryGetSingleton<MobilizationStateSingleton>(out var mob)
                        ? mob : MobilizationStateSingleton.Default)
                    .AvailableManpower;
            }

            var stats = m_StateSystem != null ? m_StateSystem.GetUiStatsSnapshot() : AirDefenseUiStatsSnapshot.Empty;
            var wallet = ServiceRegistryFeatureExtensions.TryGetOrNullObject(NullShadowWalletService.Instance);

            var sb = new StringBuilder(1024);
            sb.Append('[');
            bool first = true;
            foreach (var option in AAPlacementCatalog.All)
            {
                // No shadow economy in this save, no black market: the option is absent rather than
                // locked. A row the player can never unlock is noise, not a goal — the wave gate that
                // closes ShadowEconomy also closes the wallet, and that is what this reads.
                if (option.Mode == AAPlacementMode.BlackMarket && !wallet.HasWallet)
                    continue;

                var p = AAParams.ForOption(balance, option.Type, option.Mode);
                // Credit-funded options spend a grant, not money: their gate counts credits and
                // their cost is zero. NotCreditFunded marks a purchase, so the panel can tell an
                // exhausted grant (0 left) from a plain buy.
                //
                // Spelled out per mode on purpose, unlike the barrels: a new AA must cost nothing
                // to add, but a new SOURCE has to state whether it spends a grant or money — there
                // is no safe default to fall through to.
                int creditsLeft = option.Mode switch
                {
                    AAPlacementMode.Heritage => dto.HeritageCredits,
                    AAPlacementMode.DonorCredit => dto.DonorPatriotCredits,
                    AAPlacementMode.Paid => NotCreditFunded,
                    AAPlacementMode.BlackMarket => NotCreditFunded,
                    _ => throw new InvalidOperationException($"Unhandled AAPlacementMode: {option.Mode}")
                };

                bool canPlace;
                string reasonId;
                int affordable;
                if (creditsLeft >= 0)
                {
                    canPlace = AirDefenseEligibility.CanPlaceCreditAA(
                        creditsLeft, availableManpower, p.CrewRequired, out reasonId);
                    affordable = creditsLeft;
                }
                else if (option.Mode == AAPlacementMode.BlackMarket)
                {
                    canPlace = AirDefenseEligibility.CanPlaceShadowAA(
                        p.Price, availableManpower, p.CrewRequired, wallet, out reasonId);
                    affordable = AirDefenseEligibility.AffordableShadowAACount(
                        p.Price, availableManpower, p.CrewRequired, wallet);
                }
                else
                {
                    canPlace = AirDefenseEligibility.CanPlacePaidAA(
                        p.Price, availableManpower, p.CrewRequired, World, out reasonId);
                    affordable = AirDefenseEligibility.AffordablePaidAACount(
                        p.Price, availableManpower, p.CrewRequired, World);
                }

                var entry = new AaPlacementOptionEntry
                {
                    Prefab = option.Prefab,
                    Mode = (int)option.Mode,
                    NameKey = option.NameKey,
                    Icon = option.Icon,
                    Munition = (int)option.Type.AmmoKind(),
                    Range = p.Range,
                    InterceptShahed = p.InterceptChanceShahed,
                    InterceptBallistic = p.InterceptChanceBallistic,
                    Crew = p.CrewRequired,
                    Deployed = stats.GetCount(option.Type),
                    Cost = creditsLeft >= 0 ? 0 : p.Price,
                    CreditsLeft = creditsLeft,
                    AffordableCount = affordable,
                    CanPlace = canPlace,
                    LockedReasonId = reasonId ?? string.Empty
                };
                if (!first) sb.Append(',');
                first = false;
                entry.WriteTo(sb);
            }

            sb.Append(']');
            dto.AaRosterJson = sb.ToString();
        }

        private TriggerOutcome OnEmergencyResupply(int targetTypeId)
        {
            // Normal request button: no vanilla tool activation here, so ECS handoff is
            // intentional. The request processor owns budget/ammo validation and apply.
            // The guns/rockets sentinels each restock their whole munition group in one batch; any
            // other id is a single-type request.
            bool gunsMode = targetTypeId == AAResupplyGroups.GunsResupplyTypeId;
            bool rocketsMode = targetTypeId == AAResupplyGroups.RocketsResupplyTypeId;
            var target = AAType.HeritageBofors;
            if (!gunsMode && !rocketsMode && !TryDecodeAaType(targetTypeId, out target))
            {
                Log.Warn($"Emergency resupply rejected: invalid AAType id={targetTypeId}");
                return TriggerOutcome.Reject(ReasonIds.AirDefenseUnknownResupply);
            }

            EmergencyResupplyKind kind;
            if (gunsMode) kind = EmergencyResupplyKind.EmergencyGuns;
            else if (rocketsMode) kind = EmergencyResupplyKind.EmergencyRockets;
            else kind = EmergencyResupplyKind.Emergency;

            string label;
            if (gunsMode) label = "Guns";
            else if (rocketsMode) label = "Rockets";
            else label = target.ToString();

            // No pause reject here, by design — the consumer runs in ModificationEnd and
            // pays synchronously through BudgetTransactionResolver (AXIOM 14).
            var ecb = m_EndFrameBarrier.CreateCommandBuffer();
            var entity = ecb.CreateEntity();
            ecb.AddComponent(entity, new EmergencyResupplyRequest { Kind = kind, Target = target });
            Log.Info($"Emergency resupply requested [{label}]");
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
                case (int)AAType.HawkSAM:
                    type = AAType.HawkSAM;
                    return true;
                default:
                    type = AAType.HeritageBofors;
                    return false;
            }
        }

        private TriggerOutcome OnSetDefensePolicy(int policyId)
        {
            if (!TryFromUiDefensePolicyId(policyId, out var policy))
            {
                Log.Warn($"OnSetDefensePolicy: invalid policyId={policyId}, ignoring");
                return TriggerOutcome.Reject(ReasonIds.DefenseInvalidPolicy);
            }

            // No pause reject here, by design — pause-safe defence setting (AXIOM 14 route 3:
            // sync service-host call on the UI thread). The owner (AirDefensePolicySystem,
            // single writer of the persisted policy) applies the value synchronously, so the
            // selector works while the simulation is paused — same shape as the Patriot and
            // auto-resupply toggles below.
            m_DefensePolicyCommand.SetDefensePolicyImmediate(policy);
            if (Log.IsDebugEnabled) Log.Debug($"Defense policy set synchronously: {policy}");
            return TriggerOutcome.SyncSuccess();
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
        /// or <c>BuildingPlacementPending</c> publication to an ECS update would
        /// make the click appear to do nothing until unpause.
        /// </summary>
        private TriggerOutcome OnPlaceAABuilding(string payload)
        {
            var placement = DecodeAAPlacementPayload(payload);
            Log.Info($"OnPlaceAABuilding called with: {placement.PrefabName}, mode={placement.Mode}");

            return TriggerOutcome.Supersede(ReasonIds.AaCancelled, token =>
                ActivateAAPlacementImmediately(placement, token));
        }

        // Synchronous like activation (Axiom 14): the click may land while paused, and the
        // tool must visibly leave the cursor before the callback returns.
        private void OnCancelAAPlacement()
        {
            m_PlacementCommand.CancelActivePlacement();
        }

        private void ActivateAAPlacementImmediately(AAPlacementPayload placement, RequestToken token)
        {
            var result = m_PlacementCommand.TryActivatePlacement(
                BuildingPlacementKind.AirDefense,
                placement.PrefabName,
                (byte)placement.Mode,
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
