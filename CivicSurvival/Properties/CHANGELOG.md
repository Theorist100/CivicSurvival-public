# Civic Survival — Changelog

Player-facing release notes shipped with the Paradox Mods build. The full internal
development history lives in `Docs/Project/CHANGELOG.md` in the source repo and is **not**
shipped to subscribers. Keep this file in sync with the `<ChangeLog>` element in
`Properties/PublishConfiguration.xml` (Paradox shows that one in the launcher).

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
