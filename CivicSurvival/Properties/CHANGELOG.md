# Civic Survival — Changelog

Player-facing release notes shipped with the Paradox Mods build. The full internal
development history lives in `Docs/Project/CHANGELOG.md` in the source repo and is **not**
shipped to subscribers. Keep this file in sync with the `<ChangeLog>` element in
`Properties/PublishConfiguration.xml` (Paradox shows that one in the launcher).

## v0.3.22 — Bug fixes and performance improvements

- Bug fixes and performance improvements.

---

## v0.3.21 — Counterattack: War Room & Strikes

- New: War Room — a fullscreen command overlay on its own dashboard tab: situation map with the threat radar, wave status, and a STRIKE view for planning attacks.
- New: counterattack — build a drone launcher and strike targets in the enemy city; damage sets back their attack axes. Intel level controls how precisely you see the enemy's state, and intel can be bought mid-wave.
- The radar now tells your outbound strikes apart from incoming threats.
- Update safety: the game warns ahead of a mod update and offers a restart if an update lands mid-session.
- Stability: removed the UI-engine crash triggers around the post-wave report — the most frequent native crash on 0.3.19/0.3.20.

---

## v0.3.19 — Missile Defense & Shadow Arms

- New: MIM-23 Hawk — a drone-hunting missile launcher, cheaper than the Patriot. Bought, donor-funded, or off the black market; runs on its own rockets.
- Air defense ammo is tracked by kind now — guns use shells, launchers use rockets — each with its own "restock all" button.
- New: buy air defenses off the books — the same launchers pushed past spec for shadow money, at the risk of exposure.
- New: Prosperity ladder — a third arena board ranking how well your city lives.
- Clearer shadow ledger, and refused actions now explain why.
- Better handling of the rare "no air attacks after an update" install issue.

---

## v0.3.18 — Shadow Ladder & Hourly Schemes

- New: Shadow ladder — a second arena board ranking what you stole and kept. Five titles from Aid Skimmer to Disaster Capitalist.
- Corruption schemes pay every game hour instead of once at midnight; daily totals unchanged.
- Set or change your nickname from the arena board.
- Clearer ledger labels; the city's crisis injection is now Crisis Aid.

---

## v0.3.17 — Arena: Global Defense Ladder

- New: Arena — global defense ladder (top menu). Earn points for intercepted threats (deeper waves are worth more), climb five ranks from Blackout Survivor to Iron Mayor. All-time and weekly boards, instant points after each wave, rank-up notifications.
- Requires Global Connection for ranking; without it the game is unchanged.

---

## v0.3.16 — Diagnostics

- Extended the opt-in anonymous telemetry so corruption balance (contract offers, shady-contract failure rates, shadow income) can be tuned from real data.
- No gameplay changes.

---

## v0.3.15 — Construction Kickback & Pause-Friendly UI

- New corruption scheme: construction kickback — set a kickback rate to skim a share of the city's construction spending into the shadow wallet, accrued daily.
- The maintenance contract dialog is now fully clickable and works while the game is paused, including declining the offer.
- Most player actions now work while paused: repairs, intel purchases, hero orders, spotter operations, emergency AA resupply, investigation and police choices, policy and scheme sliders, mobilization toggles. The donor conference dialog can be closed while paused.
- Maintenance contract offers now arrive at their intended frequency, show proper building names, and their popups are dismissed once the offer is answered or expires.
- Internal cleanup of the payment pipeline; no gameplay changes from it.

---

## v0.3.14 — Corruption, Diplomacy & Shadow Economy

- Opens the remaining gameplay block: corruption schemes with counter-investigations, international donor diplomacy, the shadow economy, and neighbor relations between districts.
- New corruption scheme: a draft-exemption ring selling deferrals and fake disability papers.
- Rebalanced World Shock; compacted the Defense panel.
- Broken installs (mod model files missing after an update) are now detected with detailed diagnostics, and the in-game dialog gives a repair sequence that works.

---

## v0.3.13 — Diagnostics

- Added internal diagnostics to detect and report missing UI icons, so icon-delivery problems can be caught and fixed faster.
- No gameplay changes.

---

## v0.3.12 — Stability

- Improved mod-loading resilience when the game registers the mod's assets only partially at startup.
- Clearer guidance when air-attack models fail to load, pointing to playset re-activation.
- No gameplay changes.

---

## v0.3.11 — Bug fixes

- Fixed narrative notifications staying silent in any city started after the first one in a game session.
- The manual bug report button now has a short cooldown; repeated clicks no longer send duplicate reports.
- No gameplay changes.

---

## v0.3.1 — Balance tuning

- Population flight from strikes tuned down to a more playable rate.

---

## v0.3.0 — Information war

- Enemy psy raids now strike the city three distinct ways: propaganda feeds draft evasion that drains your manpower pool, fabricated videos send workers home on fake sick leave, and rumors keep driving panic buying.
- The info-war panel maps each attack type to the damage it deals, with a new in-game help section covering the Propaganda Center and telecom coverage.
- The Propaganda Center building snaps to the road and faces it when placed.
- Population flight from strikes is significantly stronger: city-size exodus multipliers restored to their intended values.
- Deploying the right speaker archetype counters the matching attack type, including its new effects.

---

## v0.2.15 — Fixes and tweaks

- Demolishing an air defense position now refunds part of its cost and returns its crew, matching the base game's bulldoze refund.
- Air defenses retarget correctly after being relocated.
- Power plants no longer offer repair when they have no wear.
- Further reliability work on mod content loading for new subscribers.

---

## v0.2.14 — Bug fixes

- Further bug fixes and crash-stability work.
- No gameplay changes.

