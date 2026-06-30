/**
 * Error Boundary component for catching React render errors
 */

import React, { Component, type ErrorInfo, type ReactNode } from "react";
import { Z_INDEX, getCurrentTheme } from "../themes";
import { formatCaughtError } from "../utils/errorFormat";
import { scLog } from "../utils/logging";
import { reportError } from "../services/crashReporter";

interface ErrorBoundaryState {
    hasError: boolean;
    error: string;
}

interface ErrorBoundaryProps {
    children: ReactNode;
    name: string;
    resetKey: string | number | null;
    variant?: "inline" | "modal";
    onRecover?: () => void;
    recoverLabel?: string;
}

function getErrorStyles(): Record<string, React.CSSProperties> {
    const theme = getCurrentTheme();

    return {
        errorContainer: {
            color: theme.colors.error,
            padding: "8rem",
            backgroundColor: theme.colors.paper,
        },
        retryButton: {
            marginTop: "8rem",
            padding: "4rem 12rem",
            fontSize: "12rem",
            backgroundColor: theme.colors.paperHover,
            color: theme.colors.textSecondary,
            border: `1rem solid ${theme.colors.border}`,
            borderRadius: theme.layout.borderRadiusLg,
            cursor: "pointer",
        },
        buttonRow: {
            display: "flex",
            flexWrap: "wrap",
        },
        recoveryButton: {
            marginLeft: "8rem",
        },
        modalOverlay: {
            position: "fixed",
            top: "16rem",
            right: "16rem",
            zIndex: Z_INDEX.critical,
            display: "flex",
            alignItems: "flex-start",
            justifyContent: "flex-end",
            backgroundColor: "transparent",
            pointerEvents: "auto",
        },
        modalPanel: {
            width: "420rem",
            maxWidth: "90%",
            minHeight: "120rem",
            padding: "18rem",
            backgroundColor: theme.colors.paper,
            border: `2rem solid ${theme.colors.error}`,
            borderRadius: "6rem",
            boxShadow: theme.effects.shadowLg,
            color: theme.colors.error,
            pointerEvents: "auto",
        },
        modalTitle: {
            fontSize: "15rem",
            fontWeight: 700,
            marginBottom: "8rem",
            textTransform: "uppercase",
        },
        modalMessage: {
            color: theme.colors.textSecondary,
            fontSize: "12rem",
            lineHeight: 1.4,
            marginBottom: "8rem",
        },
        modalMeta: {
            color: theme.colors.neutral,
            fontSize: "10rem",
            marginBottom: "10rem",
        },
    };
}

export class ErrorBoundary extends Component<ErrorBoundaryProps, ErrorBoundaryState> {
    constructor(props: ErrorBoundaryProps) {
        super(props);
        this.state = { hasError: false, error: "" };
    }

    static getDerivedStateFromError(error: unknown): ErrorBoundaryState {
        return { hasError: true, error: formatCaughtError(error).message };
    }

    componentDidCatch(error: unknown, errorInfo: ErrorInfo): void {
        const formatted = formatCaughtError(error);
        const tag = this.props.name ? `[${this.props.name}]` : "";
        scLog(`[ERROR BOUNDARY]${tag} ========== CAUGHT ERROR ==========`);
        scLog(`[ERROR BOUNDARY]${tag} Message: ${formatted.message}`);
        scLog(`[ERROR BOUNDARY]${tag} Stack: ${formatted.stack}`);
        scLog(`[ERROR BOUNDARY]${tag} Component stack: ${errorInfo.componentStack || "no stack"}`);
        scLog(`[ERROR BOUNDARY]${tag} =====================================`);

        reportError(error, {
            boundary: this.props.name ?? "unnamed",
            variant: this.props.variant ?? "inline",
            componentStack: errorInfo.componentStack ?? "no stack",
        });
    }

    componentDidUpdate(prevProps: ErrorBoundaryProps): void {
        if (prevProps.resetKey !== this.props.resetKey && this.state.hasError) {
            this.setState({ hasError: false, error: "" });
        }
    }

    private handleRetry = (): void => {
        this.setState({ hasError: false, error: "" });
    };

    private handleRecover = (): void => {
        try {
            this.props.onRecover?.();
        } catch (error) {
            const formatted = formatCaughtError(error);
            scLog(`[ERROR BOUNDARY][${this.props.name}] Recovery failed: ${formatted.message}`);
        }
    };

    render(): ReactNode {
        if (this.state.hasError) {
            const errorStyles = getErrorStyles();

            /* eslint-disable civic/no-hardcoded-jsx-text -- class component: hooks unavailable, crash fallback must work without l10n */
            if (this.props.variant === "modal") {
                return (
                    <div style={errorStyles.modalOverlay}>
                        <div style={errorStyles.modalPanel}>
                            <div style={errorStyles.modalTitle}>UI Error</div>
                            <div style={errorStyles.modalMessage}>{this.state.error}</div>
                            {this.props.name && (
                                <div style={errorStyles.modalMeta}>Boundary: {this.props.name}</div>
                            )}
                            <div style={errorStyles.buttonRow}>
                                <button style={errorStyles.retryButton} onClick={this.handleRetry}>
                                    Retry
                                </button>
                                {this.props.onRecover && (
                                    <button style={{ ...errorStyles.retryButton, ...errorStyles.recoveryButton }} onClick={this.handleRecover}>
                                        {this.props.recoverLabel ?? "Dismiss modal"}
                                    </button>
                                )}
                            </div>
                        </div>
                    </div>
                );
            }
            return (
                <div style={errorStyles.errorContainer}>
                    UI Error: {this.state.error}
                    <br />
                    <button style={errorStyles.retryButton} onClick={this.handleRetry}>
                        Retry
                    </button>
                </div>
            );
            /* eslint-enable civic/no-hardcoded-jsx-text */
        }
        return this.props.children;
    }
}
