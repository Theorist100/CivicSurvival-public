# Building

**Read this first: this repository is for reading the code, not for building it in
one click.**

The source is published for transparency and auditability. A full build of a Cities:
Skylines II mod requires a configured CS2 modding environment, and on top of that
**this public snapshot deliberately omits the project's private source generators**,
so the snapshot does **not** compile end-to-end for third parties. That is by design,
not a bug — see "Why the public snapshot does not fully compile" below.

If you only want to verify what the mod does, just read the code. If you want to
understand the build contract anyway, the requirements below are accurate to the
project's `CivicSurvival/CivicSurvival.csproj`.

## Requirements

1. **Cities: Skylines II installed.** Provides `Game.dll` plus the Unity / Colossal /
   UnityEngine managed assemblies the project references. The build resolves these
   from the game's `…\Cities2_Data\Managed\` folder. If your game lives somewhere the
   toolchain does not auto-detect, point the build at it via the `GameManagedPath`
   MSBuild property (`-p:GameManagedPath=...`) or the `CSII_MANAGEDPATH` environment
   variable — no need to edit the `.csproj`.

2. **CS2 Modding Toolkit**, with the `CSII_TOOLPATH` environment variable set. The
   project imports `Mod.props` / `Mod.targets` from this path; without it the project
   will not load in MSBuild.

3. **Unity mod-project** (for Burst AOT). Only needed when building with Burst
   enabled (`EnableCivicBurst=true`). The build post-processor uses the Unity mod
   project to compile native Burst output.

4. **Python and Node.js** — the prebuild step runs code generators and contract
   checks (`scripts/generate.py`, `scripts/contract_check.py`,
   `Tools/generate-binding-manifest.js`, and others). These run automatically before
   compilation and will fail the build if generated artifacts are stale.

5. **UI toolchain** — Node.js with the UI's `npm` dependencies; the UI is built with
   webpack and type-checked / linted as part of the build.

## Burst is optional

The mod has a single Burst switch, `EnableCivicBurst` (in `CivicSurvival.csproj`).

- With Burst **off** (`EnableCivicBurst=false`), jobs run as managed IL and
  **Unity.Logging is not required at all** — its reference and source generator are
  gated on `EnableCivicBurst=true`, and Burst logging is behind `#if ENABLE_BURST`.
  This is the simpler configuration.
- With Burst **on** (`EnableCivicBurst=true`), the build additionally needs the
  Unity.Logging binaries (`Unity.Logging.dll`, `LoggingCommon.dll`,
  `MainLoggingGenerator.dll`). **These are not included in this repository.** They are
  Player-compiled from `com.unity.logging@1.2.1` using a local Unity Editor. Obtaining
  them is an extra step only relevant if you specifically want the Burst-compiled
  performance path.

## Why the public snapshot does not fully compile

The project relies on a private set of **Roslyn source generators** (part of the
`CivicSurvival.Analyzers` project) that are **not published** in this snapshot.
Several of these generators emit code the client needs at compile time, so without
them the client will not compile completely.

The public snapshot therefore has the `ProjectReference` to `CivicSurvival.Analyzers`
(and its analyzer-only `AdditionalFiles`) removed from the public `.csproj` as a
cosmetic measure — so the project does not reference a project that isn't here. The
reference to `CivicSurvival.Contracts` is kept (those wire contracts are published and
the client needs them).

This is intentional: the goal of this repository is **readable, auditable code**, not
a reproducible third-party build. The author's own store releases are built from the
private repository, which has the generators.
