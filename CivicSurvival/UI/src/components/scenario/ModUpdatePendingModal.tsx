/**
 * ModUpdatePendingModal — pre-publish heads-up from UpdateAnnounceSystem: a Civic Survival
 * update goes live in ~N minutes (the release workflow sets the announce on the server
 * before publishing). Save now, restart after the update lands.
 *
 * Shown once per announced publish time; the persistent countdown lives in the dashboard
 * tab-bar badge (DomainTabs), this modal only opens the window. Payload from C#:
 * { Active: true, PublishAtUnix: number }.
 */

import React, { useEffect, useMemo } from "react";
import { createBaseModalStyles, useModalPalette } from "../../themes";
import { defineModal } from "../shared/modal";
import { IconAlert } from "../shared/common/Icons";
import { useLocale } from "../../locales";
import { dismissModUpdatePending } from "../../hooks/bindings/updateAnnounceBindings";
import { reportUpdateModalAction } from "../../hooks/bindings/modalCoordinatorBindings";

const minutesFromPayload = (payload: unknown): number => {
    if (typeof payload !== "object" || payload === null) return 0;
    const at = (payload as { PublishAtUnix?: unknown }).PublishAtUnix;
    if (typeof at !== "number" || at <= 0) return 0;
    return Math.max(0, Math.ceil((at * 1000 - Date.now()) / 60000));
};

const handleOk = (): void => {
    reportUpdateModalAction("pending", "dismissed");
    dismissModUpdatePending();
};

const ModUpdatePendingModalView: React.FC<{ payload: unknown }> = ({ payload }) => {
    const l = useLocale();
    const m = useModalPalette();

    const base = useMemo(() => createBaseModalStyles(m, {
        accentColor: m.accents.info,
        overlayOpacity: 0.9,
        width: "600rem",
    }), [m]);

    const okButtonStyle = useMemo(
        () => ({ ...base.primaryButton, minWidth: "220rem", textAlign: "center" as const }),
        [base]);

    const minutes = minutesFromPayload(payload);

    // Telemetry: "shown" on render — closes the update-safety experiment's blind spot
    // (the C# announce poll can't tell whether the countdown notice actually rendered).
    useEffect(() => {
        reportUpdateModalAction("pending", "shown");
    }, []);

    return (
        <div style={base.overlay}>
            <div style={base.modal}>
                <div style={base.header}>
                    <span style={base.headerIcon}><IconAlert /></span>
                    <h2 style={base.title}>{l.t("MODAL_MOD_UPDATE_PENDING_TITLE", String(minutes))}</h2>
                </div>

                <div style={base.body}>
                    <p style={base.text}>{l.t("MODAL_MOD_UPDATE_PENDING_TEXT", String(minutes))}</p>

                    <div style={base.buttonContainer}>
                        <button style={okButtonStyle} onClick={handleOk}>
                            {l.t("MODAL_MOD_UPDATE_PENDING_OK")}
                        </button>
                    </div>
                </div>
            </div>
        </div>
    );
};

export const ModUpdatePendingModalDef = defineModal({
    id: "ModUpdatePending",
    render: (payload) => <ModUpdatePendingModalView payload={payload} />,
});
