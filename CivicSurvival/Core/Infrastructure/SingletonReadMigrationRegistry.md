# Singleton Read Migration Registry

Inventory date: 2026-05-17
Migration completed: 2026-05-17

Scope: working code under `CivicSurvival/**/*.cs`. Test fixtures, docs, and planning notes excluded.

## Outcome

The fail-loud `EntityQueryExtensions.GetSingletonOrDefault(feature, fallback)`
accessor is **deleted**. It was a non-vanilla construct: vanilla CS2 has only

- hard input: `RequireForUpdate<T>()` + `GetSingleton<T>()`
- soft input: `TryGetSingleton<T>` (or `!IsEmpty`) + neutral fallback

`GetSingletonOrDefault` mixed config-gated fallback with a fail-fast "feature
available ⇒ throw" assertion, turning transient singleton absence into a
load-aborting `InvalidOperationException` (the `[CRITICAL]` UI crash class).

All 43 live call sites (46 inventoried minus 3 ThreatUISystem rows already
migrated before this pass) are now on one of the two vanilla contracts.

Deleted in this pass:

- `CivicSurvival/Core/Infrastructure/EntityQueryExtensions.cs` (zero callers)
- `CivicSurvival.Tests/Core/Infrastructure/FeatureAwareSingletonReadTests.cs`
  (policed the deleted construct; the invariant is now enforced structurally —
  the method no longer exists — plus `BanGetSingletonAnalyzer`/CIVIC017).

`BanGetSingletonAnalyzer` (CIVIC017) updated: `GetSingleton<T>()` is exempt when
the containing system declares a matching `RequireForUpdate<T>()` (syntax-only
match). `SetSingleton` and unguarded `GetSingleton` stay banned.

## Final dispositions

- `require_for_update` — hard read: `RequireForUpdate<T>()` in `OnCreate` +
  `GetSingleton<T>()`. Producer's owner-side `EnsureExists` confirmed.
- `config_gated_try_get` — soft read: `TryGetSingleton(out v) ? v : Default`.
  No `RequireForUpdate`; producer may be closed by `FeatureGates`/wave/dep-skip.
- `config_gated_try_get (deviation)` — registry originally said
  `convert_to_require_for_update`, but reading the code proved a blanket
  `RequireForUpdate` is unsafe; resolved as soft read. Reason per row.
- `config_gated_interim` — was `needs_owner_decision`. Resolved as soft read to
  remove the crash now, without asserting a false hard dependency (reversible,
  documented config-gated contract). **Owner decision still pending** — may be
  promoted to `require_for_update` later. Listed in "Pending owner decisions".

