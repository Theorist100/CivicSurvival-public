namespace CivicSurvival.Core.UI
{
    /// <summary>
    /// Single human-edited binding name source for C# ↔ TypeScript communication.
    /// All derivative TS/analyzer artifacts are generated from this file through
    /// Tools/generate-binding-manifest.js and Tools/sync-binding-codegen.js.
    /// Categories: group, valueBinding, trigger, dtoField.
    /// </summary>
    public static partial class B
    {
        public const string Group = "CivicSurvival";

        // ═══════════════════════════════════════════════════════════════════
        // AIR DEFENSE
        // ═══════════════════════════════════════════════════════════════════
        // Heritage AA
        // AA Types
        // Defense Policy
        public const string SetDefensePolicy = "SetDefensePolicy";
        // Patriot drone-intercept toggle (global, default off)
        public const string TogglePatriotDroneIntercept = "TogglePatriotDroneIntercept";
        // AA auto-resupply rule toggle (per-save, default on)
        public const string ToggleAutoResupply = "ToggleAutoResupply";

        // SBU/Spotter
        public const string SbuVisit = "SbuVisit";
        // Evacuation
        public const string Evacuation = "Evacuation";
        // Counter-OSINT
        public const string ToggleCounterOSINT = "ToggleCounterOSINT";
        public const string PurchaseInsider = "PurchaseInsider";

        // ═══════════════════════════════════════════════════════════════════
        // ARENA
        // ═══════════════════════════════════════════════════════════════════
        public const string ArenaLeaderboard = "ArenaLeaderboard";
        public const string ArenaRankTiers = "ArenaRankTiers";
        public const string ArenaWeekly = "ArenaWeekly";
        public const string ArenaYourPosition = "ArenaYourPosition";
        public const string ArenaYourWeeklyPosition = "ArenaYourWeeklyPosition";
        public const string ArenaYourScore = "ArenaYourScore";
        public const string ArenaPendingScore = "ArenaPendingScore";
        public const string ArenaYourRankTier = "ArenaYourRankTier";
        public const string ArenaAllTimeDelta = "ArenaAllTimeDelta";
        public const string ArenaWeeklyDelta = "ArenaWeeklyDelta";
        public const string ArenaShadowLeaderboard = "ArenaShadowLeaderboard";
        public const string ArenaShadowWeekly = "ArenaShadowWeekly";
        public const string ArenaShadowRankTiers = "ArenaShadowRankTiers";
        public const string ArenaShadowYourPosition = "ArenaShadowYourPosition";
        public const string ArenaShadowYourWeeklyPosition = "ArenaShadowYourWeeklyPosition";
        public const string ArenaShadowNet = "ArenaShadowNet";
        public const string ArenaShadowPending = "ArenaShadowPending";
        public const string ArenaShadowRankTier = "ArenaShadowRankTier";
        public const string ArenaProsperityLeaderboard = "ArenaProsperityLeaderboard";
        public const string ArenaProsperityWeekly = "ArenaProsperityWeekly";
        public const string ArenaProsperityRankTiers = "ArenaProsperityRankTiers";
        public const string ArenaProsperityYourPosition = "ArenaProsperityYourPosition";
        public const string ArenaProsperityYourWeeklyPosition = "ArenaProsperityYourWeeklyPosition";
        public const string ArenaProsperityScore = "ArenaProsperityScore";
        public const string ArenaProsperityPending = "ArenaProsperityPending";
        public const string ArenaProsperityRankTier = "ArenaProsperityRankTier";
        public const string RefreshArenaLeaderboard = "RefreshArenaLeaderboard";

        // ═══════════════════════════════════════════════════════════════════
        // BACKUP POWER
        // ═══════════════════════════════════════════════════════════════════
        public const string SetBackupPolicy = "SetBackupPolicy";
        public const string SetBackupPower = "SetBackupPower";
        public const string LaunchDistrictModernization = "LaunchDistrictModernization";

        // ═══════════════════════════════════════════════════════════════════
        // BLACKOUT / DISTRICTS
        // ═══════════════════════════════════════════════════════════════════
        public const string Districts = "Districts";
        public const string ToggleDistrictBlackout = "ToggleDistrictBlackout";
        public const string SetDistrictBlackout = "SetDistrictBlackout";
        public const string ToggleDistrictCategory = "ToggleDistrictCategory";
        public const string SetDistrictSchedule = "SetDistrictSchedule";
        public const string SetCitySchedule = "SetCitySchedule";
        public const string ToggleVIP = "ToggleVIP";
        public const string ToggleVIPBypass = "ToggleVIPBypass";
        public const string ToggleInternet = "ToggleInternet";
        public const string ToggleAutoDispatch = "ToggleAutoDispatch";

        // ═══════════════════════════════════════════════════════════════════
        // COGNITIVE WARFARE
        // ═══════════════════════════════════════════════════════════════════
        public const string CognitiveDistricts = "CognitiveDistricts";
        // Per-stratum opinion lanes (opinion board crowd lanes from CognitiveStatsState)
        public const string CognitiveStrata = "CognitiveStrata";
        // Live discrete PSYOPS attacks (query over PsyOpsAttack entities)
        public const string CognitivePsyOps = "CognitivePsyOps";
        // IPSO (enemy propaganda)
        // Hero
        public const string DeployHero = "DeployHero";
        public const string RecallHero = "RecallHero";
        public const string SetHeroMode = "SetHeroMode";
        // Speaker archetype switch (synchronous pause-safe UI command — HeroDeploymentSystem.TrySetHeroArchetype)
        public const string SetHeroArchetype = "SetHeroArchetype";
        // Broadcast coverage-allocation split across Poor/Middle/Wealthy (synchronous pause-safe UI
        // command — PropagandaCenterSystem.SetBroadcastAllocation; permille args, backend normalizes).
        public const string SetBroadcastAllocation = "SetBroadcastAllocation";

        // ═══════════════════════════════════════════════════════════════════
        // CORRUPTION / COUNTERMEASURES
        // ═══════════════════════════════════════════════════════════════════
        public const string PrepareOperation = "PrepareOperation";
        public const string ExecuteOperation = "ExecuteOperation";
        public const string CancelOperation = "CancelOperation";

        // ═══════════════════════════════════════════════════════════════════
        // DEBUG (dev tools only)
        // ═══════════════════════════════════════════════════════════════════
        public const string DebugSkipPhase = "DebugSkipPhase";
        public const string Debug_SeverityScore = "debug_severityScore";
        public const string Debug_BlackoutPercent = "debug_blackoutPercent";
        public const string Debug_HappinessPenalty = "debug_happinessPenalty";
        public const string Debug_CommercePenalty = "debug_commercePenalty";
        public const string Debug_AffectedDistricts = "debug_affectedDistricts";
        public const string Debug_TotalDistricts = "debug_totalDistricts";
        public const string Debug_PowerBalance = "debug_powerBalance";
        public const string Debug_Production = "debug_production";
        public const string Debug_Consumption = "debug_consumption";
        public const string Debug_BlackoutedMW = "debug_blackoutedMW";
        public const string Debug_ResidentialMW = "debug_residentialMW";
        public const string Debug_CommercialMW = "debug_commercialMW";
        public const string Debug_IndustrialMW = "debug_industrialMW";
        public const string Debug_OfficeMW = "debug_officeMW";
        public const string Debug_CityType = "debug_cityType";
        public const string Debug_BuildingsInBlackout = "debug_buildingsInBlackout";
        public const string Debug_BlackoutDuration = "debug_blackoutDuration";
        public const string Debug_PowerHistory = "debug_powerHistory";
        public const string Debug_SeverityHistory = "debug_severityHistory";
        public const string Debug_CorruptionHistory = "debug_corruptionHistory";
        public const string Debug_CurrentAct = "debug_currentAct";
        public const string Debug_WarDay = "debug_warDay";
        public const string Debug_ShadowBalance = "debug_shadowBalance";
        public const string Debug_ShadowDailyIncome = "debug_shadowDailyIncome";
        public const string Debug_GridWarfareUnlocked = "debug_gridWarfareUnlocked";
        public const string Debug_SetAct = "debug_setAct";

        // DevTools commands (raw debug triggers — mirrored for TS compile-time check)
        public const string DebugSetDusk = "DebugSetDusk";
        public const string DebugSetMidnight = "DebugSetMidnight";
        public const string DebugSpawnWaveDusk = "DebugSpawnWaveDusk";
        public const string DebugSpawn1Drone = "DebugSpawn1Drone";
        public const string DebugSpawn1Ballistic = "DebugSpawn1Ballistic";
        public const string DebugSpawn25Drones = "DebugSpawn25Drones";
        public const string DebugSpawn10Ballistics = "DebugSpawn10Ballistics";
        // DEBUG: one inbound ballistic aimed at the building the player has selected, instead of
        // the wave target selector's own pick — lets a specific structure be put under fire.
        public const string DebugStrikeSelectedBallistic = "DebugStrikeSelectedBallistic";
        public const string DebugTestTracer = "DebugTestTracer";
        public const string DebugExplodeDrone = "DebugExplodeDrone";
        public const string DebugGlitchDrone = "DebugGlitchDrone";
        public const string DebugDestroyAllDrones = "DebugDestroyAllDrones";
        // DEBUG: demolish random live buildings through the production casualty path
        // (BuildingDamageHelper.TryDestroyBuilding → vanilla Destroy event → DestroySystem),
        // NOT EntityManager.DestroyEntity. Each demolish drops a building from the building
        // query → order-version bump → spatial grid/cache rebuild + swap, exercising the
        // wave-crash suspect (swap under a live Burst reader). One-shot vs continuous toggle.
        public const string DebugDemolish1Building = "DebugDemolish1Building";
        public const string DebugDemolishBuildingsToggle = "DebugDemolishBuildingsToggle";
        // DEBUG: mark 500 real vanilla citizens dead through vanilla deathcare —
        // HealthProblem{Dead|RequireTransport} → hearse → DeathcareFacilityAISystem applies Deleted
        // in MainLoop. The casualty kill path; exercises it on demand without waiting for a wave.
        public const string DebugKill500CiviliansNative = "DebugKill500CiviliansNative";
        // DEBUG: credit the shadow wallet with the payload amount (bypasses freeze/act gates
        // via the refund path) so intel purchases / strikes can be tested without farming income.
        public const string DebugGrantShadowCash = "DebugGrantShadowCash";
        public const string DebugForceCrash = "DebugForceCrash";
        // DEBUG: fire the update-restart notice through BOTH real channels (vanilla
        // MessageDialog + React ModUpdatedRestart modal) exactly like a live
        // ModInstanceWatch detection, without touching its once-per-process latch —
        // lets the dual-channel path be eyeballed on demand.
        public const string DebugShowModUpdatedRestart = "DebugShowModUpdatedRestart";
        public const string DebugToggleSystem = "DebugToggleSystem";
        // DEBUG: force-generate a pending procurement offer on the first eligible power
        // plant (skips the daily chance/cooldown) so the maintenance-contract decision
        // modal can be tested on demand, including its pause-safe accept/decline routes.
        public const string DebugSpawnContractOffer = "DebugSpawnContractOffer";
        // DEBUG: force the Countermeasures FSM into its two forced-choice modal phases
        // (investigation choice / police ultimatum) so the pause-safe decision routes
        // can be tested on demand without farming corruption/heat.
        public const string DebugForceInvestigationChoice = "DebugForceInvestigationChoice";
        public const string DebugForcePoliceChoice = "DebugForcePoliceChoice";
        // DEBUG: zero every AA installation's ammo so the pause-safe emergency
        // resupply route can be tested on demand without waiting out a raid.
        public const string DebugDrainAAAmmo = "DebugDrainAAAmmo";
        // Personal AI Chronicle dev trigger: POST /chronicle/personal/generate for
        // the current player; status mirrored back through Debug_PersonalChronicleStatus.
        public const string DebugGeneratePersonalChronicle = "DebugGeneratePersonalChronicle";
        public const string Debug_PersonalChronicleStatus = "debug_personalChronicleStatus";

        // Scenario testing bindings
        public const string Debug_WaveNumber = "debug_waveNumber";
        public const string Debug_WaveInProgress = "debug_waveInProgress";
        public const string Debug_WavePhase = "debug_wavePhase";
        public const string Debug_WavePhaseName = "debug_wavePhaseName";
        public const string Debug_EnemyStance = "debug_enemyStance";
        public const string Debug_EnemyStanceName = "debug_enemyStanceName";
        public const string Debug_EnemyPressure = "debug_enemyPressure";
        public const string Debug_StancePhase = "debug_stancePhase";
        public const string Debug_StancePhaseName = "debug_stancePhaseName";
        public const string Debug_GridCollapsed = "debug_gridCollapsed";
        public const string Debug_GridStressHours = "debug_gridStressHours";
        public const string Debug_GridRecoveryHours = "debug_gridRecoveryHours";
        public const string Debug_GridZone = "debug_gridZone";
        public const string Debug_GridZoneName = "debug_gridZoneName";
        public const string Debug_ExodusRatePercentPerDay = "debug_exodusRatePercentPerDay";
        public const string Debug_TotalFled = "debug_totalFled";
        public const string Debug_ExodusActive = "debug_exodusActive";
        public const string Debug_ShockLevel = "debug_shockLevel";
        public const string Debug_ShockTier = "debug_shockTier";
        public const string Debug_ShockTierName = "debug_shockTierName";
        public const string Debug_InfectionRate = "debug_infectionRate";
        public const string Debug_CityIntegrity = "debug_cityIntegrity";
        public const string Debug_MediaTrust = "debug_mediaTrust";
        public const string Debug_TelemarathonActive = "debug_telemarathonActive";
        public const string Debug_TrustLevel = "debug_trustLevel";
        public const string Debug_CorruptionHeat = "debug_corruptionHeat";
        public const string Debug_CorruptionScore = "debugCorruptionScore";
        public const string Debug_MoraleFactor = "debug_moraleFactor";
        public const string Debug_AaAmmo = "debug_aaAmmo";
        public const string Debug_ABTestStatus = "debug_abTestStatus";
        public const string Debug_ABTestProgress = "debug_abTestProgress";
        public const string DebugToggleStates = "DebugToggleStates";
        public const string DebugForceWave = "DebugForceWave";
        public const string DebugTestExplosion = "DebugTestExplosion";
        public const string DebugForceGridCollapse = "DebugForceGridCollapse";
        public const string DebugResetGridStress = "DebugResetGridStress";
        public const string DebugSetStress = "DebugSetStress";
        public const string DebugSetShock = "DebugSetShock";
        public const string DebugSetCorruption = "DebugSetCorruption";
        public const string DebugSetCityIntegrity = "DebugSetCityIntegrity";
        public const string DebugSetEnemyPressure = "DebugSetEnemyPressure";
        public const string DebugSetTrust = "DebugSetTrust";
        public const string DebugSetMoraleFactor = "DebugSetMoraleFactor";
        public const string DebugRunPreset = "DebugRunPreset";
        public const string DebugForceDayChange = "DebugForceDayChange";
        public const string DebugForceDraftOffer = "DebugForceDraftOffer";
        // PSYOPS raids — the wave cadence is the only in-play launcher, so the panel drives the same
        // composition directly. Payload: -1 = full raid, 0/1/2 = one Propaganda / Rumor / FakeVideo contact.
        public const string DebugLaunchPsyRaid = "DebugLaunchPsyRaid";
        public const string DebugLandPsyRaid = "DebugLandPsyRaid";
        // Narrative quests — chance/cooldown/once-per-city gates make them unreachable on demand;
        // payload = QuestId (1 = KotletaLights, 2 = RightVoice). Force-arms, lifecycle stays real.
        public const string DebugTriggerQuest = "DebugTriggerQuest";
        // Crisis sweep — in-game balance/diagnostics tool (panel-launched, PostSimulation consumer).
        public const string TriggerCrisisSweep = "TriggerCrisisSweep";
        public const string CrisisSweepState = "CrisisSweepState";

        // ═══════════════════════════════════════════════════════════════════
        // DIPLOMACY / DONOR
        // ═══════════════════════════════════════════════════════════════════
        public const string OpenDonorConference = "OpenDonorConference";
        public const string CloseDonorConference = "CloseDonorConference";
        public const string SelectDonorFunds = "SelectDonorFunds";
        public const string SelectDonorPower = "SelectDonorPower";
        public const string SelectDonorDefense = "SelectDonorDefense";

        // Aid distribution
        public const string DistributeAid = "DistributeAid";
        // ═══════════════════════════════════════════════════════════════════
        // ECONOMICS / FINANCE
        // ═══════════════════════════════════════════════════════════════════
        public const string TaxMultiplier = "TaxMultiplier";
        public const string LoansAvailable = "LoansAvailable";
        public const string CrisisDayNumber = "CrisisDayNumber";
        public const string SetEmergencyFundWithdraw = "SetEmergencyFundWithdraw";

        // ═══════════════════════════════════════════════════════════════════
        // GLOBAL NEWS
        // ═══════════════════════════════════════════════════════════════════
        public const string NewsFeed = "NewsFeed";
        public const string ToggleGlobalConnection = "ToggleGlobalConnection";

        // ═══════════════════════════════════════════════════════════════════
        // GRID WARFARE / POWER
        // ═══════════════════════════════════════════════════════════════════
        public const string SetExportPercent = "SetExportPercent";
        public const string RepairPlant = "RepairPlant";
        public const string RepairCivilian = "RepairCivilian";
        public const string DismissGridCollapse = "DismissGridCollapse";
        public const string DismissGridCritical = "DismissGridCritical";
        // Counter-attack arsenal — shadow-import purchase (paid via Shadow Cash, sanctions markup).
        // Payload encodes (kind, count); served by ShadowImportUISystem.
        public const string PurchaseCounterAttackArsenal = "PurchaseCounterAttackArsenal";
        // War Room STRIKE view: the player's per-axis preferred mirror-city target (Phase E).
        // Payload (axisRaw, targetId): axisRaw = AttackCategory (0 physical / 1 digital / 2 social),
        // targetId = EnemyTarget.Id, or 0xFFFF (NoTargetId) to clear back to auto-select. Sync,
        // pause-safe — stores a NonSerialized UI preference in PlayerAttackSystem (not persisted).
        public const string SetStrikeTarget = "SetStrikeTarget";

        // ═══════════════════════════════════════════════════════════════════
        // HELP STATE
        // ═══════════════════════════════════════════════════════════════════
        public const string GridHelpSeen = "GridHelpSeen";
        public const string ShadowHelpSeen = "ShadowHelpSeen";
        public const string MarkGridHelpSeen = "MarkGridHelpSeen";
        public const string MarkShadowHelpSeen = "MarkShadowHelpSeen";

        // ═══════════════════════════════════════════════════════════════════
        // INTEL
        // ═══════════════════════════════════════════════════════════════════
        public const string UpgradeIntel = "UpgradeIntel";
        // ═══════════════════════════════════════════════════════════════════
        // INTRO / SCENARIO
        // ═══════════════════════════════════════════════════════════════════
        public const string IntroPhase = "IntroPhase";
        public const string IntroHudVisible = "IntroHudVisible";
        public const string ActiveModalState = "ActiveModalState";
        public const string OnAcceptReality = "OnAcceptReality";
        public const string ScenarioType = "ScenarioType";
        public const string CurrentAct = "CurrentAct";
        public const string TotalRefugees = "TotalRefugees";
        public const string PopulationPercent = "PopulationPercent";
        /// <summary>Whether <see cref="PopulationPercent"/> was computed from a live reading the
        /// city actually answered. False = the panel must draw a dash; the percent then carries
        /// the last good value and means nothing on its own.</summary>
        public const string PopulationMeasured = "PopulationMeasured";
        public const string WavesDefended = "WavesDefended";
        public const string MissilesIntercepted = "MissilesIntercepted";
        public const string BlackoutRecoveries = "BlackoutRecoveries";
        public const string BuildingsDamaged = "BuildingsDamaged";
        public const string DefeatCause = "DefeatCause";
        public const string DaysSurvived = "DaysSurvived";
        public const string DismissWarFatigue = "DismissWarFatigue";
        public const string DismissDefeat = "DismissDefeat";
        // ═══════════════════════════════════════════════════════════════════
        // LOCALIZATION
        // ═══════════════════════════════════════════════════════════════════
        public const string SetLanguage = "SetLanguage";
        // ═══════════════════════════════════════════════════════════════════
        // MAINTENANCE / CONTRACTS
        // ═══════════════════════════════════════════════════════════════════
        public const string AcceptOfficialContract = "AcceptOfficialContract";
        public const string AcceptShadyContract = "AcceptShadyContract";
        public const string MakeInvestigationChoice = "MakeInvestigationChoice";
        public const string MakePoliceChoice = "MakePoliceChoice";
        public const string DismissArrested = "DismissArrested";
        // Shared by the QuestCompleted and QuestFailed modals: one button, one handler, the
        // coordinator knows which of the two currently holds the slot.
        public const string DismissQuestResult = "DismissQuestResult";
        public const string DismissModLoadFailure = "DismissModLoadFailure";
        public const string ModLoadFailureAction = "ModLoadFailureAction";
        public const string DismissModUpdatedRestart = "DismissModUpdatedRestart";
        // Pre-publish update announce (server /config/announce): JSON {"Active":bool,"PublishAtUnix":long}
        public const string UpdateAnnounceState = "UpdateAnnounceState";
        public const string DismissModUpdatePending = "DismissModUpdatePending";
        // Show/dismiss telemetry ack from the update-safety modals (pending/restart × shown/dismissed)
        public const string ModUpdateModalAction = "ModUpdateModalAction";
        // ═══════════════════════════════════════════════════════════════════
        // MOBILIZATION
        // ═══════════════════════════════════════════════════════════════════
        public const string CallToArms = "CallToArms";
        public const string ToggleConscription = "ToggleConscription";
        public const string DeclineProcurement = "DeclineProcurement";
        public const string SetProcurementLevel = "SetProcurementLevel";
        // ═══════════════════════════════════════════════════════════════════
        // RADAR / THREATS
        // ═══════════════════════════════════════════════════════════════════
        public const string FocusNextThreat = "FocusNextThreat";
        public const string FocusRadarThreat = "FocusRadarThreat";
        public const string FocusRadarDefense = "FocusRadarDefense";
        public const string FocusThreat = "FocusThreat";
        public const string DismissDebriefing = "DismissDebriefing";
        // Info-war (PSYOPS) after-action report — kinetic debriefing's cognitive sibling.
        public const string DismissPsyDebriefing = "DismissPsyDebriefing";
        // Inbound-raid alert: acknowledged (or the player jumps to PSYOPS from it).
        public const string DismissPsyInbound = "DismissPsyInbound";
        // ═══════════════════════════════════════════════════════════════════
        // REFUGEES
        // ═══════════════════════════════════════════════════════════════════
        public const string RefugeeHouseholdCount = "RefugeeHouseholdCount";
        public const string RefugeesReceived = "RefugeesReceived";
        public const string RefugeeHoursRemaining = "RefugeeHoursRemaining";
        public const string DismissRefugeeModal = "DismissRefugeeModal";
        public const string DismissCollapseModal = "DismissCollapseModal";
        // ═══════════════════════════════════════════════════════════════════
        // REPUTATION / TRUST
        // ═══════════════════════════════════════════════════════════════════
        // ═══════════════════════════════════════════════════════════════════
        // SETTINGS
        // ═══════════════════════════════════════════════════════════════════
        public const string SetUITheme = "SetUITheme";
        public const string SetDifficultyPreset = "SetDifficultyPreset";
        public const string SetConstructionDelay = "SetConstructionDelay";
        public const string SetRandomDisasters = "SetRandomDisasters";
        public const string SetWinterMultiplier = "SetWinterMultiplier";
        public const string SetNarrativeMode = "SetNarrativeMode";
        public const string SetNeighborEnvy = "SetNeighborEnvy";
        public const string SetProtectCriticalInfra = "SetProtectCriticalInfra";
        public const string SetTelemarathonActive = "SetTelemarathonActive";
        public const string SetTelemetryEnabled = "SetTelemetryEnabled";
        // Effective diagnostics = Online && diagnostics-opt-in. Single C#-side source so
        // the UI crash reporter (Sentry) and the server agree on one gate (no UI-side
        // recombination of two flags). Sentry reads THIS, not the raw TelemetryEnabled.
        public const string EffectiveDiagnostics = "EffectiveDiagnostics";
        public const string SetMuteCivicAudio = "SetMuteCivicAudio";
        public const string SetMuteDroneAudio = "SetMuteDroneAudio";
        public const string SetMuteAlertAudio = "SetMuteAlertAudio";
        public const string SetMuteCombatAudio = "SetMuteCombatAudio";
        public const string SendReport = "SendReport";
        public const string SendLoadFailureReport = "SendLoadFailureReport";
        public const string CopyReport = "CopyReport";
        public const string SendModLog = "SendModLog";
        public const string SendCrashDumps = "SendCrashDumps";
        public const string ClearErrors = "ClearErrors";
        public const string OpenExternalLink = "OpenExternalLink";
        public const string SetPlayerNickname = "SetPlayerNickname";
        public const string DebugToggleExodus = "DebugToggleExodus";

        // ═══════════════════════════════════════════════════════════════════
        // SHADOW ECONOMY
        // ═══════════════════════════════════════════════════════════════════
        public const string SetShadowImportMW = "SetShadowImportMW";
        public const string SetFuelSiphonPercent = "SetFuelSiphonPercent";
        public const string SetDraftDeferralPercent = "SetDraftDeferralPercent";
        public const string SetConstructionKickbackPercent = "SetConstructionKickbackPercent";
        // ═══════════════════════════════════════════════════════════════════
        // SHOCK / SANCTIONS
        // ═══════════════════════════════════════════════════════════════════
        public const string ShockActActive = "ShockActActive";
        // ═══════════════════════════════════════════════════════════════════
        // SOCIAL FEED
        // ═══════════════════════════════════════════════════════════════════
        public const string SocialFeed = "SocialFeed";
        public const string SetInternetMode = "SetInternetMode";

        // ═══════════════════════════════════════════════════════════════════
        // STANCE (Enemy)
        // ═══════════════════════════════════════════════════════════════════
        // ═══════════════════════════════════════════════════════════════════
        // TOAST
        // ═══════════════════════════════════════════════════════════════════
        public const string TriggerResult = "TriggerResult";
        public const string ToastCount = "ToastCount";
        public const string ToastsJson = "ToastsJson";
        public const string AcceptToast = "AcceptToast";
        public const string DismissToast = "DismissToast";
        public const string RejectToast = "RejectToast";
        // ═══════════════════════════════════════════════════════════════════
        // TUTORIAL
        // ═══════════════════════════════════════════════════════════════════
        public const string DismissFirstStrike = "DismissFirstStrike";
        public const string DismissExodusWarning = "DismissExodusWarning";
        public const string OnOpenGridTab = "OnOpenGridTab";
        public const string OnOpenShadowTab = "OnOpenShadowTab";
        // Air-defense funnel analytics: WAR tab open, then RADAR / DEFENSE sub-view.
        public const string OnOpenWarTab = "OnOpenWarTab";
        public const string OnOpenRadarView = "OnOpenRadarView";
        public const string OnOpenDefenseBuild = "OnOpenDefenseBuild";

        // ═══════════════════════════════════════════════════════════════════
        // MILESTONE TUTORIALS
        // ═══════════════════════════════════════════════════════════════════
        public const string DismissWarBegins = "DismissWarBegins";
        public const string DismissFirstDonorAid = "DismissFirstDonorAid";
        public const string DismissFirstSuccessfulDefense = "DismissFirstSuccessfulDefense";
        public const string DismissGeneratorEra = "DismissGeneratorEra";
        public const string DismissSpotterAlert = "DismissSpotterAlert";
        public const string DismissCorruptionOffer = "DismissCorruptionOffer";
        public const string DismissGhostTown = "DismissGhostTown";
        public const string DismissWhoStaysBehind = "DismissWhoStaysBehind";

        // ═══════════════════════════════════════════════════════════════════
        // UI GENERAL
        // ═══════════════════════════════════════════════════════════════════
        public const string SetCrashDumpScan = "SetCrashDumpScan";
        public const string JsLog = "JsLog";
        public const string UiProfileReport = "UiProfileReport";
        public const string PlaceAABuilding = "PlaceAABuilding";
        // Vanilla take-in-hand convention: a second click on the active AA tile puts the tool away.
        public const string CancelAAPlacement = "CancelAAPlacement";
        // Propaganda Center: placement (generic pipeline) + capacity upgrade (sync host-direct).
        public const string PlacePropagandaCenter = "PlacePropagandaCenter";
        public const string UpgradePropagandaCenter = "UpgradePropagandaCenter";
        // Launcher emplacements: placement through the generic pipeline (GridWarfare-owned,
        // shadow-paid), one trigger per munition kind.
        public const string PlaceDroneLauncher = "PlaceDroneLauncher";
        public const string PlaceBallisticLauncher = "PlaceBallisticLauncher";
        // Dev-only outbound launch: fires one drone / one ballistic straight from a built launcher,
        // bypassing the War Room operation flow so the flight can be checked in isolation. Consumed
        // by the debug panel, which the production bundle strips entirely.
        // DEBUG: run a REAL outbound operation of that axis through the ordinary Prepare/Execute
        // path — same cost, munition, damage roll, target pick and pending-launch ledger as the War
        // Room buttons. Replaces the old flight-probe launches, which flew a zero-damage projectile
        // and resolved nothing on arrival.
        public const string DebugStrikeDrone = "DebugStrikeDrone";
        public const string DebugStrikeCyber = "DebugStrikeCyber";
        public const string DebugStrikePsyops = "DebugStrikePsyops";
        // ═══════════════════════════════════════════════════════════════════
        // DOMAIN STATE JSON BINDINGS (one JSON string per domain)
        // ═══════════════════════════════════════════════════════════════════
        public const string MobilizationState = "MobilizationState";
        public const string AttentionState = "AttentionState";
        public const string FinanceState = "FinanceState";
        public const string ReputationState = "ReputationState";
        public const string MaintenanceState = "MaintenanceState";
        public const string ExportState = "ExportState";
        public const string ImportState = "ImportState";
        public const string SchemesState = "SchemesState";
        public const string BuckwheatState = "BuckwheatState";
        public const string CountermeasuresState = "CountermeasuresState";
        public const string PowerGridState = "PowerGridState";
        public const string BackupPowerState = "BackupPowerState";
        public const string NewsState = "NewsState";
        public const string DonorState = "DonorState";
        public const string SettingsState = "SettingsState";
        public const string SettingsLocalizationState = "SettingsLocalizationState";
        public const string ThreatState = "ThreatState";
        public const string AirDefenseState = "AirDefenseState";
        public const string IntelState = "IntelState";
        public const string SpotterState = "SpotterState";
        public const string CognitiveState = "CognitiveState";
        public const string GridWarfareState = "GridWarfareState";
        public const string QuestState = "QuestState";

        /// <summary>
        /// Static city coastline contour: a JSON array of world-space X/Z polylines
        /// (<c>[[x,z,x,z,...],...]</c>). Published once per loaded city, NOT in the
        /// per-frame ThreatState payload. The radar normalizes it with the same
        /// normalizePosition() as threats/targets so the outline aligns with markers.
        /// </summary>
        public const string MapContour = "MapContour";

        /// <summary>
        /// Mirror-city STRIKE-view snapshot: a JSON object with the generated enemy city already
        /// quantized to the player's effective intel level (C#-side — exact numbers never leave the
        /// sim). Shape: <c>{header:{variantId,mapId,genVersion,intelLevel}, signals:[], targets:[], aa:[]}</c>.
        /// Published (like <see cref="MapContour"/>) as its own raw-string binding, rebuilt only when
        /// the quantized content actually changes — NOT part of the per-frame GridWarfareState payload.
        /// </summary>
        public const string MirrorCity = "MirrorCity";

        // ═══════════════════════════════════════════════════════════════════
        // FEATURE GATES
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// JSON object with the beta wave plan:
        /// {"current": int, "waves": {featureId: int}}.
        /// "current" is the active beta wave; "waves" maps feature ids to the
        /// wave that unlocks them. A feature is considered locked in the UI iff
        /// waves[featureId] > current. Unlisted features default to wave 1.
        /// </summary>
        public const string FeatureWaveManifest = "FeatureWaveManifest";

        // ═══════════════════════════════════════════════════════════════════
        // POST-UNIFICATION CATCHUP — DTO fields added by ui-dto.contract.yaml
        // ═══════════════════════════════════════════════════════════════════
    }
}