---

## v0.2.13 — Bug reporting

- Crash report: choose which crash dumps to send. A scrollable list shows each dump with its time and size, so you can pick the one that matches the crash.
- Crash diagnostics improved for more accurate abnormal-shutdown classification.
- No gameplay changes.

---

## v0.2.12 — Compatibility

- Hardening for compatibility with other mods that change game system order.
- No gameplay changes.

---

## v0.2.11 — Stability

- Further crash-stability work on the rendering path used during attacks.
- No gameplay changes.

---

## v0.2.10 — Stability & crash fixes

- Rendering safety: threat and interceptor visuals are now safely synced with the game's own render pass every wave frame, closing a race that could crash the game during attacks — especially when focusing the camera on a drone or under heavy fire.
- Fewer crashes & corruption: hardened refugee arrivals, neighbor reactions, and save handling against rare crash and save-corruption cases.
- Better crash diagnostics: native crashes are now attributed to the mod vs the base game, so we can pinpoint and fix the real cause faster. Keeping telemetry on helps a lot here.

---

## v0.2.9 — Wave pacing rebalance

- The wait before the first attack is shortened, while the lulls between later waves keep their full length — the opening strike comes sooner without speeding up the rest of the fight.

---

## v0.2.8 — Early-scenario fixes for small towns

- New power plants now deliver ~20% of their capacity from the start of construction instead of zero, so a small city isn't left fully blacked out while a plant is being built.
- Faster first attack and shorter lulls between waves, especially in small towns — less waiting before the action starts.

---

## v0.2.7 — Maintenance

- Improved logging. No gameplay changes.

---

## v0.2.6 — AA auto-resupply

- Air defense — optional auto-resupply for AA ammo (toggle it in the air-defense menu; magazines refill over time when funded).
- Further wave and air-defense tuning, plus general performance improvements for smoother raids.

---

## v0.2.5 — Forest fires, Patriot/wave rebalance, air-defense performance fix

- Forest fires — threat debris now ignites your trees and spreads through woodland.
- Air defense — Patriot is now anti-ballistic (weak vs drones); smaller magazines; higher wave cap.
- Waves — less frequent but harder.
- Major drop in air-defense main-thread cost during raids — noticeably smoother FPS under heavy waves.

---

## v0.2.4 — Reliable 3D model loading on first launch

- Mod 3D models (attack drones, missiles, and anti-air units) could fail to load on the very first launch right after subscribing, leaving threats invisible or missing until you restarted the game. They now load reliably on the first launch, so a fresh subscription works immediately.

---

## v0.2.3 — Patriot interceptors, Gepard, new radar, air-defense rebalance

New:
- Visible Patriot interceptors — the Patriot now launches a real interceptor missile that flies out and detonates on the incoming threat, instead of firing invisible gun tracers.
- Flakpanzer Gepard — a new mobile anti-air unit joins the defense roster, with its own 3D model and a streamlined defense panel.
- New command-post radar — the threat radar is redesigned as a tactical map with coastline and water, a camera marker, and clickable air-defense sites that pan the camera to them.

Balance:
- Air-defense rebalance — Patriot/AA ammunition now scales with city size, per-wave resupply is capped, the manpower pool is larger with cheaper crews, and gun accuracy is tuned toward saturation defense.
- Gentler first wave — the opening attack is no longer a forced massive strike; it is now at most 20% stronger than a regular wave.
- Shorter pre-war — the pre-war phase is trimmed from 30 to 20 days.

---

## v0.2.2 — Tracers, radar target boxes, bug fixes

New:
- AA tracers — anti-air fire now renders as visible world-space tracers (orange-red, hot head with a fading tail). Previously the shells were effectively invisible.
- Threat radar target boxes — buildings under attack now show as schematic 3D boxes on the threat radar.
- Richer news feed — the in-game chronicle now reacts to scenario, mobilization and cognitive-warfare beats.

Plus a round of bug fixes and performance and reliability improvements.

---

## v0.2.1 — Bug report delivery fix

- In-game bug reports now reach the team reliably — long reports were previously dropped before delivery. Plus internal telemetry cleanup.

---

## v0.2.0 — Air defense & online update

Adds the Patriot (MIM-104) SAM with an optional drone-intercept toggle, per-AA-type ammo bars and independent resupply, and conscription as a managed toggle with a reactivation cooldown. Reworks the online setup into a single Global Grid consent with personal AI news digests and separate news channels, plus a new audio mute toggle. Stability and performance: slow mod loads no longer freeze startup, fewer render spikes, and a startup-crash fix.

---

## v0.1.1 — Maintenance update

- Removed the closed-beta startup check-in.

No gameplay changes from v0.1.0. Anonymous telemetry remains opt-in (off by default).

---

## v0.1.0 — First public beta (Phase 1)

First wave of the beta — expect bugs and systems that may not work as intended yet.
**Saves are NOT version-stable: a save may not load after an update.**

Available this phase:

- Rolling blackouts by district (4-on / 4-off, day-only, manual)
- Threat waves: Shahed drones and ballistic missiles
- Air defense: deploy AA, assign crews, ammo economy
- Mobilization: conscript manpower to crew defenses
- Spotters & Intel: buy reconnaissance
- Backup power: generators and batteries
- Economy & Finance
- Tutorial onboarding
- English and Ukrainian

Anonymous telemetry is opt-in (off by default) — please enable it in mod settings to help
us find bugs. We collect only an anonymous ID, gameplay/performance events, and crash
reports; never personal data.

Later phases add corruption, diplomacy, shadow economy, and refugees. Grid Warfare and the
PvP arena are planned for mod v2.