| # | File:line (pre-pass) | Singleton | Final disposition |
|---:|---|---|---|
| 1 | AirDefenseUISystem.cs:122 | SpotterPenaltyState | config_gated_try_get |
| 2 | AirDefenseUISystem.cs:149 | MobilizationStateSingleton | config_gated_try_get (reverted to show-defaults) |
| 3 | TelemetryService.cs:674 | CurrentActSingleton | config_gated_try_get |
| 4 | TelemetryPulse.cs:191 | CurrentActSingleton | config_gated_try_get |
| 5 | TelemetryPulse.cs:196 | WaveStateSingleton | config_gated_try_get |
| 6 | TelemetryPulse.cs:203 | ThreatStatsSingleton | config_gated_try_get |
| 7 | TelemetryPulse.cs:279 | PowerGridSingleton | config_gated_try_get |
| 8 | WaveExecutor.cs:447 | InterceptStatsSingleton | config_gated_try_get (deviation) |
| 9 | WaveExecutor.cs:562 | ThreatStatsSingleton | config_gated_try_get (deviation) |
| 10 | ExodusSystem.cs:296 | ScenarioSingleton | config_gated_try_get (deviation) |
| 11 | ExodusSystem.cs:300 | CurrentActSingleton | config_gated_try_get (deviation) |
| 12 | ThreatUISystem.cs:183 | ThreatStatsSingleton | config_gated_try_get (pre-pass) |
| 13 | ThreatUISystem.cs:186 | InterceptStatsSingleton | config_gated_try_get (pre-pass) |
| 14 | ThreatUISystem.cs:188 | ThreatOutcomeStatsSingleton | config_gated_try_get (pre-pass) |
| 15 | AirDefenseOrchestrator.cs:871 | SpotterPenaltyState | config_gated_try_get |
| 16 | AirDefenseOrchestrator.cs:874 | TelemarathonRuntimeState | config_gated_try_get |
| 17 | ThreatAudioOrchestrator.cs:203 | WaveStateSingleton | config_gated_try_get |
| 18 | BlackoutSystem.cs:340 | BackupPowerStateSingleton | config_gated_interim |
| 19 | FinanceUISystem.cs:57 | ShadowWalletSingleton | config_gated_try_get |
| 20 | CrisisEconomicsSystem.cs:152 | DonorSanctionsSingleton | config_gated_try_get |
| 21 | CrisisEconomicsSystem.cs:224 | CognitiveState | config_gated_try_get |
| 22 | SpotterUISystem.cs:59 | SpotterStatsSingleton | config_gated_try_get (reverted to show-defaults) |
| 23 | SpotterUISystem.cs:67 | SpotterPenaltyState | config_gated_try_get (reverted to show-defaults) |
| 24 | SpotterUISystem.cs:79 | CurrentActSingleton | removed (act-policy refactor — see top section) |
| 25 | SpotterUISystem.cs:82 | SpotterStatsSingleton | config_gated_try_get (reverted to show-defaults) |
| 26 | PlantWearSimulation.cs:673 | WaveStateSingleton | config_gated_try_get |
| 27 | SpotterCommandIngressSystem.cs:380 | CurrentActSingleton | require_for_update |
| 28 | SpotterAggregateSystem.cs:733 | TelemarathonRuntimeState | config_gated_try_get |
| 29 | MentalHealthResolverSystem.cs:471 | BackupPowerStateSingleton | config_gated_interim |
| 30 | MobilizationSystem.cs:273 | CorruptionSingleton | config_gated_try_get |
| 31 | PowerGridUISystem.cs:173 | PowerGridSingleton | config_gated_try_get (reverted to show-defaults) |
| 32 | PowerGridUISystem.cs:215 | PowerGridSingleton | config_gated_try_get (reverted to show-defaults) |
| 33 | PowerGridUISystem.cs:261 | WaveStateSingleton | config_gated_try_get |
| 34 | PowerGridUISystem.cs:432 | WaveStateSingleton | config_gated_try_get |
| 35 | PowerGridUISystem.cs:609 | ShadowWalletSingleton | config_gated_try_get |
| 36 | BackupPowerRuntimeSystem.cs:432 | BackupPowerStateSingleton | config_gated_try_get (deviation) |
| 37 | BackupPowerRuntimeSystem.cs:437 | EconomySingleton | config_gated_interim |
| 38 | IntelUISystem.cs:80 | IntelStateSingleton | config_gated_try_get (reverted to show-defaults) |
| 39 | IntelUISystem.cs:179 | CurrentActSingleton | removed (act-policy refactor — see top section) |
| 40 | IntelUISystem.cs:183 | ShadowWalletSingleton | config_gated_try_get |
| 41 | PowerGridDataSystem.cs:242 | ShadowExportState | config_gated_try_get |
| 42 | IntelPurchaseSystem.cs:194 | CurrentActSingleton | require_for_update |
| 43 | IntelPurchaseSystem.cs:238 | CurrentActSingleton | require_for_update |
| 44 | CivicUIPanelSystem.cs:223 | WaveStateSingleton | config_gated_interim |
| 45 | PostLoadValidationSystem.cs:237 | CurrentActSingleton | config_gated_try_get |
| 46 | SaveMetadataSystem.cs:78 | CurrentActSingleton | require_for_update |

## Deviations (registry said convert_to_require_for_update; code said otherwise)

