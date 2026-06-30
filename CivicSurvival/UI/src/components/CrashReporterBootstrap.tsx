/**
 * Mounts as a hidden ModRoot child. Reads the C#-computed EFFECTIVE diagnostics
 * binding (Online && opt-in) and initializes or disables Sentry as it changes.
 * Online off ⇒ effective diagnostics false ⇒ Sentry disabled, matching the server
 * gate. Single source: no UI-side recombination of the Online + opt-in flags.
 *
 * No-op render — returns null. Lives in the React tree only to subscribe
 * to the binding.
 */

import { useEffect, type FC } from "react";
import { useBooleanBinding } from "@hooks/useSafeBinding";
import { effectiveDiagnostics$ } from "@hooks/bindings/networkBindings";
import { disableCrashReporter, ensureSentryInitialized } from "../services/crashReporter";

export const CrashReporterBootstrap: FC = () => {
    const effectiveDiagnostics = useBooleanBinding(effectiveDiagnostics$, "EffectiveDiagnostics");

    useEffect(() => {
        if (effectiveDiagnostics.status !== "ready") return;

        if (effectiveDiagnostics.data) {
            ensureSentryInitialized();
        } else {
            disableCrashReporter();
        }
    }, [effectiveDiagnostics]);

    return null;
};
