import React, { useCallback, useState } from "react";
import { Column, Row } from "@coherent";
import { useTheme, useAccents, hexToRgba } from "@themes";
import { useLocale } from "../../../locales";
import { SegmentedTabs } from "../../shared/ui";
import { useNetworkActions } from "../../../hooks/actions";
import { OnlineConsentContent } from "../../scenario/OnlineConsentContent";

export type LeaderboardTabType = "ranks" | "alltime" | "weekly";

interface LeaderboardTabsProps {
    activeTab: LeaderboardTabType;
    onTabChange: (tab: LeaderboardTabType) => void;
    disabled?: boolean;
}

interface LeaderboardPositionFooterProps {
    yourPosition: number | null;
    yourWeeklyPosition: number | null;
}

interface LeaderboardOptInOverlayProps {
    onlineEnabled: boolean;
    onlineConsentRecorded: boolean;
}

export const LeaderboardTabs: React.FC<LeaderboardTabsProps> = ({ activeTab, onTabChange, disabled = false }) => {
    const accents = useAccents();
    const l = useLocale();

    return (
        <Row style={{ marginBottom: "16rem" }}>
            <SegmentedTabs
                options={[
                    { value: "ranks", label: l.t("UI_ARENA_TAB_RANKS") },
                    { value: "alltime", label: l.t("UI_ARENA_TAB_ALLTIME") },
                    { value: "weekly", label: l.t("UI_ARENA_TAB_WEEKLY") },
                ]}
                value={activeTab}
                onChange={onTabChange}
                color={accents.operations.accent}
                disabled={disabled}
            />
        </Row>
    );
};

export const LeaderboardPositionFooter: React.FC<LeaderboardPositionFooterProps> = ({
    yourPosition,
    yourWeeklyPosition,
}) => {
    const theme = useTheme();
    const accents = useAccents();
    const l = useLocale();

    if (yourPosition === null && yourWeeklyPosition === null) return null;

    const footerStyle: React.CSSProperties = {
        marginTop: "12rem",
        padding: "12rem 16rem",
        background: hexToRgba(accents.operations.accent, 0.08),
        border: `2rem solid ${hexToRgba(accents.operations.accent, 0.25)}`,
        borderRadius: theme.layout.borderRadius,
    };

    const labelStyle: React.CSSProperties = {
        fontSize: "10rem",
        color: theme.colors.textMuted,
        textTransform: "uppercase" as const,
    };

    const valueStyle: React.CSSProperties = {
        fontSize: "14rem",
        fontWeight: 700,
        color: accents.operations.accent,
        fontFamily: theme.typography.fontFamilyMono,
    };

    return (
        <Row justify="space-around" style={footerStyle}>
            {yourPosition !== null && (
                <Column align="center">
                    <span style={labelStyle}>{l.t("UI_ARENA_YOUR_ALLTIME")}</span>
                    <span style={valueStyle}>#{yourPosition}</span>
                </Column>
            )}
            {yourWeeklyPosition !== null && (
                <Column align="center">
                    <span style={labelStyle}>{l.t("UI_ARENA_THIS_WEEK")}</span>
                    <span style={valueStyle}>#{yourWeeklyPosition}</span>
                </Column>
            )}
        </Row>
    );
};

