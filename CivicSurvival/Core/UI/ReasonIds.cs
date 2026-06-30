using CivicSurvival.Core.Attributes;

namespace CivicSurvival.Core.Types
{
    /// <summary>
    /// Canonical locale ids for UI request rejection reasons.
    /// </summary>
    public static partial class ReasonIds
    {
        [UiReasonId("internal error")]
        public static readonly ReasonId InternalError = ReasonId.Of("UI_INTERNAL_ERROR");

        [UiReasonId("aa ammo full")]
        public static readonly ReasonId AaAmmoFull = ReasonId.Of("UI_AA_AMMO_FULL");

        [UiReasonId("aa cancelled")]
        public static readonly ReasonId AaCancelled = ReasonId.Of("UI_AA_CANCELLED");

        [UiReasonId("aa config error")]
        public static readonly ReasonId AaConfigError = ReasonId.Of("UI_AA_CONFIG_ERROR");

        [UiReasonId("aa resupply on cooldown")]
        public static readonly ReasonId AaResupplyCooldown = ReasonId.Of("UI_AA_RESUPPLY_COOLDOWN");

        [UiReasonId("aa insufficient funds")]
        public static readonly ReasonId AaInsufficientFunds = ReasonId.Of("UI_AA_INSUFFICIENT_FUNDS");

        [UiReasonId("aa insufficient manpower")]
        public static readonly ReasonId AaInsufficientManpower = ReasonId.Of("UI_AA_INSUFFICIENT_MANPOWER");

        [UiReasonId("aa no credit")]
        public static readonly ReasonId AaNoCredit = ReasonId.Of("UI_AA_NO_CREDIT");

        [UiReasonId("aa no live installations")]
        public static readonly ReasonId AaNoLiveInstallations = ReasonId.Of("UI_AA_NO_LIVE_INSTALLATIONS");

        [UiReasonId("aa placement failed")]
        public static readonly ReasonId AaPlacementFailed = ReasonId.Of("UI_AA_PLACEMENT_FAILED");

        [UiReasonId("aa budget payment failed")]
        public static readonly ReasonId AaBudgetFailed = ReasonId.Of("UI_AA_BUDGET_FAILED");

        [UiReasonId("aa placement building lost")]
        public static readonly ReasonId AaBuildingLost = ReasonId.Of("UI_AA_BUILDING_LOST");

        [UiReasonId("aa duplicate placement")]
        public static readonly ReasonId AaDuplicate = ReasonId.Of("UI_AA_DUPLICATE");

        [UiReasonId("airdefense action failed")]
        public static readonly ReasonId AirDefenseActionFailed = ReasonId.Of("UI_AIRDEFENSE_ACTION_FAILED");

        [UiReasonId("airdefense precrisis")]
        public static readonly ReasonId AirDefensePreCrisis = ReasonId.Of("UI_AIRDEFENSE_PRECRISIS");

        [UiReasonId("airdefense unknown resupply")]
        public static readonly ReasonId AirDefenseUnknownResupply = ReasonId.Of("UI_AIRDEFENSE_UNKNOWN_RESUPPLY");

        [UiReasonId("arena refresh telemetry disabled")]
        public static readonly ReasonId ArenaRefreshTelemetryDisabled = ReasonId.Of("UI_ARENA_REFRESH_TELEMETRY_DISABLED");

        [UiReasonId("arena partial refresh")]
        public static readonly ReasonId ArenaPartialRefresh = ReasonId.Of("UI_ARENA_PARTIAL_REFRESH");

        [UiReasonId("arena refresh inflight")]
        public static readonly ReasonId ArenaRefreshInflight = ReasonId.Of("UI_ARENA_REFRESH_INFLIGHT");

        [UiReasonId("autodispatch state unavailable")]
        public static readonly ReasonId AutoDispatchStateUnavailable = ReasonId.Of("UI_AUTODISPATCH_STATE_UNAVAILABLE");

        [UiReasonId("backup modernization config error")]
        public static readonly ReasonId BackupModernizationConfigError = ReasonId.Of("UI_BACKUP_MODERNIZATION_CONFIG_ERROR");

        [UiReasonId("backup modernization cooldown")]
        public static readonly ReasonId BackupModernizationCooldown = ReasonId.Of("UI_BACKUP_MODERNIZATION_COOLDOWN");

        [UiReasonId("backup modernization insufficient funds")]
        public static readonly ReasonId BackupModernizationInsufficientFunds = ReasonId.Of("UI_BACKUP_MODERNIZATION_INSUFFICIENT_FUNDS");

