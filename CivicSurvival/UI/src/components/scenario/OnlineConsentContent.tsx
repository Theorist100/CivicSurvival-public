/**
 * OnlineConsentContent — the shared GLOBAL GRID agreement block.
 *
 * Reused by the standalone startup agreement modal (OnlineConsentModal) and the
 * settings first-enable flow. Calm "rules/agreement" styling (green terminal panel,
 * no crisis-red alarm). It contains a short explanation of the functional online
 * features, two toggles, an agreement line + PRIVACY link, and ONE Continue button:
 *
 *  - "Go online" toggle (master, default ON).
 *  - "Share developer diagnostics" toggle (default OFF; disabled while Online is OFF).
 *  - Continue → onConfirm(goOnline, diagnostics). Diagnostics is forced false when
 *    Online is off (effective diagnostics = Online && opt-in).
 *
 * There is no "Cancel": flipping "Go online" off and pressing Continue is the offline
 * choice. The block is dumb — it never dismisses anything; each host owns dismissal
 * (the startup modal via the C# gate, settings via local state).
 */

import React, { useMemo, useState } from "react";
import { useLocale } from "../../locales";
import { useModalPalette, useTheme } from "../../themes";
import { useSettingsActions } from "@hooks/actions";

export interface OnlineConsentContentProps {
    onConfirm: (goOnline: boolean, diagnostics: boolean) => void;
    /** Initial "Go online" state. Defaults to ON (recommended / settings turn-on path). */
    initialGoOnline?: boolean;
}

