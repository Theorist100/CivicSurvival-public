/**
 * SETTINGS → ONLINE view: the Online master, diagnostics opt-in, nickname and Discord link.
 */

import React, { memo, useCallback, useState } from "react";
import { Z_INDEX, getButtonStyles } from "../../themes";
import { bindingDataOrDefault } from "@hooks/domain";
import { DEFAULT_GLOBAL_NEWS_STATE, useGlobalNews } from "../../hooks/state/useGlobalNews";
import { useNetworkActions, useSettingsActions } from "../../hooks/actions";
import { useLocale } from "../../locales";
import { DisabledOverlay } from "../shared/ui";
import { NicknameEditor } from "../shared/nickname/NicknameEditor";
import { OnlineConsentContent } from "../scenario/OnlineConsentContent";
import { SettingsToggleRow, useSettingsDto, useSettingsStyles } from "./settingsShared";

const SettingsOnlineContentComponent: React.FC = () => {
    const l = useLocale();
    const { theme, accents, s } = useSettingsStyles();
    const settings = useSettingsDto();
    const { openDiscord } = useSettingsActions();
    const networkActions = useNetworkActions();
    const news = useGlobalNews();
    const newsData = bindingDataOrDefault(news, DEFAULT_GLOBAL_NEWS_STATE);

    // First-enable Online consent prompt. Shown only when the player turns the master ON
    // for the first time (no consent decision recorded yet). It is a narrow consent prompt,
    // NOT a second settings menu — no duplicate toggles, choice is made by its buttons.
    const [showOnlineConsent, setShowOnlineConsent] = useState(false);
    // Local latch closing the sub-frame window between a consent decision and the next
    // throttled NewsDto refresh (~500ms). The C# latch (m_OnlineConsentRecorded) is set
    // synchronously, but onlineConsentRecorded from the DTO only reflects it on the next
    // panel tick; a fast OFF→ON inside that window would otherwise re-show the prompt.
    // This is a correct record-of-decision latch (once a decision is made this session,
    // do not prompt again), not race masking — it never resets once true.
    const [consentDecidedLocally, setConsentDecidedLocally] = useState(false);
    const onlineConsentRecorded = newsData.onlineConsentRecorded || consentDecidedLocally;

    const handleGlobalConnectionToggle = useCallback(() => {
        const turningOn = !newsData.networkConnectionEnabled;
        // First time enabling Online and no consent recorded yet → show the consent prompt
        // instead of toggling immediately. Online is enabled by the accept button. Turning
        // OFF, or re-enabling after a prior decision, toggles directly.
        if (turningOn && !onlineConsentRecorded) {
            setShowOnlineConsent(true);
            return;
        }
        networkActions.toggleGlobalConnection(turningOn);
    }, [networkActions, newsData.networkConnectionEnabled, onlineConsentRecorded]);

    // The agreement decides Online + diagnostics with one Continue button (no Cancel:
    // flipping "Go online" off is the offline choice). Any decision records consent
    // globally (toggleGlobalConnection latches the C# m_OnlineConsentRecorded), so latch
    // locally too before the DTO catches up — a fast re-toggle inside the DTO-refresh
    // window must not re-prompt. Diagnostics is forced false when offline.
    const handleConsentConfirm = useCallback((goOnline: boolean, diagnostics: boolean) => {
        setShowOnlineConsent(false);
        setConsentDecidedLocally(true);
        networkActions.toggleGlobalConnection(goOnline);
        networkActions.setTelemetryEnabled(goOnline && diagnostics);
    }, [networkActions]);

    if (!settings) return null;

    const buttonStyles = getButtonStyles(theme, accents);
    const telemetryEnabled = settings.TelemetryEnabled ?? false;
    const canToggleTelemetry = settings.CanToggleTelemetry;
    const telemetryLockedReasonId = settings.TelemetryLockedReasonId ?? "";

    // Consent prompt covers this view only, not the whole screen: it is a decision about
    // one setting, and the rest of the dashboard stays legible behind the dashboard chrome.
    const consentOverlayStyle: React.CSSProperties = {
        position: "absolute",
        top: 0,
        left: 0,
        right: 0,
        bottom: 0,
        background: theme.colors.background,
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        padding: "16rem",
        minHeight: 0,
        zIndex: Z_INDEX.modal,
    };

    const consentCardStyle: React.CSSProperties = {
        width: "100%",
        maxWidth: "440rem",
        maxHeight: "100%",
        overflowY: "auto",
        background: theme.colors.paper,
        border: `2rem solid ${theme.colors.border}`,
        borderRadius: theme.layout.borderRadius,
        padding: "12rem",
    };

    return (
        <div style={{ ...s.column, position: "relative" }}>
            <div style={s.section}>
                <SettingsToggleRow
                    label={l.t("UI_SETTINGS_ONLINE")}
                    checked={newsData.networkConnectionEnabled}
                    onChange={handleGlobalConnectionToggle}
                    color={accents.schemes.accent}
                    styles={s}
                />
                <div style={s.description}>{l.t("UI_SETTINGS_ONLINE_DESC")}</div>
            </div>

            <DisabledOverlay disabled={!canToggleTelemetry}>
                <div style={s.section}>
                    <SettingsToggleRow
                        label={l.t("UI_SETTINGS_TELEMETRY")}
                        checked={telemetryEnabled}
                        onChange={() => networkActions.setTelemetryEnabled(!telemetryEnabled)}
                        color={accents.schemes.accent}
                        disabled={!canToggleTelemetry}
                        styles={s}
                    />
                    <div style={s.description}>
                        {telemetryLockedReasonId ? l.tDynamic(telemetryLockedReasonId) : l.t("UI_SETTINGS_TELEMETRY_DESC")}
                    </div>
                </div>
            </DisabledOverlay>

            {/* Shared with the arena board — NicknameEditor owns validation, the change
                limit and its own availability gate, so both surfaces stay in step. */}
            <div style={s.section}>
                <NicknameEditor />
            </div>

            {/* The only in-game Discord entry point — the Roadmap view that used to
                carry the support links was retired (announcements live in Discord). */}
            <div style={s.section}>
                <div style={s.label}>{l.t("UI_SETTINGS_COMMUNITY")}</div>
                <button style={buttonStyles.ghost(theme.colors.textSecondary)} onClick={openDiscord}>
                    {l.t("UI_SETTINGS_DISCORD")}
                </button>
                <div style={s.description}>{l.t("UI_SETTINGS_DISCORD_DESC")}</div>
            </div>

            {showOnlineConsent && (
                <div style={consentOverlayStyle}>
                    <div style={consentCardStyle}>
                        <OnlineConsentContent onConfirm={handleConsentConfirm} />
                    </div>
                </div>
            )}
        </div>
    );
};

export const SettingsOnlineContent = memo(SettingsOnlineContentComponent);
SettingsOnlineContent.displayName = "SettingsOnlineContent";