        [UiReasonId("backup modernization invalid contractor")]
        public static readonly ReasonId BackupModernizationInvalidContractor = ReasonId.Of("UI_BACKUP_MODERNIZATION_INVALID_CONTRACTOR");

        [UiReasonId("backup modernization no targets")]
        public static readonly ReasonId BackupModernizationNoTargets = ReasonId.Of("UI_BACKUP_MODERNIZATION_NO_TARGETS");

        [UiReasonId("backup modernization pending")]
        public static readonly ReasonId BackupModernizationPending = ReasonId.Of("UI_BACKUP_MODERNIZATION_PENDING");

        [UiReasonId("backup policy invalid")]
        public static readonly ReasonId BackupPolicyInvalid = ReasonId.Of("UI_BACKUP_POLICY_INVALID");

        [UiReasonId("backup policy state unavailable")]
        public static readonly ReasonId BackupPolicyStateUnavailable = ReasonId.Of("UI_BACKUP_POLICY_STATE_UNAVAILABLE");

        [UiReasonId("district invalid input")]
        public static readonly ReasonId DistrictInvalidInput = ReasonId.Of("UI_DISTRICT_INVALID_INPUT");

        [UiReasonId("civilian repair already active")]
        public static readonly ReasonId CivilianRepairAlreadyActive = ReasonId.Of("UI_CIVILIAN_REPAIR_ALREADY_ACTIVE");

        [UiReasonId("civilian repair budget pending")]
        public static readonly ReasonId CivilianRepairBudgetPending = ReasonId.Of("UI_CIVILIAN_REPAIR_BUDGET_PENDING");

        [UiReasonId("civilian repair insufficient funds")]
        public static readonly ReasonId CivilianRepairInsufficientFunds = ReasonId.Of("UI_CIVILIAN_REPAIR_INSUFFICIENT_FUNDS");

        [UiReasonId("civilian repair not found")]
        public static readonly ReasonId CivilianRepairNotFound = ReasonId.Of("UI_CIVILIAN_REPAIR_NOT_FOUND");

        [UiReasonId("civilian repair refund failed")]
        public static readonly ReasonId CivilianRepairRefundFailed = ReasonId.Of("UI_CIVILIAN_REPAIR_REFUND_FAILED");

        [UiReasonId("invalid repair type")]
        public static readonly ReasonId InvalidRepairType = ReasonId.Of("UI_INVALID_REPAIR_TYPE");

        [UiReasonId("cognitive invalid mode")]
        public static readonly ReasonId CognitiveInvalidMode = ReasonId.Of("UI_COGNITIVE_INVALID_MODE");

        [UiReasonId("cognitive invalid narrative mode")]
        public static readonly ReasonId CognitiveInvalidNarrativeMode = ReasonId.Of("UI_COGNITIVE_INVALID_NARRATIVE_MODE");

        [UiReasonId("contract insufficient funds")]
        public static readonly ReasonId ContractInsufficientFunds = ReasonId.Of("UI_CONTRACT_INSUFFICIENT_FUNDS");

        [UiReasonId("contract invalid price")]
        public static readonly ReasonId ContractInvalidPrice = ReasonId.Of("UI_CONTRACT_INVALID_PRICE");

        [UiReasonId("contract offer stale")]
        public static readonly ReasonId ContractOfferStale = ReasonId.Of("UI_CONTRACT_OFFER_STALE");

        [UiReasonId("corruption window closed")]
        public static readonly ReasonId CorruptionWindowClosed = ReasonId.Of("UI_CORRUPTION_WINDOW_CLOSED");

        [UiReasonId("countermeasures invalid choice")]
        public static readonly ReasonId CountermeasuresInvalidChoice = ReasonId.Of("UI_COUNTERMEASURES_INVALID_CHOICE");

        [UiReasonId("countermeasures locked")]
        public static readonly ReasonId CountermeasuresLocked = ReasonId.Of("UI_COUNTERMEASURES_LOCKED");

        [UiReasonId("counter choice not available")]
        public static readonly ReasonId CounterChoiceNotAvailable = ReasonId.Of("UI_COUNTER_CHOICE_NOT_AVAILABLE");

        [UiReasonId("counter heat level critical")]
        public static readonly ReasonId CounterHeatLevelCritical = ReasonId.Of("UI_COUNTER_HEAT_LEVEL_CRITICAL");

        [UiReasonId("counter heat level danger")]
        public static readonly ReasonId CounterHeatLevelDanger = ReasonId.Of("UI_COUNTER_HEAT_LEVEL_DANGER");

