import React from "react";

interface ABTestControlsProps {
    abRunning: string;
    abProgress: string;
    cancelArmed: boolean;
    onStartAB: (key: string) => void;
}

// Cognitive rows isolate the psy-pipeline cost (FPS investigation 2026-07-22):
// whole domain vs the MHR resolve chain alone vs the stats aggregator alone.
const AB_LABELS: Record<string, string> = {
    "ab:allThreats": "A/B Threats",
    "ab:airDefense": "A/B AirDef",
    "ab:cognitive": "A/B Cognitive",
    "ab:mhr": "A/B MHR",
    "ab:cogStats": "A/B CogStats",
};

export const ABTestControls: React.FC<ABTestControlsProps> = ({
    abRunning,
    abProgress,
    cancelArmed,
    onStartAB,
}) => (
    <div style={{ display: "flex", flexWrap: "wrap" as const, marginBottom: "4rem" }}>
        {["ab:allThreats", "ab:airDefense", "ab:cognitive", "ab:mhr", "ab:cogStats"].map((key, i, keys) => {
            const label = AB_LABELS[key] ?? key;
            const running = abRunning === key;
            const armed = running && cancelArmed;
            const anyRunning = abRunning.length > 0;
            return (
                <button key={key} onClick={() => onStartAB(key)}
                    disabled={anyRunning && !running}
                    style={{
                        flexBasis: "31%",
                        flexGrow: 1,
                        padding: "6rem 0",
                        fontSize: "11rem",
                        margin: "0 0 2rem 0",
                        marginRight: (i + 1) % 3 === 0 || i === keys.length - 1 ? "0rem" : "4rem",
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

interface SequencePalette { bg: string; fg: string; border: string }

const sequencePalette = (armed: boolean, running: boolean, anyRunning: boolean, accent: boolean): SequencePalette => {
    if (armed) return { bg: "#882200", fg: "#ff4444", border: "#ff4444" };
    if (running) return { bg: "#886600", fg: "#ffcc00", border: "#ffcc00" };
    const border = accent ? "#446688" : "#885500";
    if (anyRunning) return { bg: "#333", fg: "#555", border };
    return accent
        ? { bg: "#334455", fg: "#66aaff", border }
        : { bg: "#553300", fg: "#ffaa44", border };
};

const SequenceABButton: React.FC<ABTestControlsProps & { abKey: string; label: string; accent?: boolean }> = ({
    abRunning,
    abProgress,
    cancelArmed,
    onStartAB,
    abKey,
    label,
    accent,
}) => {
    const running = abRunning === abKey;
    const armed = running && cancelArmed;
    const anyRunning = abRunning.length > 0;
    const palette = sequencePalette(armed, running, anyRunning, accent === true);
    const buttonText = armed ? "CONFIRM STOP?" : running ? abProgress || "STOP" : label;

    return (
        <div style={{ display: "flex", marginBottom: "8rem" }}>
            <button onClick={() => onStartAB(abKey)}
                disabled={anyRunning && !running}
                style={{
                    flex: 1,
                    padding: "6rem 0",
                    fontSize: "11rem",
                    backgroundColor: palette.bg,
                    color: palette.fg,
                    border: `1rem solid ${palette.border}`,
                    borderRadius: "4rem",
                    cursor: anyRunning && !running ? "default" : "pointer",
                    pointerEvents: "auto" as const,
                    fontWeight: "bold" as const,
                }}>
                {buttonText}
            </button>
        </div>
    );
};

export const SharedOverheadABButton: React.FC<ABTestControlsProps> = (props) => (
    <>
        <SequenceABButton {...props} abKey="ab:psy:all" label="A/B Psy Pipeline (3 tests, auto)" accent />
        <SequenceABButton {...props} abKey="ab:s:all" label="A/B Shared Overhead (4 tests)" />
    </>
);
