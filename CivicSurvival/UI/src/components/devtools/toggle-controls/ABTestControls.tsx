import React from "react";

interface ABTestControlsProps {
    abRunning: string;
    abProgress: string;
    cancelArmed: boolean;
    onStartAB: (key: string) => void;
}

export const ABTestControls: React.FC<ABTestControlsProps> = ({
    abRunning,
    abProgress,
    cancelArmed,
    onStartAB,
}) => (
    <div style={{ display: "flex", marginBottom: "4rem" }}>
        {["ab:allThreats", "ab:airDefense"].map((key, i) => {
            const label = key === "ab:allThreats" ? "A/B Threats" : "A/B AirDef";
            const running = abRunning === key;
            const armed = running && cancelArmed;
            const anyRunning = abRunning.length > 0;
            return (
                <button key={key} onClick={() => onStartAB(key)}
                    disabled={anyRunning && !running}
                    style={{
                        flex: 1,
                        padding: "6rem 0",
                        fontSize: "11rem",
                        marginRight: i === 0 ? "6rem" : "0rem",
                        backgroundColor: armed ? "#882200" : running ? "#886600" : (anyRunning ? "#333" : "#444"),
                        color: armed ? "#ff4444" : running ? "#ffcc00" : (anyRunning ? "#555" : "#aaa"),
                        border: `1rem solid ${armed ? "#ff4444" : running ? "#ffcc00" : "#666"}`,
                        borderRadius: "4rem",
                        cursor: anyRunning && !running ? "default" : "pointer",
                        pointerEvents: "auto" as const,
                        fontWeight: "bold" as const,
                    }}>
                    {armed ? "CONFIRM STOP?" : running ? abProgress || "STOP" : label}
                </button>
            );
        })}
    </div>
);

export const SharedOverheadABButton: React.FC<ABTestControlsProps> = ({
    abRunning,
    abProgress,
    cancelArmed,
    onStartAB,
}) => {
    const key = "ab:s:all";
    const running = abRunning === key;
    const armed = running && cancelArmed;
    const anyRunning = abRunning.length > 0;

    return (
        <div style={{ display: "flex", marginBottom: "8rem" }}>
            <button onClick={() => onStartAB(key)}
                disabled={anyRunning && !running}
                style={{
                    flex: 1,
                    padding: "6rem 0",
                    fontSize: "11rem",
                    backgroundColor: armed ? "#882200" : running ? "#886600" : (anyRunning ? "#333" : "#553300"),
                    color: armed ? "#ff4444" : running ? "#ffcc00" : (anyRunning ? "#555" : "#ffaa44"),
                    border: `1rem solid ${armed ? "#ff4444" : running ? "#ffcc00" : "#885500"}`,
                    borderRadius: "4rem",
                    cursor: anyRunning && !running ? "default" : "pointer",
                    pointerEvents: "auto" as const,
                    fontWeight: "bold" as const,
                }}>
                {armed ? "CONFIRM STOP?" : running ? abProgress || "STOP" : "A/B Shared Overhead (4 tests)"}
            </button>
        </div>
    );
};