        [UiReasonId("counter heat level warning")]
        public static readonly ReasonId CounterHeatLevelWarning = ReasonId.Of("UI_COUNTER_HEAT_LEVEL_WARNING");

        [UiReasonId("counter heat level safe")]
        public static readonly ReasonId CounterHeatLevelSafe = ReasonId.Of("UI_COUNTER_HEAT_LEVEL_SAFE");

        [UiReasonId("counter phase idle")]
        public static readonly ReasonId CounterPhaseIdle = ReasonId.Of("UI_COUNTER_PHASE_IDLE");

        [UiReasonId("counter phase suspicion")]
        public static readonly ReasonId CounterPhaseSuspicion = ReasonId.Of("UI_COUNTER_PHASE_SUSPICION");

        [UiReasonId("counter phase investigation")]
        public static readonly ReasonId CounterPhaseInvestigation = ReasonId.Of("UI_COUNTER_PHASE_INVESTIGATION");

        [UiReasonId("counter phase waiting decision")]
        public static readonly ReasonId CounterPhaseWaitingDecision = ReasonId.Of("UI_COUNTER_PHASE_WAITING_DECISION");

        [UiReasonId("counter phase article published")]
        public static readonly ReasonId CounterPhaseArticlePublished = ReasonId.Of("UI_COUNTER_PHASE_ARTICLE_PUBLISHED");

        [UiReasonId("counter phase police decision")]
        public static readonly ReasonId CounterPhasePoliceDecision = ReasonId.Of("UI_COUNTER_PHASE_POLICE_DECISION");

        [UiReasonId("counter phase under investigation")]
        public static readonly ReasonId CounterPhaseUnderInvestigation = ReasonId.Of("UI_COUNTER_PHASE_UNDER_INVESTIGATION");

        [UiReasonId("counter phase arrested")]
        public static readonly ReasonId CounterPhaseArrested = ReasonId.Of("UI_COUNTER_PHASE_ARRESTED");

        [UiReasonId("counter phase unknown")]
        public static readonly ReasonId CounterPhaseUnknown = ReasonId.Of("UI_COUNTER_PHASE_UNKNOWN");

        [UiReasonId("defense invalid policy")]
        public static readonly ReasonId DefenseInvalidPolicy = ReasonId.Of("UI_DEFENSE_INVALID_POLICY");

        [UiReasonId("district toggle unknown category")]
        public static readonly ReasonId DistrictToggleUnknownCategory = ReasonId.Of("UI_DISTRICT_TOGGLE_UNKNOWN_CATEGORY");

        [UiReasonId("district toggle unknown schedule")]
        public static readonly ReasonId DistrictToggleUnknownSchedule = ReasonId.Of("UI_DISTRICT_TOGGLE_UNKNOWN_SCHEDULE");

        [UiReasonId("district toggle unknown type")]
        public static readonly ReasonId DistrictToggleUnknownType = ReasonId.Of("UI_DISTRICT_TOGGLE_UNKNOWN_TYPE");

        [UiReasonId("donor conference unavailable")]
        public static readonly ReasonId DonorConferenceUnavailable = ReasonId.Of("UI_DONOR_CONFERENCE_UNAVAILABLE");

        [UiReasonId("donor dialog already open")]
        public static readonly ReasonId DonorDialogAlreadyOpen = ReasonId.Of("UI_DONOR_DIALOG_ALREADY_OPEN");

        [UiReasonId("donor no active dialog")]
        public static readonly ReasonId DonorNoActiveDialog = ReasonId.Of("UI_DONOR_NO_ACTIVE_DIALOG");

        [UiReasonId("donor precrisis locked")]
        public static readonly ReasonId DonorPrecrisisLocked = ReasonId.Of("UI_DONOR_PRECRISIS_LOCKED");

        [UiReasonId("donor unknown dialog action")]
        public static readonly ReasonId DonorUnknownDialogAction = ReasonId.Of("UI_DONOR_UNKNOWN_DIALOG_ACTION");

        [UiReasonId("donor unknown selection")]
        public static readonly ReasonId DonorUnknownSelection = ReasonId.Of("UI_DONOR_UNKNOWN_SELECTION");

        [UiReasonId("donor selection rejected")]
        public static readonly ReasonId DonorSelectionRejected = ReasonId.Of("UI_DONOR_SELECTION_REJECTED");

        [UiReasonId("donor trust refused")]
        public static readonly ReasonId DonorTrustRefused = ReasonId.Of("UI_DONOR_TRUST_REFUSED");

