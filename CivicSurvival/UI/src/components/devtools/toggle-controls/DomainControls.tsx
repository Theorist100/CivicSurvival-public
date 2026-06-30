import React from "react";
import { useLocale } from "../../../locales";
import { HoverTipTarget } from "../../shared/common/HoverTip";

interface ToggleDisplayEntry {
    key: string;
    name: string;
    count: number;
    color: string;
}

interface DomainToggleProps {
    domain: ToggleDisplayEntry;
    isOn: boolean;
    indent?: boolean;
    subToggles?: Array<{ key: string; name: string }> | undefined;
    subStates: Record<string, boolean>;
    canDisable?: boolean;
    lockedReasonId?: string;
    onToggleDomain: (key: string) => void;
    onToggleSub: (key: string) => void;
}

interface ThreatsHeaderProps {
    allThreatsOn: boolean;
    totalCount: number;
    onCount: number;
    domainCount: number;
    onToggle: () => void;
}

interface ToggleGridButtonProps {
    domain: ToggleDisplayEntry;
    isOn: boolean;
    canDisable?: boolean;
    lockedReasonId?: string;
    onClick: () => void;
}

export const ThreatsHeader: React.FC<ThreatsHeaderProps> = ({ allThreatsOn, totalCount, onCount, domainCount, onToggle }) => (
    <button
        onClick={onToggle}
        style={{
            display: "flex",
            justifyContent: "space-between",
            alignItems: "center",
            width: "100%",
            padding: "8rem 10rem",
            fontSize: "12rem",
            backgroundColor: allThreatsOn ? "rgba(255,68,68,0.1)" : "rgba(255,0,0,0.2)",
            color: allThreatsOn ? "#ff4444" : "#888",
            border: `2rem solid ${allThreatsOn ? "#ff4444" : "#555"}`,
            borderRadius: "4rem",
            cursor: "pointer",
            pointerEvents: "auto" as const,
            marginBottom: "4rem",
            textDecoration: allThreatsOn ? "none" : "line-through",
            fontWeight: "bold" as const,
        }}
    >
        <span>Threats</span>
        <span style={{ fontSize: "10rem", opacity: 0.6, fontWeight: "normal" }}>
            {totalCount} sys &middot; {onCount}/{domainCount}
        </span>
    </button>
);

export const DomainToggle: React.FC<DomainToggleProps> = ({
    domain,
    isOn,
    indent = false,
    subToggles,
    subStates,
    canDisable = true,
    lockedReasonId = "",
    onToggleDomain,
    onToggleSub,
}) => {
    const l = useLocale();
    const disabled = !canDisable;
    const lockText = lockedReasonId ? l.tDynamic(lockedReasonId) : "";
    return (
        <React.Fragment>
            <HoverTipTarget text={lockText || null}>
                <button
                    onClick={() => onToggleDomain(domain.key)}
                    disabled={disabled}
                    style={{
                        display: "flex",
                        justifyContent: "space-between",
                        alignItems: "center",
                        padding: "6rem 10rem",
                        fontSize: "11rem",
                        backgroundColor: isOn ? "transparent" : "rgba(255,0,0,0.1)",
                        color: isOn ? domain.color : "#666",
                        border: `1rem solid ${isOn ? domain.color : "#444"}`,
                        borderRadius: "3rem",
                        cursor: disabled ? "not-allowed" : "pointer",
                        pointerEvents: "auto" as const,
                        marginBottom: "3rem",
                        marginLeft: indent ? "12rem" : "0rem",
                        textDecoration: isOn ? "none" : "line-through",
                    }}
                >
                    <span style={{ fontWeight: "bold" }}>{domain.name}</span>
                    <span style={{ fontSize: "10rem", opacity: 0.6 }}>
                        {domain.count} sys &middot; {disabled ? "LOCKED" : isOn ? "ON" : "OFF"}
                    </span>
                </button>
            </HoverTipTarget>
            {subToggles && (
                <div style={{ display: "flex", flexWrap: "wrap", marginBottom: "4rem", marginLeft: indent ? "22rem" : "10rem" }}>
                    {subToggles.map(subToggle => {
                        const subOn = subStates[subToggle.key];
                        return (
                            <button key={subToggle.key} onClick={() => onToggleSub(subToggle.key)}
                                style={{
                                    padding: "3rem 8rem",
                                    fontSize: "10rem",
                                    backgroundColor: subOn ? "transparent" : "rgba(255,0,0,0.15)",
                                    color: subOn ? domain.color : "#666",
                                    border: `1rem solid ${subOn ? domain.color : "#444"}`,
                                    borderRadius: "2rem",
                                    cursor: "pointer",
                                    pointerEvents: "auto" as const,
                                    textDecoration: subOn ? "none" : "line-through",
                                    opacity: 0.8,
                                    marginRight: "3rem",
                                    marginBottom: "3rem",
                                }}>
                                {subToggle.name}
                            </button>
                        );
                    })}
                </div>
            )}
        </React.Fragment>
    );
};

export const ToggleGridButton: React.FC<ToggleGridButtonProps> = ({ domain, isOn, canDisable = true, lockedReasonId = "", onClick }) => {
    const l = useLocale();
    const disabled = !canDisable;
    const lockText = lockedReasonId ? l.tDynamic(lockedReasonId) : "";
    return (
        <HoverTipTarget key={domain.key} text={lockText || null}>
            <button onClick={onClick}
                disabled={disabled}
                style={{
                    display: "flex",
                    justifyContent: "space-between",
                    alignItems: "center",
                    padding: "5rem 8rem",
                    fontSize: "11rem",
                    backgroundColor: isOn ? "transparent" : "rgba(255,0,0,0.1)",
                    color: isOn ? domain.color : "#666",
                    border: `1rem solid ${isOn ? domain.color : "#444"}`,
                    borderRadius: "3rem",
                    cursor: disabled ? "not-allowed" : "pointer",
                    pointerEvents: "auto" as const,
                    textDecoration: isOn ? "none" : "line-through",
                }}>
                <span style={{ fontWeight: "bold" }}>{domain.name}</span>
                <span style={{ fontSize: "10rem", opacity: 0.6 }}>{disabled ? "LOCK" : domain.count}</span>
            </button>
        </HoverTipTarget>
    );
};

export const SectionLabel: React.FC<{ text: string; color: string }> = ({ text, color }) => (
    <div style={{ fontSize: "10rem", color, marginBottom: "4rem", fontWeight: "bold", letterSpacing: "1rem" }}>
        {text}
    </div>
);