export const LeaderboardOptInOverlay: React.FC<LeaderboardOptInOverlayProps> = ({ onlineEnabled, onlineConsentRecorded }) => {
    const theme = useTheme();
    const accents = useAccents();
    const l = useLocale();
    const networkActions = useNetworkActions();
    // Reuses the SAME shared consent prompt (OnlineConsentContent) and the SAME first-enable
    // gate (onlineConsentRecorded) as SettingsPanel — not a second consent mechanism. On a
    // first enable the prompt is shown here; once a decision is recorded, the button toggles
    // Online directly, matching SettingsPanel.handleGlobalConnectionToggle.
    const [showConsent, setShowConsent] = useState(false);
    // Local latch mirroring SettingsPanel: closes the sub-frame window between a consent
    // decision and the next throttled NewsDto refresh (~500ms). onlineConsentRecorded from
    // the DTO only reflects the C# m_OnlineConsentRecorded latch on the next tick; without
    // this latch a fast re-open inside that window would re-show the prompt and a second
    // click would re-send toggleGlobalConnection + setTelemetryEnabled, overwriting the
    // just-made diagnostics choice. Never resets once true (record-of-decision).
    const [consentDecidedLocally, setConsentDecidedLocally] = useState(false);
    const consentRecorded = onlineConsentRecorded || consentDecidedLocally;

    const handleEnable = useCallback(() => {
        if (!consentRecorded) {
            setShowConsent(true);
            return;
        }
        networkActions.toggleGlobalConnection(true);
    }, [networkActions, consentRecorded]);

    // One Continue button decides Online + diagnostics (no Cancel: "Go online" off is
    // the offline choice). Any decision records consent globally; latch locally too so a
    // fast re-toggle inside the DTO-refresh window does not re-prompt. Diagnostics is
    // forced false when offline.
    const handleConsentConfirm = useCallback((goOnline: boolean, diagnostics: boolean) => {
        setShowConsent(false);
        setConsentDecidedLocally(true);
        networkActions.toggleGlobalConnection(goOnline);
        networkActions.setTelemetryEnabled(goOnline && diagnostics);
    }, [networkActions]);

    if (onlineEnabled) return null;

    const overlayStyle: React.CSSProperties = {
        position: "absolute",
        top: 0,
        left: 0,
        right: 0,
        bottom: 0,
        display: "flex",
        flexDirection: "column",
        minHeight: "100%",
        alignItems: "center",
        justifyContent: "center",
        background: hexToRgba(theme.colors.background, 0.7),
        zIndex: 10,
        padding: "16rem",
    };

    if (showConsent) {
        return (
            <div style={overlayStyle}>
                <div style={{
                    width: "100%",
                    maxWidth: "360rem",
                    maxHeight: "100%",
                    overflowY: "auto",
                }}>
                    <OnlineConsentContent onConfirm={handleConsentConfirm} />
                </div>
            </div>
        );
    }

    const badgeStyle: React.CSSProperties = {
        background: accents.operations.accent,
        color: theme.colors.background,
        padding: "8rem 24rem",
        borderRadius: theme.layout.borderRadius,
        fontSize: "12rem",
        fontWeight: 700,
        textTransform: "uppercase",
        letterSpacing: "0.5rem",
        marginBottom: "12rem",
    };

    const promptTitleStyle: React.CSSProperties = {
        fontSize: "18rem",
        fontWeight: 700,
        color: theme.colors.textPrimary,
        textAlign: "center",
        marginBottom: "8rem",
    };

    const promptSubtitleStyle: React.CSSProperties = {
        fontSize: "12rem",
        color: theme.colors.textSecondary,
        textAlign: "center",
        marginBottom: "16rem",
        maxWidth: "280rem",
    };

    const enableButtonStyle: React.CSSProperties = {
        padding: "12rem 32rem",
        background: accents.operations.accent,
        border: "none",
        borderRadius: theme.layout.borderRadius,
        fontSize: "12rem",
        fontWeight: 700,
        color: theme.colors.background,
        textTransform: "uppercase",
        cursor: "pointer",
    };

    return (
        <div style={overlayStyle}>
            <div style={badgeStyle}>{l.t("UI_ARENA_GLOBAL_GRID")}</div>
            <div style={promptTitleStyle}>{l.t("UI_ARENA_JOIN_RANKINGS")}</div>
            <div style={promptSubtitleStyle}>
                {l.t("UI_ARENA_ENABLE_PROMPT")}
            </div>
            <button
                style={enableButtonStyle}
                onClick={handleEnable}
            >
                {l.t("UI_ARENA_ENABLE_BUTTON")}
            </button>
        </div>
    );
};
