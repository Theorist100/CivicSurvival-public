/**
 * SETTINGS → SCENARIO view: difficulty preset and the advanced rule toggles.
 */

import React, { memo } from "react";
import { useSettingsActions } from "../../hooks/actions";
import { useLocale } from "../../locales";
import { isDifficultyPresetId, type DifficultyPresetId } from "../../types/semantic";
import { SegmentedTabs } from "../shared/ui";
import { SettingsToggleRow, useSettingsDto, useSettingsStyles } from "./settingsShared";

const DIFFICULTY_LABEL_KEYS = {
    0: "PRESET_MANAGEDDEFICIT",
    1: "PRESET_BLACKOUTPROTOCOL",
    2: "PRESET_ISLANDMODE",
    3: "PRESET_CUSTOM",
} as const;

const SettingsScenarioContentComponent: React.FC = () => {
    const l = useLocale();
    const { accents, s } = useSettingsStyles();
    const settings = useSettingsDto();
    const {
        setBackupPower,
        setConstructionDelay,
        setDifficultyPreset,
        setNeighborEnvy,
        setProtectCriticalInfra,
        setRandomDisasters,
        setWinterMultiplier,
    } = useSettingsActions();

    if (!settings) return null;

    const rawDifficultyPreset = settings.DifficultyPreset ?? 0;
    const difficultyPreset: DifficultyPresetId = isDifficultyPresetId(rawDifficultyPreset) ? rawDifficultyPreset : 0;
    const difficultyOptions = ([0, 1, 2, 3] as DifficultyPresetId[])
        .map(value => ({ value, label: l.t(DIFFICULTY_LABEL_KEYS[value]) }));

    return (
        <div style={s.column}>
            <div style={s.section}>
                <div style={s.label}>{l.t("LABEL_DIFFICULTY")}</div>
                <SegmentedTabs
                    options={difficultyOptions}
                    value={difficultyPreset}
                    onChange={setDifficultyPreset}
                    color={accents.crisis.accent}
                />
                <div style={s.description}>
                    {`${l.t("LABEL_IMPORT_CAP")}: ${settings.LegalImportMW}`}
                </div>
                <div style={s.description}>
                    {`${l.t("LABEL_EXPORT_CAP")}: ${settings.LegalExportMW}`}
                </div>
            </div>

            <div style={s.section}>
                <div style={s.label}>{l.t("LABEL_ADVANCED_SETTINGS")}</div>
                <SettingsToggleRow
                    label={l.t("LABEL_BUILD_DELAY")}
                    checked={settings.ConstructionDelay}
                    onChange={() => setConstructionDelay(!settings.ConstructionDelay)}
                    styles={s}
                />
                <SettingsToggleRow
                    label={l.t("LABEL_DISASTERS")}
                    checked={settings.RandomDisasters}
                    onChange={() => setRandomDisasters(!settings.RandomDisasters)}
                    styles={s}
                />
                <SettingsToggleRow
                    label={l.t("LABEL_WINTER")}
                    checked={settings.WinterMultiplier}
                    onChange={() => setWinterMultiplier(!settings.WinterMultiplier)}
                    styles={s}
                />
                <SettingsToggleRow
                    label={l.t("LABEL_NEIGHBOR_ENVY")}
                    checked={settings.NeighborEnvy}
                    onChange={() => setNeighborEnvy(!settings.NeighborEnvy)}
                    styles={s}
                />
                <SettingsToggleRow
                    label={l.t("LABEL_BACKUP_POWER")}
                    checked={settings.BackupPower}
                    onChange={() => setBackupPower(!settings.BackupPower)}
                    styles={s}
                />
                <SettingsToggleRow
                    label={l.t("LABEL_PROTECT_CRITICAL_INFRA")}
                    checked={settings.ProtectCriticalInfra}
                    onChange={() => setProtectCriticalInfra(!settings.ProtectCriticalInfra)}
                    styles={s}
                />
            </div>
        </div>
    );
};

export const SettingsScenarioContent = memo(SettingsScenarioContentComponent);
SettingsScenarioContent.displayName = "SettingsScenarioContent";
