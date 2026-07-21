import React, { useCallback, useState } from "react";
import { Column, Row } from "@coherent";
import { getButtonStyles, useTheme, useAccents, hexToRgba } from "@themes";
import { useGlobalNews } from "../../../hooks/state/useGlobalNews";
import { useLocale } from "../../../locales";
import { SegmentedTabs } from "../../shared/ui";
import { NicknameEditor } from "../../shared/nickname/NicknameEditor";
import { useNetworkActions } from "../../../hooks/actions";
import { OnlineConsentContent } from "../../scenario/OnlineConsentContent";

export type LeaderboardTabType = "ranks" | "alltime" | "weekly" | "guide";

const formatDelta = (delta: number): string => (delta > 0 ? `+${delta}` : `${delta}`);

interface LeaderboardTabsProps {
    activeTab: LeaderboardTabType;
    onTabChange: (tab: LeaderboardTabType) => void;
    disabled?: boolean;
}

interface LeaderboardPositionFooterProps {
    yourPosition: number | null;
    yourWeeklyPosition: number | null;
    /** Positions climbed this session (positive = up); 0 hides the chip. */
    allTimeDelta?: number;
    weeklyDelta?: number;
    /**
     * What to do to enter this board, shown instead of an empty footer when
     * the player is unranked. Silence reads as breakage — the player cannot
     * tell "no data yet" from "the panel is broken".
     */
    unrankedHint?: string;
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
                    { value: "guide", label: l.t("UI_ARENA_TAB_GUIDE") },
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
    allTimeDelta = 0,
    weeklyDelta = 0,
    unrankedHint,
}) => {
    const theme = useTheme();
    const accents = useAccents();
    const l = useLocale();
    const news = useGlobalNews();
    // Nickname lives in the settings panel, but the board is where the player notices
    // they are "Mayor_XXXX" — almost nobody finds the setting from there. Kept open once
    // opened so the save result stays visible after nicknameInitialized flips.
    const [editingNickname, setEditingNickname] = useState(false);
    // "You have no nickname" is only claimed from real data: the default DTO is empty,
    // which would flash the prompt at a named player on every cold open before the
    // first NewsDto lands.
    //
    // Asks whether a name EXISTS, not whether one was ever set: nicknameInitialized
    // latches true forever, so a player who cleared their nickname kept the flag, lost
    // the name, went back to appearing as Mayor_XXXX — and had the prompt hidden from
    // them precisely when they needed it.
    const nicknameMissing = news.status === "ready" && news.data.playerNickname.length === 0;

    const isUnranked = yourPosition === null && yourWeeklyPosition === null;
    if (isUnranked && unrankedHint === undefined) return null;

    const deltaStyle = (delta: number): React.CSSProperties => ({
        fontSize: "11rem",
        fontWeight: 700,
        fontFamily: theme.typography.fontFamilyMono,
        color: delta > 0 ? theme.colors.success : theme.colors.error,
        marginLeft: "6rem",
    });

    // Column root: the footer now stacks the positions row and the nickname prompt.
    // display is explicit — cohtml emulates block through flex and React must never
    // diff it down to "".
    const footerStyle: React.CSSProperties = {
        display: "flex",
        flexDirection: "column",
        minHeight: "40rem",
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

    const hintStyle: React.CSSProperties = {
        fontSize: "11rem",
        color: theme.colors.textSecondary,
        textAlign: "center" as const,
    };

    const nicknameBlockStyle: React.CSSProperties = {
        display: "flex",
        flexDirection: "column",
        minHeight: "40rem",
        marginTop: "10rem",
        paddingTop: "10rem",
        borderTop: `2rem solid ${hexToRgba(accents.operations.accent, 0.25)}`,
    };

    const setNicknameButtonStyle: React.CSSProperties = {
        ...getButtonStyles(theme, accents).outline(accents.operations.accent),
        marginTop: "6rem",
    };

    const nicknameStyle: React.CSSProperties = {
        fontSize: "12rem",
        fontWeight: 600,
        color: theme.colors.textPrimary,
        marginRight: "8rem",
    };

    const changeNicknameStyle: React.CSSProperties = {
        ...getButtonStyles(theme, accents).ghost(accents.operations.accent),
        fontSize: "10rem",
    };

    // The board is where the player reads their own name, so it is where both
    // nickname doors live — otherwise a player who dislikes what they see has to go
    // hunting through settings. Editing renders the mod's single NicknameEditor,
    // never a copy of it. Stays open once opened so the save result remains visible
    // after the name lands.
    const nicknameBlock = (
        <div style={nicknameBlockStyle}>
            {editingNickname ? (
                <NicknameEditor compact />
            ) : nicknameMissing ? (
                <Column align="center">
                    <span style={hintStyle}>{l.t("UI_ARENA_NICKNAME_PROMPT")}</span>
                    <button type="button" style={setNicknameButtonStyle} onClick={() => setEditingNickname(true)}>
                        {l.t("UI_ARENA_SET_NICKNAME")}
                    </button>
                </Column>
            ) : (
                <Row align="center" justify="center">
                    <span style={nicknameStyle}>{news.status === "ready" ? news.data.playerNickname : ""}</span>
                    <button type="button" style={changeNicknameStyle} onClick={() => setEditingNickname(true)}>
                        {l.t("UI_ARENA_CHANGE_NICKNAME")}
                    </button>
                </Row>
            )}
        </div>
    );

    // Nothing to offer before the first NewsDto: no name to show, and claiming one is
    // missing would be a guess.
    const nicknamePrompt = news.status === "ready" && nicknameBlock;

    if (isUnranked) {
        return (
            <div style={footerStyle}>
                <Row justify="center">
                    <span style={hintStyle}>{unrankedHint}</span>
                </Row>
                {nicknamePrompt}
            </div>
        );
    }

    return (
        <div style={footerStyle}>
            <Row justify="space-around">
                {yourPosition !== null && (
                    <Column align="center">
                        <span style={labelStyle}>{l.t("UI_ARENA_YOUR_ALLTIME")}</span>
                        <Row align="center">
                            <span style={valueStyle}>#{yourPosition}</span>
                            {allTimeDelta !== 0 && <span style={deltaStyle(allTimeDelta)}>{formatDelta(allTimeDelta)}</span>}
                        </Row>
                    </Column>
                )}
                {yourWeeklyPosition !== null && (
                    <Column align="center">
                        <span style={labelStyle}>{l.t("UI_ARENA_THIS_WEEK")}</span>
                        <Row align="center">
                            <span style={valueStyle}>#{yourWeeklyPosition}</span>
                            {weeklyDelta !== 0 && <span style={deltaStyle(weeklyDelta)}>{formatDelta(weeklyDelta)}</span>}
                        </Row>
                    </Column>
                )}
            </Row>
            {nicknamePrompt}
        </div>
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
        // Sized for the settings copy below, which spells out what Online actually does;
        // matches the consent card's width so both read as the same object.
        maxWidth: "360rem",
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
            {/* The same sentence the settings panel shows for the Online master — one
                description of what gets switched on. The button below enables Online
                directly once a consent decision exists, so this is the only place that
                tells a returning player what they are turning on. */}
            <div style={promptSubtitleStyle}>
                {l.t("UI_SETTINGS_ONLINE_DESC")}
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
