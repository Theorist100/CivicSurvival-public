/**
 * SETTINGS → REPORT view: error report actions and the crash-dump submit list.
 */

import React, { memo, useCallback, useState } from "react";
import { Row } from "../coherent";
import { getButtonStyles } from "../../themes";
import { useSettingsActions } from "../../hooks/actions";
import { useLocale } from "../../locales";
import { useSettingsDto, useSettingsStyles } from "./settingsShared";

// Each native dump is ~30-50 MB; cap how many a player can send at once. Mirrors the C# cap in
// ErrorReportService.MAX_CRASH_DUMPS_PER_SUBMIT.
const MAX_CRASH_DUMPS_SELECTED = 3;

const SettingsDebugContentComponent: React.FC = () => {
    const l = useLocale();
    const { theme, accents, s } = useSettingsStyles();
    const settings = useSettingsDto();
    const { clearErrors, copyReport, sendCrashDumps, sendModLog, sendReport } = useSettingsActions();

    // Two-click consent for the crash-dump send: the dump is raw process memory, so the player
    // must affirm after reading the warning before it leaves the machine.
    const [dumpConsent, setDumpConsent] = useState(false);
    // Player-selected dump file names. The player may not know which dump is the real crash, so the
    // list is multi-select; a bounded number is sent (each is 30-50 MB).
    const [selectedDumps, setSelectedDumps] = useState<ReadonlySet<string>>(() => new Set());

    const toggleDump = useCallback((name: string) => {
        setSelectedDumps(prev => {
            const next = new Set(prev);
            if (next.has(name)) next.delete(name);
            else next.add(name);
            return next;
        });
    }, []);

    if (!settings) return null;

    const buttonStyles = getButtonStyles(theme, accents);
    const crashDumps = settings.CrashDumps ?? [];
    const selectedSizeMb = crashDumps
        .filter(dump => selectedDumps.has(dump.Name))
        .reduce((sum, dump) => sum + (dump.SizeMb ?? 0), 0);
    const overDumpLimit = selectedDumps.size > MAX_CRASH_DUMPS_SELECTED;
    const canSendDumps = selectedDumps.size > 0 && !overDumpLimit;

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

    const actionButtonStyle = {
        ...buttonStyles.ghost(theme.colors.textSecondary),
        marginRight: theme.spacing.xs,
        marginBottom: theme.spacing.xs,
    };

    return (
        <div style={s.column}>
            <div style={s.section}>
                <div style={s.label}>{l.t("UI_SETTINGS_ERROR_REPORT")}</div>
                {settings.ErrorCount > 0 && (
                    <div style={{ fontSize: theme.typography.sizeXS, color: accents.crisis.accent, marginBottom: theme.spacing.xs }}>
                        {l.t("UI_SETTINGS_ERROR_COUNT", settings.ErrorCount)}
                    </div>
                )}
                <Row style={{ flexWrap: "wrap" }}>
                    <button style={actionButtonStyle} onClick={sendReport}>
                        {l.t("UI_SETTINGS_SEND_REPORT")}
                    </button>
                    <button style={actionButtonStyle} onClick={copyReport}>
                        {l.t("UI_SETTINGS_COPY_REPORT")}
                    </button>
                    <button style={actionButtonStyle} onClick={sendModLog}>
                        {l.t("UI_SETTINGS_SEND_MOD_LIST")}
                    </button>
                    {settings.ErrorCount > 0 && (
                        <button style={actionButtonStyle} onClick={clearErrors}>
                            {l.t("UI_SETTINGS_CLEAR_ERRORS")}
                        </button>
                    )}
                </Row>
                <div style={s.hint}>{l.t("UI_SETTINGS_ERROR_REPORT_HINT")}</div>
                {settings.ReportStatusKey && (
                    <div style={s.status}>{l.tDynamic(settings.ReportStatusKey)}</div>
                )}
            </div>

            <div style={s.section}>
                <div style={s.label}>{l.t("UI_SETTINGS_CRASH_DUMP")}</div>
                <div style={{ fontSize: theme.typography.sizeXS, color: theme.colors.textSecondary, marginBottom: theme.spacing.xs }}>
                    {l.t("UI_SETTINGS_CRASH_DUMP_HINT")}
                </div>

                {crashDumps.length === 0 ? (
                    <div style={{ fontSize: theme.typography.sizeXS, color: theme.colors.textMuted }}>
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

                        <div style={s.hint}>
                            {l.t("UI_SETTINGS_CRASH_DUMP_SELECTED", selectedDumps.size, selectedSizeMb.toFixed(1))}
                        </div>
                        {overDumpLimit && (
                            <div style={s.error}>
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
                                        style={actionButtonStyle}
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
                    <div style={s.status}>{l.tDynamic(settings.ReportStatusKey)}</div>
                )}
            </div>
        </div>
    );
};

export const SettingsDebugContent = memo(SettingsDebugContentComponent);
SettingsDebugContent.displayName = "SettingsDebugContent";
