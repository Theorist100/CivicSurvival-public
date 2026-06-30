/**
 * Systems Critical UI - Entry Point
 * Registers UI components with Cities: Skylines 2
 */

import React from "react";
import { type ModRegistrar } from "cs2/modding";
import { Dashboard } from "components/Dashboard";
import { ToastContainer, RejectToast } from "components/shared/toast";
import { IntroOverlay } from "components/scenario";
import { ProcurementChoicePanel } from "components/procurement";
import { FloatingChirperPanel } from "components/news/FloatingChirperPanel";
import { ModRoot } from "components/ModRoot";
import { CrashReporterBootstrap } from "components/CrashReporterBootstrap";
import { useMaintenance } from "hooks/domain";
import { parseProcurementOffer } from "hooks/bindings/procurementBindings";
import { useModalCoordinator } from "hooks/scenario/useModalCoordinator";
import { reportError } from "services/crashReporter";
import { scLog } from "utils/logging";
import { withProfiler } from "utils/uiProfiler";

// webpack CommonJS require, typed locally — tsconfig `types` is restricted to one file
// so @types/node is not active. Used only for the gated, prod-eliminated debug-panel load below.
declare const require: (id: string) => { BalanceDebugPanel: React.FC };

scLog("index.tsx loaded, Dashboard: " + typeof Dashboard);
// Build-mode marker. DefinePlugin inlines the flags, so this resolves to a
// constant string at build time; grep CivicSurvival.log for `[BUILD]` to know
// whether the deployed bundle is a dev build (debug panel present) or production.
scLog(`[BUILD] UI mode=${__CIVIC_DEVTOOLS__ ? "development" : "production"} devtools=${String(__CIVIC_DEVTOOLS__)} version=${__CIVIC_MOD_VERSION__}`);

// Profiled wrappers (stable references — defined at module scope)
const ProfiledDashboard = withProfiler(() => <ModRoot name="Dashboard"><Dashboard /></ModRoot>, "Dashboard");
const ProfiledToast = withProfiler(() => <ModRoot name="Toast"><ToastContainer /></ModRoot>, "Toast");
const ProfiledRejectToast = withProfiler(() => <ModRoot name="RejectToast"><RejectToast /></ModRoot>, "RejectToast");
const ProfiledChirper = withProfiler(() => <ModRoot name="FloatingChirper"><FloatingChirperPanel /></ModRoot>, "FloatingChirper");

const IntroRoot: React.FC = () => {
    const { activeId, version } = useModalCoordinator();
    const resetKey = `IntroOverlay:${activeId ?? "none"}:${version}`;
    return <ModRoot name="IntroOverlay" variant="modal" resetKey={resetKey}><IntroOverlay /></ModRoot>;
};
const ProfiledIntro = withProfiler(IntroRoot, "Intro");

const ProcurementRoot: React.FC = () => {
    const maintenanceState = useMaintenance();
    const maintenance = maintenanceState.status === "ready" ? maintenanceState.data : null;
    const offer = maintenance ? parseProcurementOffer(maintenance.PendingProcurementOffer) : null;
    const resetKey = offer ? `Procurement:${offer.entityIndex}:${offer.entityVersion}` : "Procurement:none";
    return <ModRoot name="Procurement" variant="modal" resetKey={resetKey}><ProcurementChoicePanel /></ModRoot>;
};
const ProfiledProcurement = withProfiler(ProcurementRoot, "Procurement");
const createProfiledDebugPanel = () => {
    if (!__CIVIC_DEVTOOLS__) return null;

    // Gated sync require, NOT a top-level import: in production __CIVIC_DEVTOOLS__ is false,
    // so the early return above makes this unreachable (Terser-eliminated) and webpack's
    // IgnorePlugin stub for this module is never executed — that top-level static import was
    // exactly the prod-bundle crash introduced by 3d4f6e9b67. In dev the module exists and a
    // sync require avoids the async chunk load that broke React.lazy under Coherent.
    const { BalanceDebugPanel } = require("components/devtools/BalanceDebugPanel");

    return withProfiler(() => (
        <ModRoot name="DebugPanel">
            <BalanceDebugPanel />
        </ModRoot>
    ), "DebugPanel");
};
const ProfiledDebugPanel = createProfiledDebugPanel();

// Hidden bootstrap: subscribes to EffectiveDiagnostics (Online && opt-in) and inits/disables
// Sentry as it changes. Wrapped in ModRoot so it lives under ThemeProvider + ErrorBoundary like other roots.
const CrashReporterMount = () => <ModRoot name="CrashReporter"><CrashReporterBootstrap /></ModRoot>;

// Global error handler - additive (does not overwrite engine handlers)
window.addEventListener("error", (event) => {
    scLog(`[GLOBAL ERROR] ${event.message} at ${event.filename}:${event.lineno}:${event.colno}`);
    if (event.error?.stack) {
        scLog(`[GLOBAL ERROR] Stack: ${event.error.stack}`);
    }
    reportError(event.error ?? new Error(event.message), {
        source: "window.error",
        filename: event.filename,
        line: event.lineno,
        column: event.colno,
    });
});

// Catch unhandled promise rejections
window.addEventListener("unhandledrejection", (event) => {
    scLog(`[UNHANDLED REJECTION] ${event.reason}`);
    reportError(event.reason, { source: "unhandledrejection" });
});

const register: ModRegistrar = (moduleRegistry) => {
    scLog("register() called");

    // Crash reporter bootstrap (subscribes to EffectiveDiagnostics, inits/disables Sentry as it changes).
    // Registered first so it observes the binding before other roots can throw.
    moduleRegistry.append("Game", CrashReporterMount);
    scLog("CrashReporterBootstrap registered");

    // Dashboard UI (tabbed interface)
    moduleRegistry.append("Game", ProfiledDashboard);
    scLog("Dashboard registered");

    // Toast notifications (corruption offers, events)
    moduleRegistry.append("Game", ProfiledToast);
    scLog("ToastContainer registered");

    // Intro sequence overlay (04:57 AM Cold Open)
    moduleRegistry.append("Game", ProfiledIntro);
    scLog("IntroOverlay registered");

    // Rejection feedback toast (auto-dismiss, bottom-center)
    moduleRegistry.append("Game", ProfiledRejectToast);
    scLog("RejectToast registered");

    // Procurement choice popup (self-hides when no pending offer)
    moduleRegistry.append("Game", ProfiledProcurement);
    scLog("ProcurementChoicePanel registered");

    // Floating Chirper-style news feed (toggle button, beside vanilla Chirper)
    moduleRegistry.append("Game", ProfiledChirper);
    scLog("FloatingChirperPanel registered");

    if (ProfiledDebugPanel) {
        moduleRegistry.append("Game", ProfiledDebugPanel);
        scLog("BalanceDebugPanel registered");
    }

    scLog("append() done");
};

export default register; // eslint-disable-line civic/no-default-export -- CS2 ModRegistrar API requires default export