        [UiReasonId("donor funds unavailable")]
        public static readonly ReasonId DonorFundsUnavailable = ReasonId.Of("UI_DONOR_FUNDS_UNAVAILABLE");

        [UiReasonId("donor trust insufficient")]
        public static readonly ReasonId DonorTrustInsufficient = ReasonId.Of("UI_DONOR_TRUST_INSUFFICIENT");

        [UiReasonId("donor shock insufficient")]
        public static readonly ReasonId DonorShockInsufficient = ReasonId.Of("UI_DONOR_SHOCK_INSUFFICIENT");

        [UiReasonId("donor generator cap")]
        public static readonly ReasonId DonorGeneratorCap = ReasonId.Of("UI_DONOR_GENERATOR_CAP");

        [UiReasonId("donor power unavailable")]
        public static readonly ReasonId DonorPowerUnavailable = ReasonId.Of("UI_DONOR_POWER_UNAVAILABLE");

        [UiReasonId("donor patriot cap")]
        public static readonly ReasonId DonorPatriotCap = ReasonId.Of("UI_DONOR_PATRIOT_CAP");

        [UiReasonId("donor defense unavailable")]
        public static readonly ReasonId DonorDefenseUnavailable = ReasonId.Of("UI_DONOR_DEFENSE_UNAVAILABLE");

        [UiReasonId("generic singleton not ready")]
        public static readonly ReasonId GenericSingletonNotReady = ReasonId.Of("UI_GENERIC_SINGLETON_NOT_READY");

        [UiReasonId("generic insufficient funds")]
        public static readonly ReasonId InsufficientFunds = ReasonId.Of("UI_INSUFFICIENT_FUNDS");

        [UiReasonId("game is paused")]
        public static readonly ReasonId GamePaused = ReasonId.Of("UI_GAME_PAUSED");

        [UiReasonId("civilian shadow repair insufficient funds")]
        public static readonly ReasonId InfraCivilianShadowInsufficient = ReasonId.Of("INFRA_CIV_SHADOW_INSUFFICIENT");

        [UiReasonId("grid invalid schedule")]
        public static readonly ReasonId GridInvalidSchedule = ReasonId.Of("UI_GRID_INVALID_SCHEDULE");

        [UiReasonId("gw duplicate operation")]
        public static readonly ReasonId GwDuplicateOperation = ReasonId.Of("UI_GW_DUPLICATE_OPERATION");

        [UiReasonId("gw insufficient funds")]
        public static readonly ReasonId GwInsufficientFunds = ReasonId.Of("UI_GW_INSUFFICIENT_FUNDS");


        [UiReasonId("gw intel config error")]
        public static readonly ReasonId GwIntelConfigError = ReasonId.Of("UI_GW_INTEL_CONFIG_ERROR");

        [UiReasonId("gw intel max")]
        public static readonly ReasonId GwIntelMax = ReasonId.Of("UI_GW_INTEL_MAX");

        [UiReasonId("gw locked reason")]
        public static readonly ReasonId GwLockedReason = ReasonId.Of("UI_GW_LOCKED_REASON");

        [UiReasonId("gw not ready")]
        public static readonly ReasonId GwNotReady = ReasonId.Of("UI_GW_NOT_READY");

        [UiReasonId("gw no active operation")]
        public static readonly ReasonId GwNoActiveOperation = ReasonId.Of("UI_GW_NO_ACTIVE_OPERATION");

        [UiReasonId("gw no empty slot")]
        public static readonly ReasonId GwNoEmptySlot = ReasonId.Of("UI_GW_NO_EMPTY_SLOT");

        [UiReasonId("gw no arsenal")]
        public static readonly ReasonId GwNoArsenal = ReasonId.Of("UI_GW_NO_ARSENAL");

        [UiReasonId("gw system unavailable")]
        public static readonly ReasonId GwSystemUnavailable = ReasonId.Of("UI_GW_SYSTEM_UNAVAILABLE");

        [UiReasonId("gw unknown action")]
        public static readonly ReasonId GwUnknownAction = ReasonId.Of("UI_GW_UNKNOWN_ACTION");

        [UiReasonId("gw unknown attack")]
        public static readonly ReasonId GwUnknownAttack = ReasonId.Of("UI_GW_UNKNOWN_ATTACK");

        [UiReasonId("gw wallet unavailable")]
        public static readonly ReasonId GwWalletUnavailable = ReasonId.Of("UI_GW_WALLET_UNAVAILABLE");

