# Civic Survival

**Infrastructure Survival Mod for Cities: Skylines II**

> *"Systems critical. Initiating blackout protocol."*

Transform your city into an infrastructure survival challenge: keep the power grid
alive, defend against drone and missile attacks, and mobilize your crews under
pressure.

- **Play it on Paradox Mods:** <https://mods.paradoxplaza.com/mods/147665>
- **Report bugs / give feedback (Discord):** <https://discord.gg/yg4G2rVrd>
- **User guide:** [`USER_GUIDE.md`](USER_GUIDE.md)
- **Privacy policy:** [`PRIVACY.md`](PRIVACY.md)

This repository is both the **public home for the mod's player documentation** (user
guide, privacy policy) **and** the **client source code**, published for
transparency (see "Reading the code" below).

## ⚠️ Early Public Beta

This is an early wave of the beta and ships **Phase 1 of 4** — the core grid + threats
+ defense loop. Expect bugs and rough edges; the mod is improving actively, and bug
reports and feedback are hugely welcome.

**Saves are not version-stable yet** — a save made in one version may not load after
an update. Treat beta saves as disposable until v1.0.

## One Mod, Two Experiences

| English | Українська |
|---------|------------|
| Neutral infrastructure simulation | Реалістичний контекст кризи |
| "Power Company" | "ДТЕК" |
| "Emergency Shelter" | "Пункт Незламності" |
| "Incoming threat" | "Шахеди на підльоті!" |

Same mechanics, different storytelling. The language you choose defines the experience.

## Available Now — Beta Phase 1

- **Rolling Blackouts** — schedule power outages by district
- **Threat Waves** — Shahed drones and ballistic missiles attack your infrastructure
- **Air Defense** — deploy AA positions, assign crews, and manage an ammo economy
- **Mobilization** — conscript manpower to crew your air defenses
- **Spotters & Intel** — buy reconnaissance to track incoming threats
- **Backup Power** — generators and batteries protect critical buildings
- **Economy & Finance** — fund your defenses and weather the cost of crisis
- **Refugees** — displaced households arrive during the crisis; shelter and integrate them
- **News & Narrative** — an in-game feed reacts to events as your city fights to survive
- **Tutorial** — guided onboarding for the grid + threats + defense loop

Later phases add corruption, diplomacy, and shadow economy. Offensive Grid Warfare and
a competitive PvP arena are planned for mod v2.

## Privacy & Telemetry

Anonymous telemetry is **opt-in and off by default**. When enabled it helps us find
bugs and balance problems. We never collect anything that identifies you personally —
no name, email, Steam ID, city/save names, chat, or precise location. Full details:
[`PRIVACY.md`](PRIVACY.md).

## Reading the code — read it, don't "clone and build"

The client source here is published for **transparency and auditability**, not as a
one-click build. Anyone can read the client code and confirm the mod does exactly what
it claims — nothing more.

> **This is not a buildable distribution.** A full build requires a configured
> Cities: Skylines II modding environment (the game itself, the CS2 Modding Toolkit,
> a Unity mod-project for Burst, prebuild code generators, and a UI toolchain). That
> is normal for a CS2 mod, not a shortcoming. In addition, this public snapshot
> **does not include the private source generators** the project depends on at
> compile time, so the snapshot will not compile end-to-end for third parties **by
> design**. See [`BUILDING.md`](BUILDING.md) for the full picture. Open code gives
> trust without requiring everyone to build it.

### This is the client; the server is closed

What you see here is the **client mod** — the code that loads into Cities: Skylines
II. The **server side is intentionally closed-source**. The server is the authority
for balance formulas, validation, and (in the future) competitive PvP, so that
knowing the client code gives no player an advantage. Open client + closed arbiter is
the right balance of transparency and integrity.

### AI declaration

The entire codebase of this mod is **AI-generated, authored by an AI assistant under
the direction of a single developer.** We are open about this. The guarantee of
quality is the **verifiability of the open source code itself** — this declaration
complements that openness, it does not replace it. If you want to know what the mod
does, read it.

## Inspired By

- Frostpunk (survival city builder)
- This War of Mine (resource scarcity)
- Real-world infrastructure resilience

## License

The client source is published under the **PolyForm Strict License 1.0.0** — see
[`LICENSE`](LICENSE) and [`NOTICE.md`](NOTICE.md). The code is source-available for
reading and noncommercial use; redistribution and derivative works (forks) are not
permitted. Game assets under `Assets/` are licensed separately under
**CC BY-NC-ND** (see `Assets/LICENSE`).

## Contributing

Bug reports and feedback go to our [Discord](https://discord.gg/yg4G2rVrd), not GitHub
Issues. Pull requests are governed by the license — see [`CONTRIBUTING.md`](CONTRIBUTING.md).
