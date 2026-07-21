import React from "react";

interface MasterControlsProps {
    allOff: boolean;
    allOn: boolean;
    totalOn: number;
    totalCount: number;
    textMuted: string;
    onToggleAll: (enabled: boolean) => void;
}

export const MasterControls: React.FC<MasterControlsProps> = ({
    allOff,
    allOn,
    totalOn,
    totalCount,
    textMuted,
    onToggleAll,
}) => (
    <div style={{ display: "flex", marginBottom: "8rem", alignItems: "center" }}>
        <button onClick={() => onToggleAll(false)}
            style={{
                flex: 1,
                padding: "8rem 0",
                fontSize: "12rem",
                backgroundColor: allOff ? "#882222" : "#cc0000",
                color: "#fff",
                border: "none",
                borderRadius: "4rem",
                cursor: "pointer",
                pointerEvents: "auto" as const,
                fontWeight: "bold" as const,
                marginRight: "6rem",
            }}>
            ALL OFF
        </button>
        <span style={{ fontSize: "10rem", color: textMuted, minWidth: "50rem", textAlign: "center" }}>
            {totalOn}/{totalCount}
        </span>
        <button onClick={() => onToggleAll(true)}
            style={{
                flex: 1,
                padding: "8rem 0",
                fontSize: "12rem",
                backgroundColor: allOn ? "#226622" : "#00aa00",
                color: "#fff",
                border: "none",
                borderRadius: "4rem",
                cursor: "pointer",
                pointerEvents: "auto" as const,
                fontWeight: "bold" as const,
                marginLeft: "6rem",
            }}>
            ALL ON
        </button>
    </div>
);