        [UiReasonId("hero budget pending")]
        public static readonly ReasonId HeroBudgetPending = ReasonId.Of("UI_HERO_BUDGET_PENDING");

        [UiReasonId("hero config error")]
        public static readonly ReasonId HeroConfigError = ReasonId.Of("UI_HERO_CONFIG_ERROR");

        [UiReasonId("hero insufficient funds")]
        public static readonly ReasonId HeroInsufficientFunds = ReasonId.Of("UI_HERO_INSUFFICIENT_FUNDS");

        [UiReasonId("hero invalid mode")]
        public static readonly ReasonId HeroInvalidMode = ReasonId.Of("UI_HERO_INVALID_MODE");

        [UiReasonId("hero not deployed")]
        public static readonly ReasonId HeroNotDeployed = ReasonId.Of("UI_HERO_NOT_DEPLOYED");

        [UiReasonId("hero prewar locked")]
        public static readonly ReasonId HeroPrewarLocked = ReasonId.Of("UI_HERO_PREWAR_LOCKED");

        [UiReasonId("hero recall first")]
        public static readonly ReasonId HeroRecallFirst = ReasonId.Of("UI_HERO_RECALL_FIRST");

        [UiReasonId("hero system unavailable")]
        public static readonly ReasonId HeroSystemUnavailable = ReasonId.Of("UI_HERO_SYSTEM_UNAVAILABLE");

        [UiReasonId("insider already active")]
        public static readonly ReasonId InsiderAlreadyActive = ReasonId.Of("UI_INSIDER_ALREADY_ACTIVE");

        [UiReasonId("insider config error")]
        public static readonly ReasonId InsiderConfigError = ReasonId.Of("UI_INSIDER_CONFIG_ERROR");

        [UiReasonId("insider insufficient funds")]
        public static readonly ReasonId InsiderInsufficientFunds = ReasonId.Of("UI_INSIDER_INSUFFICIENT_FUNDS");


        [UiReasonId("insider price changed")]
        public static readonly ReasonId InsiderPriceChanged = ReasonId.Of("UI_INSIDER_PRICE_CHANGED");

        [UiReasonId("insider wallet unavailable")]
        public static readonly ReasonId InsiderWalletUnavailable = ReasonId.Of("UI_INSIDER_WALLET_UNAVAILABLE");

        [UiReasonId("intel locked")]
        public static readonly ReasonId IntelLocked = ReasonId.Of("UI_INTEL_LOCKED");

        [UiReasonId("intel request pending")]
        public static readonly ReasonId IntelRequestPending = ReasonId.Of("UI_INTEL_REQUEST_PENDING");

        [UiReasonId("intel price changed")]
        public static readonly ReasonId IntelPriceChanged = ReasonId.Of("UI_INTEL_PRICE_CHANGED");

        [UiReasonId("intel wallet unavailable")]
        public static readonly ReasonId IntelWalletUnavailable = ReasonId.Of("UI_INTEL_WALLET_UNAVAILABLE");

        [UiReasonId("intel wave active")]
        public static readonly ReasonId IntelWaveActive = ReasonId.Of("UI_INTEL_WAVE_ACTIVE");

        [UiReasonId("intel upgrade already processed this frame")]
        public static readonly ReasonId IntelUpgradeDuplicate = ReasonId.Of("UI_INTEL_UPGRADE_DUPLICATE");

        [UiReasonId("intel purchase type is unknown")]
        public static readonly ReasonId IntelUnknownPurchaseType = ReasonId.Of("UI_INTEL_UNKNOWN_PURCHASE_TYPE");

        [UiReasonId("internet mode duplicate")]
        public static readonly ReasonId InternetModeDuplicate = ReasonId.Of("UI_INTERNET_MODE_DUPLICATE");

        [UiReasonId("internet mode invalid")]
        public static readonly ReasonId InternetModeInvalid = ReasonId.Of("UI_INTERNET_MODE_INVALID");

        [UiReasonId("market freeze frozen")]
        public static readonly ReasonId MarketFreezeFrozen = ReasonId.Of("UI_MARKET_FREEZE_FROZEN");

        [UiReasonId("market insufficient funds")]
        public static readonly ReasonId MarketInsufficientFunds = ReasonId.Of("UI_MARKET_INSUFFICIENT_FUNDS");

        [UiReasonId("market invalid input")]
        public static readonly ReasonId MarketInvalidInput = ReasonId.Of("UI_MARKET_INVALID_INPUT");

