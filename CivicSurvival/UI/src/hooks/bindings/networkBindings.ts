/**
 * Network domain bindings - Global News & Telemetry
 * Binds to GlobalNewsUIPanel.cs
 */

import { triggerCivic } from "@hooks/typedTrigger";
import { bindValue } from "cs2/api";
import { B } from "../bindingNames.generated";

// Effective diagnostics = Online && opt-in, computed C#-side (single source). Sentry
// reads THIS so the in-game crash reporter is off whenever Online is off, matching the
// server gate (TelemetryConfig.Enabled) with no UI-side recombination of two flags.
export const effectiveDiagnostics$ = bindValue<boolean>(B.Group, B.EffectiveDiagnostics, false);

// Triggers
export function toggleGlobalConnection(enable: boolean): void {
    triggerCivic(B.ToggleGlobalConnection, enable);
}

export function setPlayerNickname(nickname: string): void {
    triggerCivic(B.SetPlayerNickname, nickname);
}
