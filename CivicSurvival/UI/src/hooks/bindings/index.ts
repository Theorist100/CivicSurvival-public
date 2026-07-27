/**
 * Re-export all bindings from domain-specific files
 *
 * Individual binding files for domains with JSON DTOs have been removed.
 * Use domain hooks from hooks/domain/ instead.
 */

// Domain JSON (new — replaces individual bindings)
export * from "./domainJsonBindings";

// Core
export * from "./coreBindings";

// Economy — Procurement (types, triggers, helpers only)
export * from "./procurementBindings";

// Threats & Defense
export * from "./shockActBindings";
export * from "./refugeeBindings";

// Scenario & UI
export * from "./scenarioDirectorBindings";
export * from "./introBindings";
export * from "./modalCoordinatorBindings";
export * from "./toastBindings";
export * from "./debugBindings";
export * from "./localeBindings";
export * from "./helpBindings";
export * from "./milestoneTutorialBindings";

// Network & Telemetry
export * from "./networkBindings";

// Arena (Leaderboards)
export * from "./arenaBindings";