        [UiReasonId("market request failed")]
        public static readonly ReasonId MarketRequestFailed = ReasonId.Of("UI_MARKET_REQUEST_FAILED");

        [UiReasonId("market sanctioned")]
        public static readonly ReasonId MarketSanctioned = ReasonId.Of("UI_MARKET_SANCTIONED");

        [UiReasonId("market state unavailable")]
        public static readonly ReasonId MarketStateUnavailable = ReasonId.Of("UI_MARKET_STATE_UNAVAILABLE");

        [UiReasonId("market wallet unavailable")]
        public static readonly ReasonId MarketWalletUnavailable = ReasonId.Of("UI_MARKET_WALLET_UNAVAILABLE");

        [UiReasonId("mob call to arms cooldown")]
        public static readonly ReasonId MobCallToArmsCooldown = ReasonId.Of("UI_MOB_CALL_TO_ARMS_COOLDOWN");

        [UiReasonId("mob conscription cooldown")]
        public static readonly ReasonId MobConscriptionCooldown = ReasonId.Of("UI_MOB_CONSCRIPTION_COOLDOWN");

        [UiReasonId("mob not in crisis")]
        public static readonly ReasonId MobNotInCrisis = ReasonId.Of("UI_MOB_NOT_IN_CRISIS");

        [UiReasonId("mob no casualties")]
        public static readonly ReasonId MobNoCasualties = ReasonId.Of("UI_MOB_NO_CASUALTIES");

        [UiReasonId("call to arms rejected")]
        public static readonly ReasonId MobCallToArmsRejected = ReasonId.Of("UI_MOB_CALL_TO_ARMS_REJECTED");

        [UiReasonId("mobilization request rejected")]
        public static readonly ReasonId MobRejected = ReasonId.Of("UI_MOB_REJECTED");

        [UiReasonId("mob request pending")]
        public static readonly ReasonId MobRequestPending = ReasonId.Of("UI_MOB_REQUEST_PENDING");

        [UiReasonId("mob state unavailable")]
        public static readonly ReasonId MobStateUnavailable = ReasonId.Of("UI_MOB_STATE_UNAVAILABLE");

        [UiReasonId("nickname invalid chars")]
        public static readonly ReasonId NicknameInvalidChars = ReasonId.Of("UI_NICKNAME_INVALID_CHARS");

        [UiReasonId("nickname invalid length")]
        public static readonly ReasonId NicknameInvalidLength = ReasonId.Of("UI_NICKNAME_INVALID_LENGTH");

        [UiReasonId("nickname already taken")]
        public static readonly ReasonId NicknameTaken = ReasonId.Of("UI_NICKNAME_TAKEN");

        [UiReasonId("nickname server unavailable")]
        public static readonly ReasonId NicknameServerUnavailable = ReasonId.Of("UI_NICKNAME_SERVER_UNAVAILABLE");

        [UiReasonId("nickname restricted content")]
        public static readonly ReasonId NicknameRestricted = ReasonId.Of("UI_NICKNAME_RESTRICTED");

        [UiReasonId("nickname no changes left")]
        public static readonly ReasonId NicknameNoChanges = ReasonId.Of("UI_NICKNAME_NO_CHANGES");

        [UiReasonId("nickname rate limited")]
        public static readonly ReasonId NicknameRateLimited = ReasonId.Of("UI_NICKNAME_RATE_LIMITED");

        [UiReasonId("plants shadow insufficient funds")]
        public static readonly ReasonId PlantsShadowInsufficientFunds = ReasonId.Of("UI_PLANTS_SHADOW_INSUFFICIENT_FUNDS");

        [UiReasonId("plant repair already repairing")]
        public static readonly ReasonId PlantRepairAlreadyRepairing = ReasonId.Of("UI_PLANT_REPAIR_ALREADY_REPAIRING");

        [UiReasonId("plant repair config error")]
        public static readonly ReasonId PlantRepairConfigError = ReasonId.Of("UI_PLANT_REPAIR_CONFIG_ERROR");

        [UiReasonId("plant repair not found")]
        public static readonly ReasonId PlantRepairNotFound = ReasonId.Of("UI_PLANT_REPAIR_NOT_FOUND");

        [UiReasonId("plant repair no damage")]
        public static readonly ReasonId PlantRepairNoDamage = ReasonId.Of("UI_PLANT_REPAIR_NO_DAMAGE");

        [UiReasonId("plant repair pending")]
        public static readonly ReasonId PlantRepairPending = ReasonId.Of("UI_PLANT_REPAIR_PENDING");

