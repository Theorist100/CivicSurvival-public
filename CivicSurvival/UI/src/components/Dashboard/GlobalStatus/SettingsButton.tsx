import React, { memo } from "react";
import { useTheme } from "../../../themes";
import { IconGear } from "../../shared/common/Icons";
import { toggleSettingsWindow, useSettingsWindowOpen } from "../../../hooks/useSettingsWindow";

// Opens the settings window. The window itself is NOT mounted here — it is its own
// module-registry root beside the Dashboard (see components/settings/SettingsWindow), so it
// is free of this bar's clipping without needing a portal out of the game UI layer. The two
// trees share open state through the useSettingsWindow store.
export const SettingsButton: React.FC = memo(() => {
    const theme = useTheme();
    const isOpen = useSettingsWindowOpen();

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
            <button style={buttonStyle} onClick={(e) => { e.stopPropagation(); toggleSettingsWindow(); }}>
                <IconGear />
            </button>
        </div>
    );
});
SettingsButton.displayName = "SettingsButton";
