import React from "react";
import { IconChevronLeft } from "../../shared/common/Icons";

interface PassportHeaderProps {
    headerStyle: React.CSSProperties;
    backButtonStyle: React.CSSProperties;
    titleStyle: React.CSSProperties;
    backLabel: string;
    title: string;
    onBack: () => void;
}

export const PassportHeader: React.FC<PassportHeaderProps> = ({
    headerStyle,
    backButtonStyle,
    titleStyle,
    backLabel,
    title,
    onBack,
}) => (
    <div style={headerStyle}>
        <button style={backButtonStyle} onClick={onBack}>
            <IconChevronLeft />
            <span style={{ marginLeft: "4rem" }}>{backLabel}</span>
        </button>
        <span style={titleStyle}>{title}</span>
    </div>
);
