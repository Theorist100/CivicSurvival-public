import React, { memo } from "react";
import { useTheme } from "../../../themes";
import { IconGear } from "../../shared/common/Icons";
import { SettingsPanel } from "./SettingsPanel";
import { useSettings } from "../../../hooks/domain";
import { useSettingsActions } from "../../../hooks/actions";

// Renders the toggle button plus the panel. The panel portals itself to the mod
// root and is wrapped in a fixed overlay (see SettingsPanel) so it stays anchored
// to the viewport and does not drag along with the Dashboard window.
export const SettingsButton: React.FC = memo(() => {
    const settings = useSettings();
    const { togglePanel } = useSettingsActions();
    const isOpen = settings.status === "ready" && settings.data.IsExpanded;
    const theme = useTheme();

    const buttonStyle: React.CSSProperties = {
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        width: "28rem",
        height: "28rem",
        background: isOpen ? theme.colors.paperHover : "transparent",
        border: `2rem solid ${isOpen ? theme.colors.border : "transparent"}`,
        borderRadius: theme.layout.borderRadius,
        cursor: "pointer",
        fontSize: "16rem",
        color: theme.colors.textSecondary,
        position: "relative" as const,
    };

    return (
        <div style={{ position: "relative" as const }} data-no-drag>
            <button style={buttonStyle} onClick={(e) => { e.stopPropagation(); togglePanel(); }}>
                <IconGear />
            </button>
            <SettingsPanel />
        </div>
    );
});
SettingsButton.displayName = "SettingsButton";
