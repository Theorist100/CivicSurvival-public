import React, { memo, useCallback, useEffect, useRef, useState } from "react";
import { createPortal } from "react-dom";
import { Row } from "../../coherent";
import { Z_INDEX, getButtonStyles, useTheme, useAccents } from "../../../themes";
import { useSafeNumber } from "../../../hooks/useSafeBinding";
import { uiTheme$ } from "../../../hooks/bindings/coreBindings";
import { bindingDataOrDefault, type SettingsDto, useSettings } from "@hooks/domain";
import { getModRoot } from "@utils/modRoot";
import { ModLanguage, setLanguage } from "../../../hooks/bindings/localeBindings";
import { DEFAULT_GLOBAL_NEWS_STATE, useGlobalNews } from "../../../hooks/state/useGlobalNews";
import {
    isDifficultyPresetId,
    isModLanguageId,
    isUIThemeId,
    type DifficultyPresetId,
    type ModLanguageId,
    type UIThemeId,
} from "../../../types/semantic";
import { DisabledOverlay, SegmentedTabs, ToggleSwitch } from "../../shared/ui";
import { useLocale } from "../../../locales";
import { useNetworkActions, useSettingsActions } from "../../../hooks/actions";
import { useDraggable } from "../../../hooks/useDraggable";
import { OnlineConsentContent } from "../../scenario/OnlineConsentContent";

type NetworkActions = ReturnType<typeof useNetworkActions>;
type SettingsTab = "interface" | "scenario" | "online" | "debug";

const SETTINGS_TABS: Array<{ id: SettingsTab; label: string }> = [
    { id: "interface", label: "Interface" },
    { id: "scenario", label: "Scenario" },
    { id: "online", label: "Online" },
    { id: "debug", label: "Report a bug" },
];

const LANGUAGE_LABELS: Record<ModLanguageId, string> = {
    [ModLanguage.GameDefault]: "Auto",
    [ModLanguage.English]: "EN",
    [ModLanguage.Ukrainian]: "UA",
    [ModLanguage.German]: "DE",
    [ModLanguage.Spanish]: "ES",
    [ModLanguage.French]: "FR",
    [ModLanguage.Polish]: "PL",
    [ModLanguage.Chinese]: "ZH",
};

const THEME_LABELS: Record<UIThemeId, string> = {
    0: "Tech Noir",
    1: "Classic Gold",
    2: "Soft Focus",
};

const DIFFICULTY_LABEL_KEYS = {
    0: "PRESET_MANAGEDDEFICIT",
    1: "PRESET_BLACKOUTPROTOCOL",
    2: "PRESET_ISLANDMODE",
    3: "PRESET_CUSTOM",
} as const;

const normalizeIds = <T extends number>(value: unknown, guard: (id: number) => id is T): T[] =>
    Array.isArray(value) ? value.filter((item): item is T => typeof item === "number" && guard(item)) : [];

const NICKNAME_RE = /^[A-Za-z0-9_]{3,20}$/;

// Each native dump is ~30-50 MB; cap how many a player can send at once. Mirrors the C# cap in
// ErrorReportService.MAX_CRASH_DUMPS_PER_SUBMIT.
const MAX_CRASH_DUMPS_SELECTED = 3;

const terminalRequestIdOnMount = (request: { RequestId: number; Status: string } | undefined): number | null =>
    request && request.RequestId > 0 && (request.Status === "failed" || request.Status === "success")
        ? request.RequestId
        : null;

interface SettingsPanelProps {
    isOpen: boolean;
    onClose: () => void;
}

// Portaled to the mod root and wrapped in a fixed overlay (see SettingsPanelReady),
// the same shape as the working HelpSection modal. The overlay anchors the panel
// to the viewport so it no longer drags along with the Dashboard window. Mounting
// it as a standalone Coherent module instead broke useDraggable + outside-click,
// so the portal route is the correct one. Visibility is data-driven via
// SettingsDto.IsExpanded + the togglePanel trigger.
export const SettingsPanel: React.FC = memo(() => {
    const settings = useSettings();
    const { togglePanel } = useSettingsActions();
    const isOpen = settings.status === "ready" && settings.data.IsExpanded;
    const handleClose = useCallback(() => {
        if (isOpen) togglePanel();
    }, [isOpen, togglePanel]);

    if (!isOpen || settings.status !== "ready") return null;

    return createPortal(
        <SettingsPanelReady
            settings={settings.data}
            isOpen={isOpen}
            onClose={handleClose}
        />,
        getModRoot()
    );
});

interface SettingsPanelReadyProps extends SettingsPanelProps {
    settings: SettingsDto;
}

