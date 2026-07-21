/**
 * Centralized logging utility.
 * Sends messages to C# via trigger for unified logging in CivicSurvival.log
 *
 * Usage:
 *   scLog("message")      - info level
 *   scDebug("message")    - debug only (skipped in Release)
 *   scWarn("message")     - warning level
 *   scError("message")    - error level
 */

import { triggerCivic } from "@hooks/typedTrigger";
import { B } from "../hooks/bindingNames.generated";

/**
 * Debug mode flag.
 * false = production (all CS2 mod builds).
 * Set to true temporarily for local debugging, then revert.
 */
const DEBUG = false;

/**
 * Log a message to CivicSurvival.log via C# bridge.
 * Safe to call even if trigger fails (during initialization).
 */
export const scLog = (msg: string): void => {
    try {
        triggerCivic(B.JsLog, msg);
    } catch {
        // Ignore if trigger fails during early initialization
    }
};

/**
 * Debug-only log. Skipped when DEBUG = false.
 */
export const scDebug = (msg: string): void => {
    if (DEBUG) scLog(msg);
};

/**
 * Warning level log.
 */
export const scWarn = (msg: string): void => {
    scLog(`WARN: ${msg}`);
};

/**
 * Error level log.
 */
export const scError = (msg: string): void => {
    scLog(`ERROR: ${msg}`);
};