        [UiReasonId("plant repair refund failed")]
        public static readonly ReasonId PlantRepairRefundFailed = ReasonId.Of("UI_PLANT_REPAIR_REFUND_FAILED");

        [UiReasonId("plant repair system not ready")]
        public static readonly ReasonId PlantRepairSystemNotReady = ReasonId.Of("UI_PLANT_REPAIR_SYSTEM_NOT_READY");

        [UiReasonId("plant repair wave active")]
        public static readonly ReasonId PlantRepairWaveActive = ReasonId.Of("UI_PLANT_REPAIR_WAVE_ACTIVE");

        [UiReasonId("procurement price changed")]
        public static readonly ReasonId ProcurementPriceChanged = ReasonId.Of("UI_PROCUREMENT_PRICE_CHANGED");

        [UiReasonId("relief district cooldown")]
        public static readonly ReasonId ReliefDistrictCooldown = ReasonId.Of("UI_RELIEF_DISTRICT_COOLDOWN");

        [UiReasonId("relief invalid district")]
        public static readonly ReasonId ReliefInvalidDistrict = ReasonId.Of("UI_RELIEF_INVALID_DISTRICT");

        [UiReasonId("relief not enough reserve")]
        public static readonly ReasonId ReliefNotEnoughReserve = ReasonId.Of("UI_RELIEF_NOT_ENOUGH_RESERVE");

        [UiReasonId("relief procurement config error")]
        public static readonly ReasonId ReliefProcurementConfigError = ReasonId.Of("UI_RELIEF_PROCUREMENT_CONFIG_ERROR");

        [UiReasonId("relief procurement insufficient funds")]
        public static readonly ReasonId ReliefProcurementInsufficientFunds = ReasonId.Of("UI_RELIEF_PROCUREMENT_INSUFFICIENT_FUNDS");

        [UiReasonId("relief system not ready")]
        public static readonly ReasonId ReliefSystemNotReady = ReasonId.Of("UI_RELIEF_SYSTEM_NOT_READY");

        [UiReasonId("repair budget pending")]
        public static readonly ReasonId RepairBudgetPending = ReasonId.Of("UI_REPAIR_BUDGET_PENDING");

        [UiReasonId("repair rejected")]
        public static readonly ReasonId RepairRejected = ReasonId.Of("UI_REPAIR_REJECTED");

        [UiReasonId("settings available from crisis")]
        public static readonly ReasonId SettingsAvailableFromCrisis = ReasonId.Of("UI_SETTINGS_AVAILABLE_FROM_CRISIS");

        [UiReasonId("settings locale not available")]
        public static readonly ReasonId SettingsLocaleNotAvailable = ReasonId.Of("UI_SETTINGS_LOCALE_NOT_AVAILABLE");

        [UiReasonId("settings telemetry disabled")]
        public static readonly ReasonId SettingsStatusTelemetryDisabled = ReasonId.Of("UI_SETTINGS_STATUS_TELEMETRY_DISABLED");

        [UiReasonId("settings telemetry needs online")]
        public static readonly ReasonId SettingsTelemetryNeedsOnline = ReasonId.Of("UI_SETTINGS_TELEMETRY_NEEDS_ONLINE");

        [UiReasonId("settings report sending")]
        public static readonly ReasonId SettingsStatusSending = ReasonId.Of("UI_SETTINGS_STATUS_SENDING");

        [UiReasonId("settings report sent")]
        public static readonly ReasonId SettingsStatusReportSent = ReasonId.Of("UI_SETTINGS_STATUS_REPORT_SENT");

        [UiReasonId("settings report unavailable")]
        public static readonly ReasonId SettingsStatusReportUnavailable = ReasonId.Of("UI_SETTINGS_STATUS_REPORT_UNAVAILABLE");

        [UiReasonId("settings report failed")]
        public static readonly ReasonId SettingsStatusReportFailed = ReasonId.Of("UI_SETTINGS_STATUS_REPORT_FAILED");

        [UiReasonId("settings no recent crash dump")]
        public static readonly ReasonId SettingsStatusDumpNone = ReasonId.Of("UI_SETTINGS_STATUS_DUMP_NONE");

        [UiReasonId("settings report copied")]
        public static readonly ReasonId SettingsStatusCopied = ReasonId.Of("UI_SETTINGS_STATUS_COPIED");

        [UiReasonId("settings report saved")]
        public static readonly ReasonId SettingsStatusReportSaved = ReasonId.Of("UI_SETTINGS_STATUS_REPORT_SAVED");

