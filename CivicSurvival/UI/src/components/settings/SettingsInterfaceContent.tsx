/**
 * SETTINGS → INTERFACE view: language, theme, audio mutes.
 */

import React, { memo, useCallback, useEffect, useRef, useState } from "react";
import { useSafeNumber } from "../../hooks/useSafeBinding";
import { uiTheme$ } from "../../hooks/bindings/coreBindings";
import { ModLanguage, setLanguage } from "../../hooks/bindings/localeBindings";
import { useSettingsActions } from "../../hooks/actions";
import { useLocale } from "../../locales";
import { isModLanguageId, isUIThemeId, type ModLanguageId, type UIThemeId } from "../../types/semantic";
import { DisabledOverlay, SegmentedTabs } from "../shared/ui";
import { terminalRequestIdOnMount } from "@utils/requestLifecycle";
import { SettingsToggleRow, useSettingsDto, useSettingsStyles } from "./settingsShared";

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

const normalizeIds = <T extends number>(value: unknown, guard: (id: number) => id is T): T[] =>
    Array.isArray(value) ? value.filter((item): item is T => typeof item === "number" && guard(item)) : [];

const SettingsInterfaceContentComponent: React.FC = () => {
    const l = useLocale();
    const { accents, s } = useSettingsStyles();
    const settings = useSettingsDto();
    const { setUITheme, setMuteCivicAudio, setMuteDroneAudio, setMuteAlertAudio, setMuteCombatAudio } = useSettingsActions();

    const rawTheme = useSafeNumber(uiTheme$, 0);
    const currentTheme: UIThemeId = isUIThemeId(rawTheme) ? rawTheme : 0;

    const [languageErrorKey, setLanguageErrorKey] = useState<string>("");
    const localeRequest = settings?.LocaleRequest;
    const lastHandledLocaleRequestIdRef = useRef<number | null>(terminalRequestIdOnMount(localeRequest));

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

    const handleThemeChange = useCallback((themeId: UIThemeId) => {
        setUITheme(themeId);
    }, [setUITheme]);

    if (!settings) return null;

    const rawLanguagePref = settings.LanguagePreference ?? 0;
    const languagePref: ModLanguageId = isModLanguageId(rawLanguagePref) ? rawLanguagePref : ModLanguage.GameDefault;
    const languageOptions = normalizeIds(settings.AvailableLocales, isModLanguageId)
        .map(value => ({ value, label: LANGUAGE_LABELS[value] }));
    const themeOptions = normalizeIds(settings.AvailableThemes, isUIThemeId)
        .map(value => ({ value, label: THEME_LABELS[value] }));

    const muteCivicAudio = settings.MuteCivicAudio ?? false;
    const muteDroneAudio = settings.MuteDroneAudio ?? false;
    const muteAlertAudio = settings.MuteAlertAudio ?? false;
    const muteCombatAudio = settings.MuteCombatAudio ?? false;

    return (
        <div style={s.column}>
            <div style={s.section}>
                <div style={s.label}>{l.t("UI_SETTINGS_LANGUAGE")}</div>
                <SegmentedTabs
                    options={languageOptions}
                    value={languagePref}
                    onChange={setLanguage}
                    color={accents.operations.accent}
                />
                {languageErrorKey && (
                    <div style={s.error}>{l.tDynamic(languageErrorKey)}</div>
                )}
            </div>

            <div style={s.section}>
                <div style={s.label}>{l.t("UI_SETTINGS_THEME")}</div>
                <SegmentedTabs
                    options={themeOptions}
                    value={currentTheme}
                    onChange={handleThemeChange}
                    color={accents.operations.accent}
                />
            </div>

            <div style={s.section}>
                <div style={s.label}>{l.t("UI_SETTINGS_MUTE_AUDIO")}</div>
                <SettingsToggleRow
                    label={l.t("UI_SETTINGS_MUTE_AUDIO")}
                    checked={muteCivicAudio}
                    onChange={() => setMuteCivicAudio(!muteCivicAudio)}
                    color={accents.operations.accent}
                    styles={s}
                />
                <div style={s.description}>{l.t("UI_SETTINGS_MUTE_AUDIO_DESC")}</div>
                {/* Master overrides categories: when on, the per-category rows are shown
                    "struck through" via DisabledOverlay — their stored values are kept,
                    but the effective rule is master OR category (matches C# IsAudioMuted). */}
                <DisabledOverlay disabled={muteCivicAudio}>
                    <SettingsToggleRow
                        label={l.t("UI_SETTINGS_MUTE_DRONES")}
                        checked={muteDroneAudio}
                        onChange={() => setMuteDroneAudio(!muteDroneAudio)}
                        color={accents.operations.accent}
                        styles={s}
                    />
                    <div style={s.description}>{l.t("UI_SETTINGS_MUTE_DRONES_DESC")}</div>
                    <SettingsToggleRow
                        label={l.t("UI_SETTINGS_MUTE_ALERTS")}
                        checked={muteAlertAudio}
                        onChange={() => setMuteAlertAudio(!muteAlertAudio)}
                        color={accents.operations.accent}
                        styles={s}
                    />
                    <div style={s.description}>{l.t("UI_SETTINGS_MUTE_ALERTS_DESC")}</div>
                    <SettingsToggleRow
                        label={l.t("UI_SETTINGS_MUTE_COMBAT")}
                        checked={muteCombatAudio}
                        onChange={() => setMuteCombatAudio(!muteCombatAudio)}
                        color={accents.operations.accent}
                        styles={s}
                    />
                    <div style={s.description}>{l.t("UI_SETTINGS_MUTE_COMBAT_DESC")}</div>
                </DisabledOverlay>
            </div>
        </div>
    );
};

export const SettingsInterfaceContent = memo(SettingsInterfaceContentComponent);
SettingsInterfaceContent.displayName = "SettingsInterfaceContent";
