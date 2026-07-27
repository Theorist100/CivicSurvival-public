/**
 * ModUpdatedRestartModal — shown when ModInstanceWatchSystem detects that the mod's
 * install folder was deleted under the live process (the Paradox SDK swaps the instance
 * folder on a version update with no regard for a running game).
 *
 * The current session keeps working from memory, so this is a calm heads-up, not a
 * failure notice: save, then fully restart the game. The one danger it must prevent is
 * loading a save WITHOUT restarting — that rebuilds render batches from the deleted
 * path and crashes the game. Deliberately instruction-only wording; the technical
 * facts ride the C# telemetry Error line, not the player's screen.
 *
 * No payload — the detection carries no player-actionable variance.
 */

import React, { useEffect, useMemo } from "react";
import { createBaseModalStyles, useModalPalette } from "../../themes";
import { defineModal } from "../shared/modal";
import { IconAlert } from "../shared/common/Icons";
import { useLocale } from "../../locales";
import { dismissModUpdatedRestart, reportUpdateModalAction } from "../../hooks/bindings/modalCoordinatorBindings";

const handleOk = (): void => {
    reportUpdateModalAction("restart", "dismissed");
    dismissModUpdatedRestart();
};

const ModUpdatedRestartModalView: React.FC = () => {
    const l = useLocale();
    const m = useModalPalette();

    // Telemetry: "shown" on render — the only proof the restart notice reached the
    // player's eyes (the C# show request alone can't tell; after a folder deletion the
    // React layer itself may be dead — the exact case this measures).
    useEffect(() => {
        reportUpdateModalAction("restart", "shown");
    }, []);

    // Info-blue like ModLoadFailure: a "here's what to do" notice, not a crisis alarm —
    // the session the player is looking at is fully functional.
    const base = useMemo(() => createBaseModalStyles(m, {
        accentColor: m.accents.info,
        overlayOpacity: 0.9,
        width: "600rem",
    }), [m]);

    const okButtonStyle = useMemo(
        () => ({ ...base.primaryButton, minWidth: "220rem", textAlign: "center" as const }),
        [base]);

    return (
        <div style={base.overlay}>
            <div style={base.modal}>
                <div style={base.header}>
                    <span style={base.headerIcon}><IconAlert /></span>
                    <h2 style={base.title}>{l.t("MODAL_MOD_UPDATED_RESTART_TITLE")}</h2>
                </div>

                <div style={base.body}>
                    {/* pre-line: the locale string carries \n paragraph breaks shared with
                        the vanilla-dialog channel — without it React collapses them to spaces. */}
                    <p style={{ ...base.text, whiteSpace: "pre-line" }}>{l.t("MODAL_MOD_UPDATED_RESTART_TEXT")}</p>

                    <div style={base.buttonContainer}>
                        <button style={okButtonStyle} onClick={handleOk}>
                            {l.t("MODAL_MOD_UPDATED_RESTART_OK")}
                        </button>
                    </div>
                </div>
            </div>
        </div>
    );
};

export const ModUpdatedRestartModalDef = defineModal({
    id: "ModUpdatedRestart",
    render: () => <ModUpdatedRestartModalView />,
});
