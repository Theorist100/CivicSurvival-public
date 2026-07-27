/**
 * OnlineConsentModal — the standalone GLOBAL GRID agreement shown once on first game
 * load (city / town / village / new game), decoupled from the narrative cold-open.
 *
 * Calm "agreement" styling (green accent, globe icon — no crisis-red alarm). The body
 * is the shared OnlineConsentContent block (two toggles + one Continue button). Confirm
 * commits the choice via the network actions; the C# OnlineConsentGateSystem releases
 * the modal slot when the resulting OnlineConnectionStateChangedEvent fires, so a queued
 * Intro cold-open (Town/City) then takes over. No dismiss binding here on purpose.
 */

import React, { useEffect, useMemo, useRef } from "react";
import { Z_INDEX, createBaseModalStyles, useModalPalette } from "../../themes";
import { defineModal } from "../shared/modal";
import { IconGlobe } from "../shared/common/Icons";
import { useNetworkActions } from "../../hooks/actions";
import { scLog } from "../../utils/logging";
import { OnlineConsentContent } from "./OnlineConsentContent";

const OnlineConsentModalView: React.FC = () => {
    const m = useModalPalette();
    const networkActions = useNetworkActions();

    const base = useMemo(() => createBaseModalStyles(m, {
        accentColor: m.grid.greenBorder,
        overlayOpacity: 0.95,
        width: "480rem",
        zIndex: Z_INDEX.modal,
    }), [m]);

    const iconStyle = useMemo(() => ({
        fontSize: "40rem",
        marginBottom: "8rem",
        color: m.grid.green,
    } as React.CSSProperties), [m]);

    const rootRef = useRef<HTMLDivElement>(null);

    const handleConfirm = (goOnline: boolean, diagnostics: boolean): void => {
        // toggleGlobalConnection records the consent decision globally (on OR off) and
        // makes GlobalNewsSystem publish OnlineConnectionStateChangedEvent, which the C#
        // gate listens for to dismiss this modal. Diagnostics is already gated to
        // (goOnline && diagnostics) by the shared block.
        networkActions.toggleGlobalConnection(goOnline);
        networkActions.setTelemetryEnabled(diagnostics);
    };

    // The modal appears at game load while input focus is on the game, so a
    // document-level keydown never sees Escape. Focus the modal root on mount so the
    // element-level onKeyDown below actually receives keys.
    useEffect(() => {
        rootRef.current?.focus();
    }, []);

    // Esc = play offline. The agreement must never be a dead end: Escape records the
    // offline decision (toggle off, no diagnostics) exactly like the offline path of
    // Continue, the C# gate dismisses the modal, and the cold-open proceeds.
    const handleKeyDown = (e: React.KeyboardEvent): void => {
        if (e.key !== "Escape") return;
        e.preventDefault();
        scLog("OnlineConsentModal: Escape -> play offline");
        networkActions.toggleGlobalConnection(false);
        networkActions.setTelemetryEnabled(false);
    };

    return (
        <div
            style={base.overlay}
            ref={rootRef}
            tabIndex={-1}
            onKeyDown={handleKeyDown}
        >
            <div style={base.modal}>
                <div style={base.header}>
                    <span style={iconStyle}><IconGlobe /></span>
                </div>
                <div style={base.body}>
                    <OnlineConsentContent onConfirm={handleConfirm} />
                </div>
            </div>
        </div>
    );
};

export const OnlineConsentModalDef = defineModal({
    id: "OnlineConsent",
    render: () => <OnlineConsentModalView />,
});