- **#8, #9 WaveExecutor → InterceptStatsSingleton / ThreatStatsSingleton.**
  WaveExecutor is the **producer** of `WaveStateSingleton` (`EnsureUnique` in
  `OnCreate`), performs post-load `WaveStateSingleton` recovery in `OnUpdate`,
  and ticks the phase timer every frame. A blanket `RequireForUpdate` on
  consumer singletons would freeze wave progression and break post-load
  recovery. The hard *feature* dependency Waves→ThreatsAirDefense is already
  enforced via `FeatureRegistry.Instance.Require<InterceptProcessingSystem>()`
  in `OnStartRunning`. Resolved as soft reads.
- **#36 BackupPowerRuntimeSystem → BackupPowerStateSingleton.**
  This system **is** the producer/owner/writer (`EnsureExists` in `OnCreate`
  and the serialization partial; `ReadWrite` query; mutates at :211/:242/:353).
  `RequireForUpdate` on a self-owned singleton is a self-block anti-pattern and
  would also gate unrelated job scheduling. Resolved as soft read, consistent
  with the system's own `TryGetSingletonEntity` defensive pattern.
- **#10, #11 ExodusSystem → ScenarioSingleton / CurrentActSingleton.**
  Scenario is verified foundational always-on (see "Scenario coupling resolved"),
  so these are NOT config-gated. But ExodusSystem re-creates its OWN singleton in
  `OnUpdateImpl` after save/load (`:216`, CIVIC303) — gating its tick on an
  external singleton is the same WaveExecutor-class self-recovery-freeze risk.
  Resolved as soft read (`ScenarioSingleton.Default.ExodusRateOverrideFraction=0`
  = "normal calc"; `CurrentActSingleton.Default=PreWar`), values real within ~0
  frames anyway.

## Act-policy separation (FINAL — supersedes all CurrentAct rows below)

Date: 2026-05-17. The CurrentAct reads in Intel/Spotter were not a singleton-read
problem but a misplaced-policy problem. Final architecture:

- Eligibility predicates (`SpotterEligibility.CanPerformSBUVisit/CanPerformEvacuation/
  CanToggleCounterOSINT`, `IntelEligibility.CanBuyInsider/CanUpgradeIntel`) are now
  **local-facts-only** — the `Act currentAct` parameter and `if (currentAct < Act.X)`
  check were removed. `[DtoEligibility]` contract unchanged (binds by method name);
  `check-dto-eligibility-coverage` still "30 gates synced".
- `IntelUISystem`/`SpotterUISystem`: all `CurrentActSingleton` reads, `m_CurrentActQuery`,
  `GetCurrentAct()` removed. Panels publish facts + local-facts `Can*`/reason only.
- Click gate moved to the gate layer: triggers `Triggers.Add` → `AddScenarioTrigger(
  name, FeatureIds.X, Act.Crisis, …)` for all 5 (SBU/Evac/CounterOSINT/Insider/Upgrade).
  Redundant manual `IsFeatureOpen("Intel")` checks + dead helper removed.
- Backend authoritative guard: explicit `if (currentAct < Act.Crisis) { failReason =
  ReasonIds.ActLockedFor(Act.Crisis); return false; }` added before the local-facts
  predicate in `IntelPurchaseSystem` (Insider+Upgrade) and `SpotterCommandIngressSystem`
  (SBU/Evac/CounterOSINT). They read the real act via group-1 `RequireForUpdate<
  CurrentActSingleton>`+`GetSingleton` (no fabrication).
- Frontend overlay: `currentAct < Act.Crisis` disables all 5 buttons
  (`IntelContent.tsx` OpSecPanel ×3; `GridWarfarePanel.tsx` IntelPreviewSlot Upgrade;
  Insider already gated via `IntelInsiderBlock` return-null pre-Crisis).
- Decision: Intel **upgrade** gate moved `Act.Routine` → `Act.Crisis` (all 5 uniform).
  Stale 2nd source `IntelStateSystem.CanUpgradeIntel` (+ dead `m_ScenarioState`/
  `m_HasScenarioState` cache) **deleted** (zero usages).

