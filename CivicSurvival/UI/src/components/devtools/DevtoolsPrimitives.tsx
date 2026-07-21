import React from "react";
import { SectionTitle } from "@shared/ui";

interface DebugSectionTitleProps {
    children: React.ReactNode;
}

export const DebugSectionTitle: React.FC<DebugSectionTitleProps> = ({ children }) => (
    <SectionTitle style={{ fontSize: "11rem", marginBottom: "6rem" }}>
        {children}
    </SectionTitle>
);
