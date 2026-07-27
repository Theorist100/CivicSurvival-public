import React from "react";
import { ThemeProvider } from "../themes";
import { useNumberBinding } from "../hooks/useSafeBinding";
import { useOptionalBinding } from "../hooks/useOptionalBinding";
import { uiTheme$ } from "../hooks/bindings/coreBindings";
import { isUIThemeId } from "../types/semantic";
import { ErrorBoundary } from "./ErrorBoundary";

interface ModRootProps {
    name: string;
    resetKey?: string | number | null;
    variant?: "inline" | "modal";
    children: React.ReactNode;
}

export const ModRoot: React.FC<ModRootProps> = ({ name, resetKey, variant, children }) => {
    const themeState = useOptionalBinding(useNumberBinding(uiTheme$, `ModRoot:${name}:themeId`));
    const rawThemeId = themeState.status === "available" ? themeState.data : 0;
    const themeId = isUIThemeId(rawThemeId) ? rawThemeId : 0;
    const boundaryProps = variant === undefined
        ? { name, resetKey: resetKey ?? name }
        : { name, resetKey: resetKey ?? name, variant };

    return (
        <ThemeProvider themeId={themeId}>
            <ErrorBoundary {...boundaryProps}>
                {children}
            </ErrorBoundary>
        </ThemeProvider>
    );
};