const SettingsPanelReady: React.FC<SettingsPanelReadyProps> = memo(({ isOpen, onClose, settings }) => {
    const theme = useTheme();
    const accents = useAccents();
    const l = useLocale();
    // Two-click consent for the crash-dump send: the dump is raw process memory, so the player
    // must affirm after reading the warning before it leaves the machine.
    const [dumpConsent, setDumpConsent] = useState(false);
    // Player-selected dump file names. The player may not know which dump is the real crash, so the
    // list is multi-select; a bounded number is sent (each is 30-50 MB).
    const [selectedDumps, setSelectedDumps] = useState<ReadonlySet<string>>(() => new Set());
    const {
        clearErrors,
        copyReport,
        sendReport,
        sendModLog,
        sendCrashDumps,
        setBackupPower,
        setConstructionDelay,
        setDifficultyPreset,
        setMuteAlertAudio,
        setMuteCivicAudio,
        setMuteCombatAudio,
        setMuteDroneAudio,
        setNeighborEnvy,
        setProtectCriticalInfra,
        setRandomDisasters,
        setUITheme,
        setWinterMultiplier,
    } = useSettingsActions();
    const news = useGlobalNews();
    const newsData = bindingDataOrDefault(news, DEFAULT_GLOBAL_NEWS_STATE);
    const networkActions = useNetworkActions();
    const rawLanguagePref = settings.LanguagePreference ?? 0;
    const languagePref: ModLanguageId = isModLanguageId(rawLanguagePref) ? rawLanguagePref : ModLanguage.GameDefault;
    const rawTheme = useSafeNumber(uiTheme$, 0);
    const currentTheme: UIThemeId = isUIThemeId(rawTheme) ? rawTheme : 0;
    const muteCivicAudio = settings.MuteCivicAudio ?? false;
    const muteDroneAudio = settings.MuteDroneAudio ?? false;
    const muteAlertAudio = settings.MuteAlertAudio ?? false;
    const muteCombatAudio = settings.MuteCombatAudio ?? false;
    const rawDifficultyPreset = settings.DifficultyPreset ?? 0;
    const difficultyPreset: DifficultyPresetId = isDifficultyPresetId(rawDifficultyPreset) ? rawDifficultyPreset : 0;
    const telemetryEnabled = settings.TelemetryEnabled ?? false;
    const backendNickname = newsData.playerNickname;
    const nicknameRequest = newsData.nicknameRequest;
    const nicknameChangesRemaining = newsData.nicknameChangesRemaining;
    const nicknameInitialized = newsData.nicknameInitialized;
    const canToggleTelemetry = settings.CanToggleTelemetry;
    const telemetryLockedReasonId = settings.TelemetryLockedReasonId ?? "";
    const localeRequest = settings.LocaleRequest;
    const languageOptions = normalizeIds(settings.AvailableLocales, isModLanguageId)
        .map(value => ({ value, label: LANGUAGE_LABELS[value] }));
    const themeOptions = normalizeIds(settings.AvailableThemes, isUIThemeId)
        .map(value => ({ value, label: THEME_LABELS[value] }));
    const difficultyOptions = ([0, 1, 2, 3] as DifficultyPresetId[])
        .map(value => ({ value, label: l.t(DIFFICULTY_LABEL_KEYS[value]) }));
    const [localNickname, setLocalNickname] = useState(backendNickname);
    const [nicknameErrorKey, setNicknameErrorKey] = useState<string>("");
    const [languageErrorKey, setLanguageErrorKey] = useState<string>("");
    const panelRef = useRef<HTMLDivElement | null>(null);
    const lastHandledNicknameRequestIdRef = useRef<number | null>(terminalRequestIdOnMount(nicknameRequest));
    const lastHandledLocaleRequestIdRef = useRef<number | null>(terminalRequestIdOnMount(localeRequest));
    const [activeTab, setActiveTab] = useState<SettingsTab>("interface");
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
    const { position, isDragging, handleMouseDown, dragRef } = useDraggable();
    const suppressNextClickRef = useRef(false);
    const setPanelRef = useCallback((el: HTMLDivElement | null) => {
        panelRef.current = el;
        dragRef(el);
    }, [dragRef]);

    useEffect(() => {
        setLocalNickname(prev => (prev === "" ? backendNickname : prev));
        setNicknameErrorKey("");
    }, [backendNickname]);

    useEffect(() => {
        if (!nicknameRequest) return;
        if (
            nicknameRequest.Status === "pending" ||
            nicknameRequest.RequestId === lastHandledNicknameRequestIdRef.current ||
            (nicknameRequest.Status !== "failed" && nicknameRequest.Status !== "success")
        ) {
            return;
        }

        lastHandledNicknameRequestIdRef.current = nicknameRequest.RequestId;
        if (nicknameRequest.Status === "failed") {
            setNicknameErrorKey(nicknameRequest.ReasonId);
        } else if (nicknameRequest.Status === "success") {
            setNicknameErrorKey("");
            if (nicknameRequest.CanonicalEcho !== localNickname) {
                setLocalNickname(nicknameRequest.CanonicalEcho);
            }
        }
    }, [backendNickname, localNickname, nicknameRequest]);

    useEffect(() => {
        if (!localeRequest) return;
        if (
            localeRequest.Status === "pending" ||
            localeRequest.RequestId === lastHandledLocaleRequestIdRef.current ||
            (localeRequest.Status !== "failed" && localeRequest.Status !== "success")
        ) {
            return;
        }

        lastHandledLocaleRequestIdRef.current = localeRequest.RequestId;
        if (localeRequest.Status === "failed") {
            setLanguageErrorKey(localeRequest.ReasonId);
        } else if (localeRequest.Status === "success") {
            setLanguageErrorKey("");
        }
    }, [localeRequest]);

    useEffect(() => {
        if (canToggleTelemetry) return;
        setNicknameErrorKey("");
        setLocalNickname(backendNickname);
    }, [backendNickname, canToggleTelemetry]);

    useEffect(() => {
        if (!isDragging) return;
        suppressNextClickRef.current = true;
    }, [isDragging]);

    useEffect(() => {
        if (!isOpen) return;
        const handleOutsideClick = (e: MouseEvent) => {
            if (isDragging) return;
            if (suppressNextClickRef.current) {
                suppressNextClickRef.current = false;
                return;
            }
            if (panelRef.current && !panelRef.current.contains(e.target as Node)) {
                onClose();
            }
        };
        const handleKeyDown = (e: KeyboardEvent) => {
            if (e.key === "Escape") onClose();
        };
        document.addEventListener("click", handleOutsideClick);
        document.addEventListener("keydown", handleKeyDown);
        return () => {
            document.removeEventListener("click", handleOutsideClick);
            document.removeEventListener("keydown", handleKeyDown);
        };
    }, [isOpen, onClose, isDragging]);

    const handleThemeChange = useCallback((themeId: UIThemeId) => {
        setUITheme(themeId);
    }, [setUITheme]);

    const handleTelemetryToggle = useCallback((actions: NetworkActions) => {
        actions.setTelemetryEnabled(!telemetryEnabled);
    }, [telemetryEnabled]);

    const handleGlobalConnectionToggle = useCallback((actions: NetworkActions) => {
        const turningOn = !newsData.networkConnectionEnabled;
        // First time enabling Online and no consent recorded yet → show the consent prompt
        // instead of toggling immediately. Online is enabled by the accept button. Turning
        // OFF, or re-enabling after a prior decision, toggles directly.
        if (turningOn && !onlineConsentRecorded) {
            setShowOnlineConsent(true);
            return;
        }
        actions.toggleGlobalConnection(turningOn);
    }, [newsData.networkConnectionEnabled, onlineConsentRecorded]);

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

    const handleNicknameChange = useCallback((name: string) => {
        setLocalNickname(name);
        setNicknameErrorKey("");
    }, []);

    const nicknameChanged = localNickname !== backendNickname;
    const nicknameValid = localNickname === "" || NICKNAME_RE.test(localNickname);
    // Save is explicit (button / Enter) only — never on blur — so a stray click-away
    // does not spend a server-limited nickname change on a half-typed value.
    const canSaveNickname = nicknameChanged && nicknameValid;

    // Client-side rejection reason for the current input, shown inline so an invalid
    // nickname explains WHY it cannot be saved instead of leaving the Save button
    // silently disabled. Server-side rejections still arrive via nicknameErrorKey; this
    // covers the local validation gate (length / allowed characters). Derived from the
    // live value so it updates as the user types, unlike nicknameErrorKey which is
    // cleared on every keystroke.
    const nicknameRejectionKey = (nicknameChanged && !nicknameValid)
        ? ((localNickname.length < 3 || localNickname.length > 20)
            ? "UI_NICKNAME_INVALID_LENGTH"
            : "UI_NICKNAME_INVALID_CHARS")
        : "";

    const commitNickname = useCallback((actions: NetworkActions) => {
        const name = localNickname;
        if (name === backendNickname) return;
        if (name !== "" && !NICKNAME_RE.test(name)) {
            setNicknameErrorKey((name.length < 3 || name.length > 20)
                ? "UI_NICKNAME_INVALID_LENGTH"
                : "UI_NICKNAME_INVALID_CHARS");
            return;
        }
        actions.setPlayerNickname(name);
    }, [localNickname, backendNickname]);

    const crashDumps = settings.CrashDumps ?? [];
    const selectedSizeMb = crashDumps
        .filter(dump => selectedDumps.has(dump.Name))
        .reduce((sum, dump) => sum + (dump.SizeMb ?? 0), 0);
    const overDumpLimit = selectedDumps.size > MAX_CRASH_DUMPS_SELECTED;
    const canSendDumps = selectedDumps.size > 0 && !overDumpLimit;

    const toggleDump = useCallback((name: string) => {
        setSelectedDumps(prev => {
            const next = new Set(prev);
            if (next.has(name)) next.delete(name);
            else next.add(name);
            return next;
        });
    }, []);

    const buttonStyles = getButtonStyles(theme, accents);
    const panelStyle: React.CSSProperties = {
        position: "fixed",
        left: position.x,
        top: position.y,
        width: "390rem",
        maxHeight: "690rem",
        display: "flex",
        flexDirection: "column",
        minHeight: 0,
        background: theme.colors.paper,
        border: `2rem solid ${theme.colors.border}`,
        borderRadius: theme.layout.borderRadius,
        boxShadow: theme.effects.shadowLg,
        overflow: "hidden",
        zIndex: Z_INDEX.dropdown,
        pointerEvents: "auto",
    };

    const sectionStyle: React.CSSProperties = {
        padding: "12rem",
        marginBottom: "10rem",
        background: theme.colors.surface,
        border: `1rem solid ${theme.colors.border}`,
        borderRadius: theme.layout.borderRadius,
    };

    // Consent prompt overlay — covers the panel, not the whole screen. Pause-safe: pure
    // local React state + EventBus triggers on accept, no GameSimulation involvement.
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
        maxHeight: "100%",
        overflowY: "auto",
        background: theme.colors.paper,
        border: `2rem solid ${theme.colors.border}`,
        borderRadius: theme.layout.borderRadius,
        padding: "12rem",
    };

    const labelStyle: React.CSSProperties = {
        fontSize: theme.typography.sizeXS,
        color: theme.colors.textSecondary,
        marginBottom: theme.spacing.xs,
        textTransform: "uppercase",
        letterSpacing: "0.5rem",
    };

    const headerStyle: React.CSSProperties = {
        display: "flex",
        alignItems: "center",
        justifyContent: "space-between",
        padding: "12rem",
        borderBottom: `2rem solid ${theme.colors.border}`,
        cursor: isDragging ? "grabbing" : "grab",
        userSelect: "none",
        WebkitUserSelect: "none",
    };

    const titleStyle: React.CSSProperties = {
        fontSize: theme.typography.sizeSM,
        fontWeight: 700,
        color: theme.colors.textPrimary,
        textTransform: "uppercase",
        letterSpacing: "0.8rem",
    };

    const closeButtonStyle: React.CSSProperties = {
        width: "26rem",
        height: "26rem",
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        background: "transparent",
        border: `2rem solid ${theme.colors.border}`,
        borderRadius: theme.layout.borderRadius,
        color: theme.colors.textSecondary,
        cursor: "pointer",
        fontSize: theme.typography.sizeSM,
        lineHeight: 1,
    };

    const tabBarStyle: React.CSSProperties = {
        display: "flex",
        padding: "8rem 8rem 0",
        borderBottom: `2rem solid ${theme.colors.border}`,
    };

    const tabButtonStyle = (active: boolean): React.CSSProperties => ({
        flex: 1,
        padding: "8rem 4rem",
        marginRight: "4rem",
        background: active ? theme.colors.surface : "transparent",
        border: "none",
        borderBottom: active ? `3rem solid ${accents.operations.accent}` : "3rem solid transparent",
        color: active ? accents.operations.accent : theme.colors.textSecondary,
        cursor: "pointer",
        fontSize: theme.typography.sizeXS,
        fontWeight: active ? 700 : 600,
        textTransform: "uppercase",
    });

    const bodyStyle: React.CSSProperties = {
        padding: "12rem",
        overflowY: "auto",
        overflowX: "hidden",
        minHeight: 0,
    };

    const rowStyle: React.CSSProperties = {
        marginBottom: "8rem",
    };

    const settingRowStyle: React.CSSProperties = {
        display: "flex",
        alignItems: "center",
        justifyContent: "space-between",
        marginBottom: "8rem",
    };

    const rowLabelStyle: React.CSSProperties = {
        fontSize: theme.typography.sizeXS,
        color: theme.colors.textSecondary,
        minWidth: 0,
        paddingRight: "10rem",
    };

    const descriptionStyle: React.CSSProperties = {
        fontSize: theme.typography.sizeXS,
        color: theme.colors.textMuted,
        marginTop: theme.spacing.xs,
        opacity: 0.75,
        lineHeight: 1.35,
    };

    // Scrollable dump list: ~3 rows visible, the rest reachable by scroll.
    const dumpListStyle: React.CSSProperties = {
        maxHeight: "108rem",
        overflowY: "auto",
        overflowX: "hidden",
        border: `1rem solid ${theme.colors.border}`,
        borderRadius: theme.layout.borderRadius,
        background: theme.colors.paper,
    };

    const dumpRowStyle = (selected: boolean): React.CSSProperties => ({
        display: "flex",
        alignItems: "center",
        padding: "6rem 8rem",
        fontSize: theme.typography.sizeXS,
        color: theme.colors.textSecondary,
        borderBottom: `1rem solid ${theme.colors.border}`,
        background: selected ? theme.colors.surface : "transparent",
        cursor: "pointer",
    });

    const dumpCheckStyle = (selected: boolean): React.CSSProperties => ({
        width: "14rem",
        height: "14rem",
        flexShrink: 0,
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        border: `2rem solid ${selected ? accents.crisis.accent : theme.colors.border}`,
        borderRadius: "3rem",
        background: selected ? accents.crisis.accent : "transparent",
        marginRight: "8rem",
    });

    const inputStyle: React.CSSProperties = {
        width: "100%",
        padding: theme.spacing.sm,
        background: theme.colors.surface,
        border: `2rem solid ${theme.colors.border}`,
        borderRadius: theme.layout.borderRadius,
        color: theme.colors.textPrimary,
        fontSize: theme.typography.sizeXS,
    };

    const nicknameRowStyle: React.CSSProperties = {
        display: "flex",
        alignItems: "center",
    };

    const nicknameInputStyle: React.CSSProperties = {
        ...inputStyle,
        flex: 1,
        minWidth: 0,
        marginRight: "6rem",
    };

    const saveNicknameButtonStyle = (enabled: boolean): React.CSSProperties => ({
        ...buttonStyles.action(accents.schemes.accent),
        flexShrink: 0,
        opacity: enabled ? 1 : 0.4,
        cursor: enabled ? "pointer" : "default",
    });

    const renderToggleRow = (
        label: string,
        checked: boolean,
        onChange: () => void,
        color: string = accents.operations.accent,
        disabled: boolean = false,
    ) => (
        <div style={settingRowStyle}>
            <span style={rowLabelStyle}>{label}</span>
            <ToggleSwitch checked={checked} color={color} onChange={onChange} disabled={disabled} />
        </div>
    );

    const renderInterfaceTab = () => (
        <>
            <div style={sectionStyle}>
                <div style={labelStyle}>{l.t("UI_SETTINGS_LANGUAGE")}</div>
                <SegmentedTabs
                    options={languageOptions}
                    value={languagePref}
                    onChange={setLanguage}
                    color={accents.operations.accent}
                />
                {languageErrorKey && (
                    <div style={{ fontSize: "10rem", color: accents.crisis.accent, marginTop: theme.spacing.xs }}>
                        {l.tDynamic(languageErrorKey)}
                    </div>
                )}
            </div>

            <div style={sectionStyle}>
                <div style={labelStyle}>{l.t("UI_SETTINGS_THEME")}</div>
                <SegmentedTabs
                    options={themeOptions}
                    value={currentTheme}
                    onChange={handleThemeChange}
                    color={accents.operations.accent}
                />
            </div>

            <div style={sectionStyle}>
                <div style={labelStyle}>{l.t("UI_SETTINGS_MUTE_AUDIO")}</div>
                {renderToggleRow(
                    l.t("UI_SETTINGS_MUTE_AUDIO"),
                    muteCivicAudio,
                    () => setMuteCivicAudio(!muteCivicAudio),
                    accents.operations.accent,
                )}
                <div style={descriptionStyle}>{l.t("UI_SETTINGS_MUTE_AUDIO_DESC")}</div>
                {/* Master overrides categories: when on, the per-category rows are shown
                    "struck through" via DisabledOverlay — their stored values are kept,
                    but the effective rule is master OR category (matches C# IsAudioMuted). */}
                <DisabledOverlay disabled={muteCivicAudio}>
                    {renderToggleRow(
                        l.t("UI_SETTINGS_MUTE_DRONES"),
                        muteDroneAudio,
                        () => setMuteDroneAudio(!muteDroneAudio),
                        accents.operations.accent,
                    )}
                    <div style={descriptionStyle}>{l.t("UI_SETTINGS_MUTE_DRONES_DESC")}</div>
                    {renderToggleRow(
                        l.t("UI_SETTINGS_MUTE_ALERTS"),
                        muteAlertAudio,
                        () => setMuteAlertAudio(!muteAlertAudio),
                        accents.operations.accent,
                    )}
                    <div style={descriptionStyle}>{l.t("UI_SETTINGS_MUTE_ALERTS_DESC")}</div>
                    {renderToggleRow(
                        l.t("UI_SETTINGS_MUTE_COMBAT"),
                        muteCombatAudio,
                        () => setMuteCombatAudio(!muteCombatAudio),
                        accents.operations.accent,
                    )}
                    <div style={descriptionStyle}>{l.t("UI_SETTINGS_MUTE_COMBAT_DESC")}</div>
                </DisabledOverlay>
            </div>
        </>
    );

    const renderScenarioTab = () => (
        <>
            <div style={sectionStyle}>
                <div style={labelStyle}>{l.t("LABEL_DIFFICULTY")}</div>
                <SegmentedTabs
                    options={difficultyOptions}
                    value={difficultyPreset}
                    onChange={setDifficultyPreset}
                    color={accents.crisis.accent}
                />
                <div style={descriptionStyle}>
                    {`${l.t("LABEL_IMPORT_CAP")}: ${settings.LegalImportMW}`}
                </div>
                <div style={descriptionStyle}>
                    {`${l.t("LABEL_EXPORT_CAP")}: ${settings.LegalExportMW}`}
                </div>
            </div>

            <div style={sectionStyle}>
                <div style={labelStyle}>{l.t("LABEL_ADVANCED_SETTINGS")}</div>
                {renderToggleRow(l.t("LABEL_BUILD_DELAY"), settings.ConstructionDelay, () => setConstructionDelay(!settings.ConstructionDelay))}
                {renderToggleRow(l.t("LABEL_DISASTERS"), settings.RandomDisasters, () => setRandomDisasters(!settings.RandomDisasters))}
                {renderToggleRow(l.t("LABEL_WINTER"), settings.WinterMultiplier, () => setWinterMultiplier(!settings.WinterMultiplier))}
                {renderToggleRow(l.t("LABEL_NEIGHBOR_ENVY"), settings.NeighborEnvy, () => setNeighborEnvy(!settings.NeighborEnvy))}
                {renderToggleRow(l.t("LABEL_BACKUP_POWER"), settings.BackupPower, () => setBackupPower(!settings.BackupPower))}
                {renderToggleRow(l.t("LABEL_PROTECT_CRITICAL_INFRA"), settings.ProtectCriticalInfra, () => setProtectCriticalInfra(!settings.ProtectCriticalInfra))}
            </div>
        </>
    );

    const renderOnlineTab = () => (
        <>
            <div style={sectionStyle}>
                {renderToggleRow(
                    l.t("UI_SETTINGS_ONLINE"),
                    newsData.networkConnectionEnabled,
                    () => handleGlobalConnectionToggle(networkActions),
                    accents.schemes.accent,
                )}
                <div style={descriptionStyle}>
                    {l.t("UI_SETTINGS_ONLINE_DESC")}
                </div>
            </div>

            <DisabledOverlay disabled={!canToggleTelemetry}>
                <div style={sectionStyle}>
                    {renderToggleRow(
                        l.t("UI_SETTINGS_TELEMETRY"),
                        telemetryEnabled,
                        () => handleTelemetryToggle(networkActions),
                        accents.schemes.accent,
                        !canToggleTelemetry,
                    )}
                    <div style={descriptionStyle}>
                        {telemetryLockedReasonId ? l.tDynamic(telemetryLockedReasonId) : l.t("UI_SETTINGS_TELEMETRY_DESC")}
                    </div>
                </div>
            </DisabledOverlay>

            <DisabledOverlay disabled={!canToggleTelemetry || !newsData.networkConnectionEnabled}>
                <div style={sectionStyle}>
                    <div style={labelStyle}>{l.t("UI_SETTINGS_NICKNAME")}</div>
                    <div style={nicknameRowStyle}>
                        <input
                            type="text"
                            style={nicknameInputStyle}
                            placeholder="CoolMayor123"
                            value={localNickname}
                            onChange={(e) => handleNicknameChange(e.target.value)}
                            onKeyDown={(e) => { if (e.key === "Enter") commitNickname(networkActions); }}
                            maxLength={20}
                        />
                        <button
                            type="button"
                            style={saveNicknameButtonStyle(canSaveNickname)}
                            disabled={!canSaveNickname}
                            onClick={() => commitNickname(networkActions)}
                        >
                            {l.t("UI_SETTINGS_NICKNAME_SAVE")}
                        </button>
                    </div>
                    <div style={{ ...descriptionStyle, fontSize: "10rem" }}>
                        {nicknameInitialized
                            ? l.t("UI_SETTINGS_NICKNAME_CHANGES_LEFT", Math.max(0, nicknameChangesRemaining), 3)
                            : l.t("UI_SETTINGS_NICKNAME_FIRST_FREE")}
                    </div>
                    <div style={{ ...descriptionStyle, fontSize: "10rem", fontStyle: "italic" }}>
                        {(nicknameErrorKey || nicknameRejectionKey)
                            ? l.tDynamic(nicknameErrorKey || nicknameRejectionKey)
                            : l.t("UI_SETTINGS_NICKNAME_WARN")}
                    </div>
                </div>
            </DisabledOverlay>
        </>
    );

    const renderDebugTab = () => (
        <>
            <div style={sectionStyle}>
                <div style={labelStyle}>{l.t("UI_SETTINGS_ERROR_REPORT")}</div>
                {settings.ErrorCount > 0 && (
                    <div style={{ fontSize: theme.typography.sizeXS, color: accents.crisis.accent, marginBottom: theme.spacing.xs }}>
                        {l.t("UI_SETTINGS_ERROR_COUNT", settings.ErrorCount)}
                    </div>
                )}
                <Row style={{ flexWrap: "wrap" }}>
                    <button style={{ ...buttonStyles.ghost(theme.colors.textSecondary), marginRight: theme.spacing.xs, marginBottom: theme.spacing.xs }} onClick={sendReport}>
                        {l.t("UI_SETTINGS_SEND_REPORT")}
                    </button>
                    <button style={{ ...buttonStyles.ghost(theme.colors.textSecondary), marginRight: theme.spacing.xs, marginBottom: theme.spacing.xs }} onClick={copyReport}>
                        {l.t("UI_SETTINGS_COPY_REPORT")}
                    </button>
                    <button style={{ ...buttonStyles.ghost(theme.colors.textSecondary), marginRight: theme.spacing.xs, marginBottom: theme.spacing.xs }} onClick={sendModLog}>
                        {l.t("UI_SETTINGS_SEND_MOD_LIST")}
                    </button>
                    {settings.ErrorCount > 0 && (
                        <button style={{ ...buttonStyles.ghost(theme.colors.textSecondary), marginRight: theme.spacing.xs, marginBottom: theme.spacing.xs }} onClick={clearErrors}>
                            {l.t("UI_SETTINGS_CLEAR_ERRORS")}
                        </button>
                    )}
                </Row>
                <div style={{ fontSize: theme.typography.sizeXS, color: theme.colors.textSecondary, marginTop: theme.spacing.xs }}>
                    {l.t("UI_SETTINGS_ERROR_REPORT_HINT")}
                </div>
                {settings.ReportStatusKey && (
                    <div style={{ fontSize: theme.typography.sizeXS, color: accents.schemes.accent, marginTop: theme.spacing.xs }}>
                        {l.tDynamic(settings.ReportStatusKey)}
                    </div>
                )}
            </div>
            <div style={sectionStyle}>
                <div style={labelStyle}>{l.t("UI_SETTINGS_CRASH_DUMP")}</div>
                <div style={{ fontSize: theme.typography.sizeXS, color: theme.colors.textSecondary, marginBottom: theme.spacing.xs }}>
                    {l.t("UI_SETTINGS_CRASH_DUMP_HINT")}
                </div>

                {crashDumps.length === 0 ? (
                    <div style={{ fontSize: theme.typography.sizeXS, color: theme.colors.textMuted, fontStyle: "italic" }}>
                        {l.t("UI_SETTINGS_CRASH_DUMP_LIST_EMPTY")}
                    </div>
                ) : (
                    <>
                        <div style={dumpListStyle}>
                            {crashDumps.map(dump => {
                                const selected = selectedDumps.has(dump.Name);
                                return (
                                    <div
                                        key={dump.Name}
                                        role="button"
                                        tabIndex={0}
                                        aria-pressed={selected}
                                        style={dumpRowStyle(selected)}
                                        onClick={() => toggleDump(dump.Name)}
                                        onKeyDown={(e) => {
                                            if (e.key === "Enter" || e.key === " ") {
                                                e.preventDefault();
                                                toggleDump(dump.Name);
                                            }
                                        }}
                                    >
                                        <span style={dumpCheckStyle(selected)}>
                                            {selected && (
                                                <svg width="10" height="10" viewBox="0 0 12 12" aria-hidden="true">
                                                    <path
                                                        d="M2 6.5 L5 9.5 L10 3"
                                                        fill="none"
                                                        stroke={theme.colors.paper}
                                                        strokeWidth="2"
                                                        strokeLinecap="round"
                                                        strokeLinejoin="round"
                                                    />
                                                </svg>
                                            )}
                                        </span>
                                        <span style={{ flex: 1, minWidth: 0 }}>{dump.TimeText}</span>
                                        <span style={{ color: theme.colors.textMuted, flexShrink: 0, marginLeft: "8rem" }}>
                                            {l.t("UI_SETTINGS_CRASH_DUMP_SIZE", (dump.SizeMb ?? 0).toFixed(1))}
                                        </span>
                                    </div>
                                );
                            })}
                        </div>

                        <div style={{ fontSize: theme.typography.sizeXS, color: theme.colors.textSecondary, marginTop: theme.spacing.xs }}>
                            {l.t("UI_SETTINGS_CRASH_DUMP_SELECTED", selectedDumps.size, selectedSizeMb.toFixed(1))}
                        </div>
                        {overDumpLimit && (
                            <div style={{ fontSize: theme.typography.sizeXS, color: accents.crisis.accent, marginTop: theme.spacing.xs }}>
                                {l.t("UI_SETTINGS_CRASH_DUMP_LIMIT", MAX_CRASH_DUMPS_SELECTED)}
                            </div>
                        )}

                        {!dumpConsent ? (
                            <button
                                style={{
                                    ...buttonStyles.ghost(theme.colors.textSecondary),
                                    marginTop: theme.spacing.xs,
                                    opacity: canSendDumps ? 1 : 0.4,
                                    cursor: canSendDumps ? "pointer" : "default",
                                }}
                                disabled={!canSendDumps}
                                onClick={() => setDumpConsent(true)}
                            >
                                {l.t("UI_SETTINGS_SEND_CRASH_DUMP")}
                            </button>
                        ) : (
                            <>
                                <div style={{ fontSize: theme.typography.sizeXS, color: accents.crisis.accent, margin: `${theme.spacing.xs} 0` }}>
                                    {l.t("UI_SETTINGS_CRASH_DUMP_CONSENT")}
                                </div>
                                <Row style={{ flexWrap: "wrap" }}>
                                    <button
                                        style={{ ...buttonStyles.ghost(accents.crisis.accent), marginRight: theme.spacing.xs, marginBottom: theme.spacing.xs }}
                                        onClick={() => {
                                            sendCrashDumps([...selectedDumps].join(","));
                                            setDumpConsent(false);
                                            setSelectedDumps(new Set());
                                        }}
                                    >
                                        {l.t("UI_SETTINGS_SEND_CRASH_DUMP_CONFIRM")}
                                    </button>
                                    <button
                                        style={{ ...buttonStyles.ghost(theme.colors.textSecondary), marginRight: theme.spacing.xs, marginBottom: theme.spacing.xs }}
                                        onClick={() => setDumpConsent(false)}
                                    >
                                        {l.t("UI_SETTINGS_CANCEL")}
                                    </button>
                                </Row>
                            </>
                        )}
                    </>
                )}

                {settings.ReportStatusKey && (
                    <div style={{ fontSize: theme.typography.sizeXS, color: accents.schemes.accent, marginTop: theme.spacing.xs }}>
                        {l.tDynamic(settings.ReportStatusKey)}
                    </div>
                )}
            </div>
        </>
    );

    const renderActiveTab = () => {
        switch (activeTab) {
            case "interface": return renderInterfaceTab();
            case "scenario": return renderScenarioTab();
            case "online": return renderOnlineTab();
            case "debug": return renderDebugTab();
        }
    };

    return (
        <div ref={setPanelRef} style={panelStyle} data-civic-delegated-click onClick={(e) => e.stopPropagation()} onMouseDown={(e) => e.stopPropagation()}>
            <div style={headerStyle} onMouseDown={handleMouseDown}>
                <div style={titleStyle}>{l.t("UI_TAB_SETTINGS")}</div>
                <button type="button" style={closeButtonStyle} onClick={onClose}>x</button>
            </div>
            <div style={tabBarStyle}>
                {SETTINGS_TABS.map((tab, index) => (
                    <button
                        key={tab.id}
                        type="button"
                        style={{
                            ...tabButtonStyle(activeTab === tab.id),
                            marginRight: index === SETTINGS_TABS.length - 1 ? 0 : "4rem",
                        }}
                        onClick={() => setActiveTab(tab.id)}
                    >
                        {tab.label}
                    </button>
                ))}
            </div>
            <div style={bodyStyle}>
                <div style={rowStyle}>{renderActiveTab()}</div>
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
});

SettingsPanelReady.displayName = "SettingsPanelReady";

SettingsPanel.displayName = "SettingsPanel";
