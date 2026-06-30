# Civic Survival Privacy

Civic Survival has two separate things you can turn on: **Online** features and
**developer diagnostics**. They are controlled together with a clear hierarchy —
diagnostics is a sub-option under Online and is never bundled in without an
off switch.

The data Civic Survival sends is tied to a random ID generated on your machine
(see *Online* below), not to your real-world identity. We never collect your name, email, or Steam ID. If you choose a leaderboard
nickname, that nickname is publicly visible.

## Online (functional features you ask for)

When you turn **Online** on, your city sends data to the Civic Survival server so
the server can generate and return online features **to you**:

- **AI news.** Global AI news digests are generated on our server and are free for
  all online players — this is the main online feature. Personal AI news (a digest
  of your own city) are free during the beta; at release they will require your own
  API key (bring-your-own-key), because per-player generation does not scale on our
  account. Personal AI news are an add-on — Online and global news work without them.
- **Online stats and leaderboards.**
- **A nickname** you optionally choose for the leaderboard.

To generate this content, the mod sends a stream of **in-game city events** — for
example wave outcomes, blackouts, infrastructure status, arrests, donations, and
the periodic city state (power balance, money, population, air-defense readiness).
The server turns this into the news, stats, and leaderboard standings it returns to
you. This event stream is **functional**: it is sent whenever **Online** is on, on
its own, and does **not** depend on whether developer diagnostics are enabled —
without it the AI news and stats would have nothing to report.

This is all processing to deliver a feature you requested, so it is part of turning
Online on. Turning Online off stops all of it.

Online also sends an **anonymous crash count**: if the previous run ended in a crash,
the mod reports just the **mod version** and a coarse crash marker (for example
"Runtime.InSimulation") so we can see how often each version crashes. This count carries
**no** session ID, player ID, IP, game state, or any other identifier, and the server
keeps only an aggregate tally per version — it cannot be tied back to you. It is separate
from the detailed crash reports below, which are sent only with diagnostics on. It is sent
whenever Online is on so a crash rate covers all online players, not just the diagnostics
subset; turning Online off stops it.

## Developer diagnostics (analytics for the developer)

Developer diagnostics are **optional and opt-out**. They are a sub-option under
Online: they are sent **only while Online is on**, and you can turn them off at any
time while keeping Online on. Turning Online off also stops diagnostics.

When diagnostics are on, the mod may send these developer-analytics signals (on top
of the functional event stream above, which Online sends regardless):

- Mod version and game version
- A random install/player ID generated locally by the mod. This random ID is created on first launch and stored only on your machine; it is sent to the server only after you enable Online. It links your sessions to each other but is not tied to your real-world identity.
- A random session ID
- Crash reports and CivicSurvival error stack traces
- For native crashes, the faulting module name and instruction offset only — read locally from
  the crash dump on next launch to tell whether the mod or the base game crashed. The crash dump
  file itself, its memory contents, and any embedded screenshot are **not** sent; file-system
  paths and your OS user name are masked out of the module name.
- FPS, frame time, and memory usage
- A one-time hardware snapshot: CPU core count, RAM amount, GPU model and VRAM amount, and operating system platform (Windows / macOS / Linux)
- A developer balance summary (such as how much of a session was spent in blackout, against the design target)

The city/gameplay metrics that drive the AI news and stats (wave number, power
production/demand, game day, population, city budget, and the like) are part of the
**functional** Online event stream described above, not the diagnostics opt-out — so
they are sent whenever Online is on, and turning diagnostics off does not stop them.

## What Is Not Sent

- Steam ID
- Email address
- Real name
- Chat or private messages
- City names
- Save names
- Local files
- Precise location
- CPU model name or operating system build version (only your CPU core count and OS platform are sent)

## Crash Reporting (Sentry)

When diagnostics are effectively on (**Online on and diagnostics on**), the in-game
UI also sends JavaScript crash reports — error messages, stack traces, the mod
version, and the build environment — to **Sentry** (sentry.io), a third-party
error-monitoring service, on its EU instance (data residency in Germany). It uses
the same effective gate as the rest of diagnostics: turning Online off, or turning
diagnostics off, stops it and closes the connection, and reports captured before
the gate is on are discarded. Sentry is configured to **not** collect default
personal data — no IP address, no cookies, and request headers are stripped before
sending. Performance tracing and session replay are disabled.

## Manual Bug Reports

If you press the manual bug report button, the report can include recent
CivicSurvival logs and technical system information. Before the report is sent,
file-system paths and your operating-system user name are masked out of it. Crash and
error stack traces are masked the same way. Do not put personal information in the
report comment.

## Control

Everything is controlled in the mod's Online settings tab:

- **Online** is the master switch. Turning it off stops both the functional online
  features and diagnostics.
- **Developer diagnostics** is a separate switch under Online. You can turn it off
  while keeping Online on; diagnostics are then not sent even though Online is on.

The first time you enable Online, a short consent prompt explains the functional
features and lets you choose whether to also enable diagnostics.

## Hosting & Retention

The Civic Survival server is hosted in Europe (EU). Retention depends on the kind of
data:

- **Developer diagnostics** (crash reports, performance, hardware snapshot, balance
  summary) are intended to be retained for **30 days**, for diagnostics, balancing, and
  abuse protection.
- **Functional Online data** tied to your player ID (the city/gameplay event stream,
  online stats, and your leaderboard nickname) is retained for as long as it is needed
  to provide the online features — for example, leaderboard standings persist while the
  leaderboard is live — or until you ask us to delete it (see *Your rights* below).

## Local caching on your machine

Before anything is sent, the data is **queued in plaintext on your machine** so it can
be retried if the network is unavailable. This includes the pending event stream, crash
context, and the credentials file that holds your random player ID. A few notes on how
this behaves:

- **Turning diagnostics or Online off erases the pending local queue.** When you opt out,
  the not-yet-sent queue is cleared rather than kept for later — it is not re-sent if you
  turn things back on.
- **Restarting the game keeps the queue** so a report that was interrupted can still be
  delivered next time you play with Online on. This is the only case where queued data
  survives.
- The credentials file holding your player ID stays on your machine; the authentication
  token in it is **protected by the operating system** (DPAPI on Windows) and is not
  stored in plaintext.
- Before a manual bug report or a crash/error report is sent, **file-system paths and
  your operating-system user name are masked** so they are not included.

## Your rights and data deletion

You can ask us to delete the data associated with your player ID. The canonical channel
is our **Discord** (the same place bug reports go):
**https://discord.gg/yg4G2rVrd**. Tell us your player ID and ask for deletion.

Turning Online off, or turning diagnostics off, stops new data from being sent and clears
the pending local queue, but it does not by itself remove data already received by the
server — that is handled through a deletion request as above. The server-side part of a
deletion request is processed separately and may depend on the hosting setup.

You also have the right to ask what data we hold about your player ID, and to ask us to
correct it.

## Legal basis and age

For players in the European Union, this processing falls under the GDPR:

- **Developer diagnostics** are processed on the basis of your **consent**, which you give
  by enabling them and can withdraw at any time by turning them off.
- The **functional Online data** is processed to deliver the online features you turned on
  — that is, on the basis of providing the service you requested (and our legitimate
  interest in running it).

Civic Survival is not directed at children. If you are below the minimum age for digital
consent in your country (16 in much of the EU, lower in some member states), you should
only enable Online or diagnostics with the consent of a parent or guardian.

