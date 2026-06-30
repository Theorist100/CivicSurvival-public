/**
 * ModLoadFailureModal — shown when CivicSurvival's core threat models (.cok) are
 * genuinely absent at game-loading-complete (CivicPrefabInitSystem.FinalizeMissing),
 * so air attacks cannot run and the mod's core gameplay is dead this session.
 *
 * Instead of leaving the player facing a silent war with no attacks, this tells them
 * what happened — with cause-specific wording from the C# disk check (files missing
 * from disk = incomplete download; files present = load failure) — and offers a
 * one-click diagnostic report that reuses the Settings manual-report path.
 *
 * Payload from C#: { Cause: "FilesMissingOnDisk" | "AssetsFailedToLoad", MissingCok }.
 */

import React, { useMemo } from "react";
import { createBaseModalStyles, useModalPalette } from "../../themes";
import { defineModal } from "../shared/modal";
import { IconAlert } from "../shared/common/Icons";
import { useLocale } from "../../locales";
import { useSettingsActions } from "../../hooks/actions";
import { useSettings } from "../../hooks/domain/useSettings";
import { dismissModLoadFailure } from "../../hooks/bindings/modalCoordinatorBindings";

interface ModLoadFailurePayload {
    Cause: string;
    MissingCok: string;
}

const isPayload = (v: unknown): v is ModLoadFailurePayload =>
    typeof v === "object" && v !== null
    && typeof (v as Partial<ModLoadFailurePayload>).Cause === "string";

// Terminal report-status keys (mirror C# SettingsUISystem.OnSendReport result mapping via
// ReasonIds). "Sending" is the in-flight key; the rest are terminal. We disable the send
// button once a send is in flight or has reported a result, and surface the real key text —
// never a hardcoded "sent" that lies when the send no-ops (telemetry off) or fails.
const STATUS_SENDING = "UI_SETTINGS_STATUS_SENDING";
const STATUS_REPORT_SENT = "UI_SETTINGS_STATUS_REPORT_SENT";

const ModLoadFailureModalView: React.FC<{ payload: unknown }> = ({ payload }) => {
    const l = useLocale();
    const m = useModalPalette();
    const { sendReport } = useSettingsActions();
    const settings = useSettings();
    const reportStatusKey = settings.status === "ready" ? settings.data.ReportStatusKey : "";
    // Disable the button only while a send is genuinely in flight or already succeeded. A
    // failed / unavailable / telemetry-disabled result leaves it enabled so the player can
    // retry after fixing the cause, instead of being shown a false "sent" and locked out.
    const sendDisabled = reportStatusKey === STATUS_SENDING || reportStatusKey === STATUS_REPORT_SENT;

    // Calm info-blue, not the crisis-orange alarm: this is a "heads-up, here's what
    // happened and how to fix it" notice, not a danger warning. Softer on the player.
    const base = useMemo(() => createBaseModalStyles(m, {
        accentColor: m.accents.info,
        overlayOpacity: 0.9,
        width: "600rem",
    }), [m]);

    // Button row: wrap so the two action buttons never spill past the modal edge regardless of
    // (localized) label width — they drop to a second line instead of overflowing. Spacing is via
    // child margins, not CSS gap (Coherent UI doesn't support gap — civic/no-css-gap).
    const buttonRow = useMemo(
        () => ({ ...base.buttonContainer, flexWrap: "wrap" as const }),
        [base]);

    // Both action buttons share one box size so the outlined "send" and the filled "continue"
    // read as a matched pair. border-box + a common minWidth equalises width regardless of label
    // length; the send button drops its vertical padding by the 2rem border (12+2 == primary's 14)
    // so the outline doesn't make it 4rem taller. Width/height stay identical, only fill vs outline
    // differs.
    const equalButtonSize = useMemo<React.CSSProperties>(
        () => ({ boxSizing: "border-box", minWidth: "220rem", textAlign: "center" }),
        []);

    // Send button keeps marginRight for the horizontal split and marginBottom so a wrap to the
    // second line still has vertical spacing under it.
    const sendButtonStyle = useMemo(
        () => ({
            ...base.secondaryButton,
            ...base.buttonContainerChild,
            ...equalButtonSize,
            padding: "12rem 32rem",
            marginBottom: "8rem",
        }),
        [base, equalButtonSize]);

    const continueButtonStyle = useMemo(
        () => ({ ...base.primaryButton, ...equalButtonSize }),
        [base, equalButtonSize]);

    const data = isPayload(payload) ? payload : { Cause: "AssetsFailedToLoad", MissingCok: "" };
    const body = data.Cause === "FilesMissingOnDisk"
        ? l.t("MODAL_MOD_LOAD_FAILURE_TEXT_MISSING", data.MissingCok)
        : l.t("MODAL_MOD_LOAD_FAILURE_TEXT_LOADFAIL");

    const handleSend = (): void => {
        sendReport();
    };

    return (
        <div style={base.overlay}>
            <div style={base.modal}>
                <div style={base.header}>
                    <span style={base.headerIcon}><IconAlert /></span>
                    <h2 style={base.title}>{l.t("MODAL_MOD_LOAD_FAILURE_TITLE")}</h2>
                </div>

                <div style={base.body}>
                    <p style={base.text}>{body}</p>
                    {reportStatusKey && <p style={base.text}>{l.tDynamic(reportStatusKey)}</p>}

                    <div style={buttonRow}>
                        <button style={sendButtonStyle} onClick={handleSend} disabled={sendDisabled}>
                            {l.t("MODAL_MOD_LOAD_FAILURE_SEND")}
                        </button>
                        <button style={continueButtonStyle} onClick={dismissModLoadFailure}>
                            {l.t("MODAL_MOD_LOAD_FAILURE_CONTINUE")}
                        </button>
                    </div>
                </div>
            </div>
        </div>
    );
};

export const ModLoadFailureModalDef = defineModal({
    id: "ModLoadFailure",
    render: (payload) => <ModLoadFailureModalView payload={payload} />,
});
