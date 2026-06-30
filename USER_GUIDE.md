# Civic Survival — Player Guide (Beta)

**Civic Survival** turns Cities: Skylines II into an infrastructure survival challenge.
Keep the power grid alive, defend your city against drone and missile attacks, mobilize
crews under pressure, and weather the cost of crisis. This is an early public beta — the
core loop is playable now, and more systems open in later phases.

> *"Systems critical. Initiating blackout protocol."*

---

## Installation & Requirements

- **Game:** Cities: Skylines II (the mod targets the current game version).
- **Where to get it:** Subscribe on **Paradox Mods** — the game downloads and enables the
  mod automatically. No manual file copying is needed for the published build.
- **Loading:** Civic Survival loads automatically when you start or load a city. There is
  no separate on/off switch in the Mods folder — if the mod is subscribed, it runs.
- **Manual install (advanced):** if you build from source, the mod lives in
  `…\Colossal Order\Cities Skylines II\Mods\CivicSurvival\`.

Once loaded, a Civic Survival status bar and command panel appear in the in-game UI. The
top status bar always shows your grid frequency, power balance, battery reserve, crisis
economy, and current threat level. The command panel is organized into tabs (POWER,
DISTRICTS, RADAR, DEFENSE, INTEL, BUDGET, and more) plus a SETTINGS button.

---

## Getting Started

When a new city first enters the crisis, Civic Survival plays a short **intro sequence**
("AIR RAID ALERT" — *the old rules of economics are gone; the rule of survival has begun*).
During the intro you'll be asked once whether to enable **Online** and, optionally,
**developer diagnostics** (see *Settings* below). You can change this any time later. If you'd rather skip the intro on
future loads, there's a **Skip Intro** option in settings.

Right after the intro, your city receives a **Heritage Grant** — a handful of free, weak
starter anti-air guns, auto-placed near your most important buildings (power plants first,
then fire stations, then hospitals). How many you get scales with city size (2 for a small
village, up to 5 for a megacity). These are intentionally weak — the first wave will show
you that you need to invest in real defenses.

New players should start by opening the command panel and getting familiar with three
things: the **POWER/grid status** (top bar), the **DISTRICTS** tab (where you control
blackouts), and the **DEFENSE** tab (where you build and manage air defense). Per-section
**"?" help portals** are built into the panels — tap them when a system is unfamiliar.

**Recommended difficulty:** the default preset is **Blackout Protocol** (*"Realistic
survival"*). New to the mod? Try **Managed Deficit** (easy — learn the mechanics). Want a
relaxed builder? **Stable Grid** removes the pressure. Hardcore? **Island Mode** /
**Total Collapse**.

---

## The Core Loop

Civic Survival is one connected pipeline:

> **Threat waves** attack your infrastructure → your **air defense** intercepts some, but
> never all → leakers damage **power plants** (capacity loss) and **buildings** (fires,
> rubble) → lost capacity pushes your **grid** toward deficit → you use **rolling
> blackouts** and **backup power** to ration what's left → you spend from your **economy**
> to repair, resupply ammo, mobilize crews, and upgrade defenses → during the calm before
> the next wave, you rebuild and prepare.

The golden rule: **no single strike ends the game, but a perfect shield is impossible.**
Deficit is normal pressure, not failure. Good defense reduces how deep a strike cuts and
how fast you recover — it never makes you invulnerable. Always plan for recovery.

---

## Phase 1 Systems

### Rolling Blackouts (DISTRICTS tab)

When power supply can't meet demand, you decide which districts get power and when. Open
the **DISTRICTS** tab, select a district from the grid, and assign it a **schedule**:

- **Manual** — always on until you intervene or a citywide blackout hits.
- **4 on / 2 off** (Mild Restriction) — ~33% saving.
- **4 on / 4 off** (Balanced) — ~50% saving, the standard rolling blackout.
- **2 on / 4 off** (Severe Crisis) — ~66% saving, survival mode.
- **Day Shift** — on 08:00–20:00, off overnight (~50% saving); good for offices/commerce.

You can also toggle power per **building category** (Residential, Commercial, Industrial,
Office), set district **protection** (critical infrastructure like hospitals, fire
stations, and water pumps can be kept powered), and use **BLACKOUT ALL / RESTORE ALL** for
fast citywide control. If demand exceeds supply, automatic **load shedding** will cut the
lowest-priority buildings to keep the grid stable.

When the grid runs short, your decisions become a load-shedding puzzle: choose what stays
lit. Citizens notice — blacked-out neighborhoods complain, and they especially notice if a
"VIP" district never loses power.

### Threat Waves (RADAR / INTEL tabs)

Attacks come in waves that cycle through phases: **All Clear → Air Raid Alert → Attack in
Progress → Recovery**. During calm you prepare and repair; during an alert you brace; during
an attack you watch and hope your defenses hold.

Two threat types attack in Phase 1:

- **Shahed drones** — slow, audible "moped" drones. Medium damage, multiple per wave. They
  mostly target residential and infrastructure. Basic AA is fairly effective against them
  (70%+ with decent guns).
- **Ballistic missiles** — fast (about 10× a drone), almost no warning. They dive nearly
  vertically onto **strategic infrastructure** (energy/critical/service — never civilian
  housing). One impact can destroy several buildings in a radius and start fires around it.
  They are near-unstoppable without a **Patriot** battery, and how many arrive scales with
  your city's power production (up to 3 per wave in a large city).

Waves don't spread evenly. About 45% of a wave concentrates into **focus clusters** that
pile multiple drones onto the same building to demolish it; the rest disperse into isolated
fires. Energy-focused strikes show up as **blackouts** (plants lose capacity rather than
collapsing into rubble); civilian focus targets become **rubble**.

The **RADAR** tab shows incoming threats; the **INTEL** tab shows the tension level, enemy
focus (Generation / Substations / Residential), and an attack forecast.

### Air Defense (DEFENSE tab)

Open the **DEFENSE** tab ("Air Defense Command") to build and manage your anti-air. AA
intercepts automatically — you can't aim shots yourself, but where you place guns and how
you crew them is everything.

**Build AA:** pick a system, click PLACE, then click on the map. Available systems
(progression from free to elite):

| System | Cost | vs Drone | vs Ballistic | Crew | Notes |
|--------|------|----------|--------------|------|-------|
| Heritage Bofors | Free | 35% | 0% | 4 | Auto-granted on Day 0, weak |
| Bofors 40mm | $10K | 50% | 0% | 6 | Early upgrade |
| Gepard | $50K | 75% | 0% | 6 | Mid-game workhorse |
| Patriot SAM | International aid | 70% | 40% | 15 | Only counter to ballistics |

**Key rule: quality beats quantity.** A few Gepards intercept more than many Heritage guns,
because they reload faster and hit harder. Spread your coverage instead of clustering all
guns in one spot, and don't place guns right next to hospitals or police stations — they're
deliberately less effective there to avoid debris casualties.

**The intercept ceiling:** no matter how good your AA is, it can intercept at most **~75% of
a wave** — **at least ~25% always leaks through**. Focus clusters are about twice as hard to
shoot down. This is by design: invest in AA to reduce damage, but always budget for recovery.

**Ammo economy:** every gun has limited ammo (the AA panel shows current ammo and warns on
**LOW AMMO**). Guns top up automatically during the calm phase. If you run dry mid-attack,
use **EMERGENCY RESUPPLY** (about $50K, flat) for an instant refill — but it raises
corruption. Low ammo (under ~20% of capacity) also drops a gun's efficiency by ~20%.

**Engagement policy:** choose **Grid Integrity** (protect power plants first — stable grid,
scandal risk) or **Humanitarian Shield** (protect hospitals first — high reputation,
blackout risk). This biases which targets your guns prioritize.

### Mobilization (Manpower)

AA guns need crews. If you don't have enough manpower, a placed gun spawns **UNMANNED** and
**cannot fire** — wasted money. Check your manpower **before** building more guns.

In the **Manpower** panel you can see available personnel, casualties, morale, and war
fatigue. Two levers:

- **Activate Conscription** — +50% manpower, but −10 reputation and −10% happiness (forced
  mobilization is unpopular).
- **Call to Arms** — recovers 20 casualties for −5 reputation (has a cooldown).

Don't build more AA than you can crew. A Patriot alone needs 15 crew — sometimes three
Gepards (18 crew) are the better use of your manpower pool.

### Spotters & Intel (INTEL tab)

Enemy informants ("spotters") report your infrastructure locations, which increases enemy
targeting accuracy and reduces your AA effectiveness. You can counter them:

- **Internet Access** (per district, DISTRICTS tab) — turn a district's internet **off** to
  silence spotters there (no leaks), or leave it **on** (spotters can report positions).
- **Counter-OSINT operations** — flood enemy channels with decoys to suppress spotter
  influence (costs budget; lapses if funds run out).

On the **INTEL** tab you can also **buy a source report** to sharpen your view of the next
attack (composition, ETA, projected impact). Intel costs money — and during high tension,
imports cost more.

### Backup Power (BACKUP)

Generators and batteries keep critical buildings running when the grid goes dark. In
difficulty settings you can enable **Backup Power** (batteries charge during surplus and
discharge during deficit) and **Protect Critical** (hospitals, fire stations, and water
pumps stay powered during blackouts).

**Backup Power Modernization** lets you do a one-time, per-district purchase of backup
equipment. You choose a contractor:

- **Honest Contractor** — reliable equipment, no kickback, no investigation risk.
- **"Your Guy"** (corrupt) — counterfeit equipment (50% capacity), 80% kickback into your
  pocket, but fire risk and added investigation pressure.

Generators need fuel and can catch fire if mistreated — backup power is insurance, not a
free pass.

### Economy & Finance (BUDGET tab)

The **BUDGET** tab ("War Economy") tracks your total liquidity across the **official budget
(Treasury)** and a **shadow fund (offshore)**, plus war expenses, aid received, and your net
position. Everything — AA, ammo resupply, repairs, intel, backup power — is paid from here.

Power isn't only a constraint, it's a lever. Difficulty presets set a **legal import cap**
(how much power you can buy from neighbors) and a **shadow import price** per MW for going
over that cap. Tight caps force self-sufficiency; shadow imports keep the lights on but cost
more and attract attention. Suspicious activity (shadow exports, VIP protection, corrupt
contracts) can trigger investigations, protests, and consequences down the line.

### Tutorial & Onboarding

Civic Survival guides you in rather than dumping everything at once:

- The **intro sequence** sets the scene and asks about diagnostics.
- A **first-strike** prompt appears after your first wave, once you've felt the damage.
- **Per-section "?" help portals** are embedded in each panel.
- **Milestone** moments mark your survival (30 / 90 / 180 / 365 days).

---

## Settings (SETTINGS button)

Open settings from the top status bar. Notable options:

- **Language** — choose your UI language (or follow the game default).
- **Theme** — Tech Noir (dark) or Classic Gold.
- **Online features** (Global Grid) — optional global AI news, online stats, leaderboards,
  and an optional **nickname** (3–20 characters; don't use your real name or email — if you
  set one, it is **publicly visible on the leaderboard**). Off by default. When Online is on,
  the mod sends a stream of in-game **city/gameplay events** (threat waves & intercepts,
  blackouts & grid state, mobilization, economy, scenario progress, and the periodic city
  state) so the server can generate the news, stats, and leaderboard standings it returns to
  you. This event stream is **functional** — it powers the online features you asked for and
  is sent whenever Online is on, **independently of diagnostics**. Your city's data is tied to
  a **random ID that is not your real-world identity**.
- **Developer diagnostics (telemetry)** — **off by default, opt-out under Online.** A separate
  switch you can turn off while keeping Online on; it is only sent while Online is on. When on,
  the mod collects developer analytics: crash reports and CivicSurvival error stack traces,
  performance metrics (FPS, frame time, memory) sampled about once a minute, a one-time
  hardware snapshot (CPU cores, RAM, GPU + VRAM, OS platform), and an end-of-session balance
  summary, alongside the random install/session ID and mod/game version. Turning diagnostics
  off does **not** stop the functional city/gameplay event stream above — that flows whenever
  Online is on. Diagnostics **never** collect city names, save names, files, Steam ID, chat,
  email, or location. Because this is a brand-new, large codebase tested by a tiny team,
  leaving diagnostics on is the single most helpful thing you can do to help find bugs and
  balance problems.
- **Bug Reporting (debug mode)** — logs detailed errors so you can send a bug report. Turn
  on only if you're gathering diagnostics. An in-settings **Error Report** panel can send,
  copy, or clear logged errors (requires telemetry enabled to send).
- **Skip Intro**, **siren sounds**, and **dark-humor messages** toggles.
- **Advanced difficulty settings** — import cap, build delay, random disasters, winter
  demand, neighbor envy, backup power, and protect-critical toggles. Changing any of these
  switches your preset to **Custom**.

---

## Languages

Civic Survival ships in **English and Ukrainian**.

The mechanics are identical across languages; the storytelling differs. The **Ukrainian
build** carries a realistic crisis context with localized satire (the "Power Company"
becomes a real utility, "Emergency Shelter" becomes a resilience point, "incoming threat"
becomes "Shaheds inbound"). The **English build** stays a neutral infrastructure simulation.
The language you choose defines the experience.

---

## Beta Caveats & Known Limitations

This is an **early public beta**. Expect bugs and rough edges — some systems may
not yet work the way you'd expect. We're starting small and improving the mod actively.

- **Saves are not version-stable.** We do **not** guarantee save compatibility between
  versions during the beta. A save made in one version may fail to load or behave
  unexpectedly after an update. Treat beta saves as disposable until v1.0.
- **Patriot / ballistic defense:** ballistic missiles require a Patriot battery to
  intercept; until you have one, expect most ballistics to leak.
- Some visuals are placeholders (e.g. a ballistic missile currently reuses the drone model);
  gameplay is unaffected.

**Reporting bugs & feedback:** bug reports are hugely welcome. Report them on our Discord:
**https://discord.gg/yg4G2rVrd**. Enabling telemetry (and Bug Reporting if
asked) gives us the data to fix problems faster.

---

## Coming Later

Opened gradually as Phase 1 stabilizes — pace set by bug reports, not a calendar:

- **Phase 2** — corruption, anti-corruption countermeasures, and international diplomacy.
- **Phase 3** — shadow economy, cognitive / information warfare.
- **Phase 4** — neighbor relations.
- **Mod v2** — offensive **Grid Warfare** and a competitive **PvP arena**.

*Inspired by real-world infrastructure resilience challenges.*