export const OnlineConsentContent: React.FC<OnlineConsentContentProps> = ({
    onConfirm,
    initialGoOnline = true,
}) => {
    const { openPrivacy } = useSettingsActions();
    const l = useLocale();
    const m = useModalPalette();
    const theme = useTheme();
    const G = m.grid;

    const [goOnline, setGoOnline] = useState(initialGoOnline);
    // Diagnostics default OFF — opt-in, the player turns it on if they want.
    const [diagnostics, setDiagnostics] = useState(false);

    const styles = useMemo(() => ({
        section: {
            marginTop: "14rem",
            padding: "16rem",
            backgroundColor: m.bgDarkGreen,
            border: `1rem solid ${m.borderGreen}`,
            borderRadius: "6rem",
        } as React.CSSProperties,
        title: {
            color: G.greenText,
            fontSize: "14rem",
            fontWeight: "bold" as const,
            letterSpacing: "1rem",
            marginBottom: "8rem",
        } as React.CSSProperties,
        body: {
            color: m.gray888,
            fontSize: "12rem",
            lineHeight: 1.5,
        } as React.CSSProperties,
        featureList: {
            margin: "8rem 0 12rem 0",
            padding: "0rem",
            listStyle: "none" as const,
        } as React.CSSProperties,
        featureItem: {
            display: "flex",
            alignItems: "center",
            marginBottom: "4rem",
            color: G.itemText,
            fontSize: "13rem",
        } as React.CSSProperties,
        featureBullet: {
            width: "6rem",
            height: "6rem",
            borderRadius: "50%",
            backgroundColor: G.greenBullet,
            marginRight: "10rem",
            flexShrink: 0,
        } as React.CSSProperties,
        toggleRow: {
            display: "flex",
            alignItems: "center",
            justifyContent: "space-between",
            marginTop: "10rem",
        } as React.CSSProperties,
        toggleLabel: {
            display: "flex",
            alignItems: "center",
        } as React.CSSProperties,
        toggleTitle: {
            color: G.greenText,
            fontSize: "13rem",
            fontWeight: "bold" as const,
            letterSpacing: "1rem",
        } as React.CSSProperties,
        diagnosticsLine: {
            color: m.gray888,
            fontSize: "11rem",
            marginTop: "6rem",
            marginBottom: "4rem",
            lineHeight: 1.5,
        } as React.CSSProperties,
        agreeLine: {
            color: m.gray888,
            fontSize: "11rem",
            marginTop: "12rem",
            lineHeight: 1.5,
        } as React.CSSProperties,
        privacyLink: {
            color: G.greenText,
            fontSize: "11rem",
            // Buttons don't inherit font-family — set it explicitly so the link matches
            // the terminal mono body instead of falling back to the UA sans-serif.
            fontFamily: theme.typography.fontFamilyMono,
            marginTop: "6rem",
            textDecoration: "underline",
            cursor: "pointer",
            background: "transparent",
            border: "none",
            padding: 0,
            display: "block",
        } as React.CSSProperties,
        continueButton: {
            marginTop: "16rem",
            padding: "12rem 18rem",
            width: "100%",
            backgroundColor: G.greenDark,
            border: `2rem solid ${G.greenBorder}`,
            borderRadius: "6rem",
            color: G.greenBright,
            fontSize: "14rem",
            fontWeight: "bold" as const,
            fontFamily: theme.typography.fontFamilyMono,
            letterSpacing: "1rem",
            cursor: "pointer",
            pointerEvents: "auto" as const,
        } as React.CSSProperties,
    }), [m, G, theme.typography.fontFamilyMono]);

    // Toggle visuals (depend on palette + enabled/disabled state).
    const toggleDot = (enabled: boolean): React.CSSProperties => ({
        width: "8rem",
        height: "8rem",
        borderRadius: "50%",
        backgroundColor: enabled ? G.green : m.gray555,
        marginRight: "12rem",
        boxShadow: enabled ? `0 0 6rem ${G.green}` : "none",
    });
    const toggleTrack = (enabled: boolean, disabled: boolean): React.CSSProperties => ({
        width: "44rem",
        height: "22rem",
        borderRadius: "11rem",
        backgroundColor: enabled ? G.greenDark : m.borderDark,
        border: `1rem solid ${enabled ? G.greenBorder : m.gray555}`,
        position: "relative" as const,
        cursor: disabled ? "default" : "pointer",
        opacity: disabled ? 0.4 : 1,
        transition: "background-color 0.2s, border-color 0.2s",
    });
    const toggleKnob = (enabled: boolean): React.CSSProperties => ({
        position: "absolute" as const,
        top: "2rem",
        left: enabled ? "24rem" : "2rem",
        width: "16rem",
        height: "16rem",
        borderRadius: "50%",
        backgroundColor: enabled ? G.greenBright : G.knobOff,
        transition: "left 0.2s",
    });

    const renderToggle = (
        label: string,
        enabled: boolean,
        onToggle: () => void,
        disabled: boolean,
    ): React.ReactNode => (
        <div
            style={{ ...styles.toggleRow, opacity: disabled ? 0.5 : 1 }}
            role="button"
            tabIndex={disabled ? -1 : 0}
            onClick={disabled ? undefined : onToggle}
            onKeyDown={(e) => {
                if (disabled) return;
                if (e.key === "Enter" || e.key === " ") {
                    e.preventDefault();
                    onToggle();
                }
            }}
        >
            <div style={styles.toggleLabel}>
                <div style={toggleDot(enabled)} />
                <span style={styles.toggleTitle}>{label}</span>
            </div>
            <div style={toggleTrack(enabled, disabled)}>
                <div style={toggleKnob(enabled)} />
            </div>
        </div>
    );

    return (
        <div style={styles.section}>
            <div style={styles.title}>{l.t("ONLINE_CONSENT_TITLE")}</div>
            <div style={styles.body}>
                {l.t("ONLINE_CONSENT_INTRO")}
                <ul style={styles.featureList}>
                    <li style={styles.featureItem}>
                        <span style={styles.featureBullet} />
                        {l.t("ONLINE_CONSENT_FEATURE_NEWS")}
                    </li>
                    <li style={styles.featureItem}>
                        <span style={styles.featureBullet} />
                        {l.t("ONLINE_CONSENT_FEATURE_STATS")}
                    </li>
                    <li style={styles.featureItem}>
                        <span style={styles.featureBullet} />
                        {l.t("ONLINE_CONSENT_FEATURE_NICKNAME")}
                    </li>
                </ul>

                {renderToggle(
                    l.t("ONLINE_CONSENT_GO_ONLINE"),
                    goOnline,
                    () => setGoOnline((v) => !v),
                    false,
                )}
                {renderToggle(
                    l.t("ONLINE_CONSENT_SHARE_DIAGNOSTICS"),
                    goOnline && diagnostics,
                    () => setDiagnostics((v) => !v),
                    !goOnline,
                )}
                <div style={styles.diagnosticsLine}>{l.t("ONLINE_CONSENT_DIAGNOSTICS")}</div>

                <div style={styles.agreeLine}>{l.t("ONLINE_CONSENT_AGREE")}</div>
                <button type="button" style={styles.privacyLink} onClick={openPrivacy}>
                    {l.t("ONLINE_CONSENT_PRIVACY_LINK")}
                </button>
            </div>
            <button
                type="button"
                style={styles.continueButton}
                onClick={() => onConfirm(goOnline, goOnline && diagnostics)}
            >
                {l.t("ONLINE_CONSENT_CONTINUE")}
            </button>
        </div>
    );
};
