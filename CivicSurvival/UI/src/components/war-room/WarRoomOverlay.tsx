/**
 * WarRoomOverlay — the fullscreen War Room command surface.
 *
 * Its own module-registry root (mounted beside the Dashboard). Open state lives in the
 * shared useWarRoomOverlay store so the RADAR-tab expand button (inside the Dashboard root)
 * can raise it. Gated by the GridWarfare beta wave — the same gate that shows the button —
 * so a closed wave never lets it mount. Closes on ESC or the header/close affordances.
 *
 * Fixed, above the Dashboard (Z_INDEX.modal) with pointer-events enabled so clicks land on
 * the console rather than the game beneath (cohtml root defaults to pointer-events: none).
 */

import React, { useEffect, useMemo } from "react";
import { useTheme, Z_INDEX } from "../../themes";
import { useBetaWave } from "../../hooks/useBetaWave";
import { closeWarRoom, useWarRoomOpen } from "../../hooks/useWarRoomOverlay";
import { WarRoomContent } from "../Dashboard/ContentPanel/views/war-room/WarRoomContent";
import { scLog } from "../../utils/logging";

export const WarRoomOverlay: React.FC = () => {
    const theme = useTheme();
    const open = useWarRoomOpen();
    const gate = useBetaWave("GridWarfare");

    // [PanelDiag] crash-triage trail (see Dashboard.tsx counterpart): the War Room
    // hosts the height-fit radar SVG — record its mount window in the mod log.
    useEffect(() => {
        scLog(`[PanelDiag] warRoom open=${open && !gate.isLocked}`);
    }, [open, gate.isLocked]);

    // ESC closes while open — listener only mounted for the open window.
    useEffect(() => {
        if (!open) return;
        const onKeyDown = (e: KeyboardEvent) => {
            if (e.key === "Escape") closeWarRoom();
        };
        window.addEventListener("keydown", onKeyDown);
        return () => window.removeEventListener("keydown", onKeyDown);
    }, [open]);

    const overlayStyle = useMemo((): React.CSSProperties => ({
        position: "fixed" as const,
        top: 0,
        left: 0,
        right: 0,
        bottom: 0,
        display: "flex",
        flexDirection: "column" as const,
        minHeight: 0,
        background: theme.colors.background,
        zIndex: Z_INDEX.modal,
        pointerEvents: "auto" as const,
    }), [theme.colors.background]);

    if (gate.isLocked || !open) return null;

    return (
        <div style={overlayStyle}>
            <WarRoomContent onClose={closeWarRoom} />
        </div>
    );
};

WarRoomOverlay.displayName = "WarRoomOverlay";