Net: `Can*` DTO = "possible by local resources/state"; `currentAct` = UI overlay;
`AddScenarioTrigger` = click gate; backend = authoritative guard. No panel owns
act-policy. Build verified: C# `--no-incremental` 0 errors / 0 CIVIC017, eligibility
30 gates synced, UI build + tsc + eslint + l10n green.

## Scenario coupling resolved (no longer "owner decision")

VERIFIED: `ScenarioDomain` is `IFeatureModule, IContentFeatureModule` — **not**
`IGatedFeatureModule`. `ScenarioStateMachine` is registered unconditionally and
creates `CurrentActSingleton`/`ScenarioSingleton` in `OnCreate`
(`[SingletonOwner]`, sole writer). They are **foundational always-on**, not
config-gated. So the prior "soft because producer may be closed" was an
unverified hedge — disproven.

- Pure-consumer / request-driven, no self-block, not multi-purpose →
  **promoted to `require_for_update`**: #24 SpotterUISystem, #39 IntelUISystem,
  #27 SpotterCommandIngressSystem, #42/#43 IntelPurchaseSystem,
  #46 SaveMetadataSystem.
- #10/#11 ExodusSystem → soft **deviation** (own-singleton post-load recovery in
  `OnUpdateImpl`; see Deviations), not because Scenario is gated.

## Remaining genuinely-soft (concrete reason, not "pending decision")

Only 4 sites, each with a real reason — none is a Scenario/always-on case:

- #44 CivicUIPanelSystem → WaveStateSingleton. Waves is legitimately closeable
  (peaceful build, no war). The CurrentAct half is already hard-guarded
  (`TryGetSingleton` → `return false`, `NO_MIGRATE`); only the WaveState half is
  soft-`Calm`, consistent with every other WaveState soft read.
- #37 BackupPowerRuntimeSystem → EconomySingleton. Multi-purpose consumer
  (schedules jobs, owns its own singleton) — blanket `RequireForUpdate` is the
  WaveExecutor anti-pattern. Soft + neutral economy baseline.
- #18 BlackoutSystem, #29 MentalHealthResolverSystem → BackupPowerStateSingleton.
  Core simulation systems that must run regardless of backup policy; soft +
  conservative `Reserve` default. (PowerBackup feature-gating not separately
  verified, but the multi-purpose-consumer argument makes soft correct
  independent of that.)

## Summary

Final state (after the act-policy refactor + show-defaults panel reverts —
the top "Act-policy separation (FINAL)" section is authoritative for the
Intel/Spotter rows; this table is corrected to match):

| Final disposition | Count | Which |
|---|---:|---|
| `require_for_update` | 4 | #27/#42/#43/#46 — backend CurrentAct group-1 + explicit act guard |
| `removed` | 2 | #24/#39 — CurrentAct taken out of panels (act → trigger/backend/frontend) |
| `config_gated_interim` | 4 | #18/#29/#37/#44 — genuinely soft, concrete reason, untouched by the act-policy refactor |
| `config_gated_try_get` | 36 | all else: reverted show-defaults UI panels, Phase-3 cross-feature, deviations, 3 pre-pass ThreatUISystem |
| Total | 46 | |

Deviations (code proved a blanket RequireForUpdate unsafe): WaveExecutor #8/#9
(WaveState producer + post-load recovery), BackupPowerRuntimeSystem #36
(self-owned), ExodusSystem #10/#11 (own-singleton post-load recovery). Resolved
as soft for a concrete WaveExecutor-class reason, not a hedge.

Refactor polish: dead `ReasonIds.GwIntelAvailableRoutine` /
`ReasonIds.InsiderPrewarLocked` deleted (orphaned after act left the predicates;
`SpotterPrecrisisLocked` stays — still used by a non-migrated predicate). Their
`UI_*` locale keys are now unused (INFO-level, tolerated).

Build verification: `dotnet build CivicSurvival.csproj --no-incremental` —
0 errors, 0 CIVIC017 (analyzer exemption verified), 0 warnings in migrated files.