        [UiReasonId("settings errors cleared")]
        public static readonly ReasonId SettingsStatusErrorsCleared = ReasonId.Of("UI_SETTINGS_STATUS_ERRORS_CLEARED");

        [UiReasonId("shadow trade capacity changed")]
        public static readonly ReasonId ShadowTradeImportCapacityChanged = ReasonId.Of("UI_SHADOW_TRADE_CAPACITY_CHANGED");

        [UiReasonId("shadow trade invalid import")]
        public static readonly ReasonId ShadowTradeInvalidImportAmount = ReasonId.Of("UI_SHADOW_TRADE_INVALID_IMPORT");

        [UiReasonId("shadow trade price changed")]
        public static readonly ReasonId ShadowTradeImportPriceChanged = ReasonId.Of("UI_SHADOW_TRADE_PRICE_CHANGED");

        [UiReasonId("shadow trade unknown error")]
        public static readonly ReasonId ShadowTradeUnknownError = ReasonId.Of("UI_SHADOW_TRADE_UNKNOWN_ERROR");

        [UiReasonId("spotter config error")]
        public static readonly ReasonId SpotterConfigError = ReasonId.Of("UI_SPOTTER_CONFIG_ERROR");

        [UiReasonId("spotter duplicate action")]
        public static readonly ReasonId SpotterDuplicateAction = ReasonId.Of("UI_SPOTTER_DUPLICATE_ACTION");

        [UiReasonId("spotter action failed")]
        public static readonly ReasonId SpotterActionFailed = ReasonId.Of("UI_SPOTTER_ACTION_FAILED");

        [UiReasonId("spotter targets already reserved this tick")]
        public static readonly ReasonId SpotterAllReservedThisTick = ReasonId.Of("UI_SPOTTER_ALL_RESERVED_THIS_TICK");

        [UiReasonId("spotter insufficient funds")]
        public static readonly ReasonId SpotterInsufficientFunds = ReasonId.Of("UI_SPOTTER_INSUFFICIENT_FUNDS");

        [UiReasonId("spotter invalid district")]
        public static readonly ReasonId SpotterInvalidDistrict = ReasonId.Of("UI_SPOTTER_INVALID_DISTRICT");

        [UiReasonId("spotter none")]
        public static readonly ReasonId SpotterNone = ReasonId.Of("UI_SPOTTER_NONE");

        [UiReasonId("spotter no active targets")]
        public static readonly ReasonId SpotterNoActiveTargets = ReasonId.Of("UI_SPOTTER_NO_ACTIVE_TARGETS");

        [UiReasonId("spotter precrisis locked")]
        public static readonly ReasonId SpotterPrecrisisLocked = ReasonId.Of("UI_SPOTTER_PRECRISIS_LOCKED");

        [UiReasonId("spotter system unavailable")]
        public static readonly ReasonId SpotterSystemUnavailable = ReasonId.Of("UI_SPOTTER_SYSTEM_UNAVAILABLE");

        [UiReasonId("spotter unknown action")]
        public static readonly ReasonId SpotterUnknownAction = ReasonId.Of("UI_SPOTTER_UNKNOWN_ACTION");

        [UiReasonId("telemarathon rejected")]
        public static readonly ReasonId TelemarathonRejected = ReasonId.Of("UI_TELEMARATHON_REJECTED");

        [UiReasonId("request superseded by a newer one")]
        public static readonly ReasonId RequestSuperseded = ReasonId.Of("UI_REQUEST_SUPERSEDED");

        [UiReasonId("infrastructure domain cannot be disabled")]
        public static readonly ReasonId ToggleInfraLocked = ReasonId.Of("UI_TOGGLE_INFRA_LOCKED");

        [UiReasonId("feature is closed in this build")]
        public static readonly ReasonId ToggleFeatureClosed = ReasonId.Of("UI_TOGGLE_FEATURE_CLOSED");

        [UiReasonId("debug toggle is display-only")]
        public static readonly ReasonId ToggleDisplayOnly = ReasonId.Of("UI_TOGGLE_DISPLAY_ONLY");

        [UiReasonId("donor trust source unavailable")]
        public static readonly ReasonId DonorTrustSourceUnavailable = ReasonId.Of("UI_DONOR_TRUST_SOURCE_UNAVAILABLE");

        [UiReasonId("mobilization social penalty source unavailable")]
        public static readonly ReasonId MobSocialPenaltyUnavailable = ReasonId.Of("UI_MOB_SOCIAL_PENALTY_UNAVAILABLE");

    }
}
