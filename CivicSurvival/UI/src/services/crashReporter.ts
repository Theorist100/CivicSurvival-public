/**
 * Crash reporter — thin wrapper over Sentry SDK.
 *
 * Single point of integration. ErrorBoundary and other callers use reportError()
 * without importing @sentry/react directly. Replacing the backend (or removing
 * it entirely) only touches this file.
 *
 * Init is gated by TelemetryConfig.Enabled. Errors reported before the consent
 * binding is ready are held in a small queue, then flushed on opt-in or dropped
 * explicitly on opt-out.
 */

import * as Sentry from "@sentry/react";
import { scLog } from "../utils/logging";
import { ensureUrlSearchParamsCompat } from "./urlSearchParamsCompat";

// Sentry DSN — injected at build time from the SENTRY_DSN env var (webpack DefinePlugin).
// Public write-only ingest key, kept out of source so the public repo carries no key.
// Empty when not configured (e.g. building without a local .env) → Sentry stays disabled.
// EU instance (data residency in Germany).
const SENTRY_DSN = process.env.SENTRY_DSN ?? "";

let initialized = false;
let telemetryAllowed = false;
let consentResolved = false;

const MAX_EARLY_REPORTS = 20;
let earlyReportOverflow = 0;

interface QueuedReport {
    error: unknown;
    context?: Record<string, unknown>;
}

const earlyReports: QueuedReport[] = [];

/**
 * Initialize Sentry once. Idempotent — repeated calls after the first are no-ops.
 * Must only be called when the user has opted in via TelemetryConfig.Enabled.
 */
export function ensureSentryInitialized(): void {
    telemetryAllowed = true;
    consentResolved = true;

    // No DSN configured (e.g. a build without a local .env): Sentry cannot run.
    // Drop any queued reports and stay disabled — never init with an empty DSN.
    if (!SENTRY_DSN) {
        earlyReports.length = 0;
        earlyReportOverflow = 0;
        return;
    }

    if (initialized) {
        flushEarlyReports();
        return;
    }

    try {
        ensureUrlSearchParamsCompat();
        Sentry.init({
            dsn: SENTRY_DSN,
            release: __CIVIC_MOD_VERSION__,
            environment: __CIVIC_DEVTOOLS__ ? "development" : "production",
            sendDefaultPii: false,
            tracesSampleRate: 0,
            integrations: (defaults) =>
                defaults.filter((integration) => integration.name !== "BrowserTracing" && integration.name !== "Replay"),
            beforeSend(event) {
                if (event.request) {
                    delete event.request.headers;
                    delete event.request.cookies;
                }
                return event;
            },
        });

        initialized = true;
        scLog(`[CrashReporter] Sentry initialized (release=${__CIVIC_MOD_VERSION__}, env=${__CIVIC_DEVTOOLS__ ? "development" : "production"})`);
        flushEarlyReports();
    } catch (error) {
        scLog(`[CrashReporter] Sentry init failed: ${String(error)}`);
    }
}

/**
 * Disable crash reporting after opt-out and drop any reports captured before
 * consent was known. Idempotent and safe during startup.
 */
export function disableCrashReporter(): void {
    telemetryAllowed = false;
    consentResolved = true;

    const dropped = earlyReports.length + earlyReportOverflow;
    earlyReports.length = 0;
    earlyReportOverflow = 0;

    if (initialized) {
        initialized = false;
        void Sentry.close(2000).catch(() => {
            // Reporter shutdown must never surface into the UI.
        });
        scLog(`[CrashReporter] Sentry disabled; dropped ${dropped} queued report(s)`);
        return;
    }

    if (dropped > 0) {
        scLog(`[CrashReporter] Telemetry disabled; dropped ${dropped} queued report(s)`);
    }
}

/**
 * Report an error to the crash reporter. No-op if Sentry is not initialized
 * (telemetry disabled or init failed). Always safe to call.
 */
export function reportError(error: unknown, context?: Record<string, unknown>): void {
    if (!initialized) {
        if (!consentResolved) {
            queueEarlyReport(error, context);
        }
        return;
    }

    if (!telemetryAllowed) return;

    try {
        Sentry.captureException(error, context ? { extra: context } : undefined);
    } catch {
        // Never let the crash reporter itself crash the host.
    }
}

/**
 * For tests / dev introspection.
 */
export function isCrashReporterInitialized(): boolean {
    return initialized;
}

function queueEarlyReport(error: unknown, context?: Record<string, unknown>): void {
    if (earlyReports.length >= MAX_EARLY_REPORTS) {
        earlyReports.shift();
        earlyReportOverflow++;
    }

    if (context === undefined) {
        earlyReports.push({ error });
    } else {
        earlyReports.push({ error, context });
    }
}

function flushEarlyReports(): void {
    if (!initialized || !telemetryAllowed || earlyReports.length === 0) return;

    const reports = earlyReports.splice(0, earlyReports.length);
    const overflow = earlyReportOverflow;
    earlyReportOverflow = 0;

    for (const report of reports) {
        try {
            Sentry.captureException(report.error, report.context ? { extra: report.context } : undefined);
        } catch {
            // Never let the crash reporter itself crash the host.
        }
    }

    scLog(`[CrashReporter] Flushed ${reports.length} queued report(s); ${overflow} older report(s) were dropped by cap`);
}
