/**
 * Scenario tab — state injection for scenario testing.
 * Split into sub-tabs: Presets, Military, Grid, Sweep, Social, Intel.
 */

import React, { useState, useMemo, useCallback } from "react";
import { triggerCivic } from "@hooks/typedTrigger";
import { B } from "../../hooks/bindingNames.generated";
import { SliderRow, StatRow } from "@shared/ui";
import type { Theme } from "../../themes";
import type { CrisisSweepDto } from "../../types/domainDtos";
import { type TabProps } from "./debugPanelShared";
import { type ScenarioSubTab } from "./ScenarioTab.config";
import { ScenarioPresetsPanel, ScenarioSubTabBar } from "./ScenarioTabControls";
import { DebugSectionTitle } from "./DevtoolsPrimitives";
import { useSyncedSlider } from "./useSyncedSlider";

type ScenarioSliderId = "pressure" | "stress" | "shock" | "morale" | "integrity" | "corruption" | "trust";
type SliderDragProps = {
    disabled: boolean;
    onDragStart: () => void;
    onDragEnd: () => void;
};

// ============================================================================
// CRISIS SWEEP VERDICT
// ============================================================================

const SWEEP_MODE_INVARIANT = 0;
const SWEEP_MODE_PACING = 1;
const SWEEP_MODE_SEVERITY = 2;

interface CrisisSweepVerdictProps {
    sweep: CrisisSweepDto;
    theme: Theme;
    successColor: string;
    errorColor: string;
    warningColor: string;
}

const pct = (v: number) => `${v.toFixed(1)}%`;
const hours = (v: number) => `${v.toFixed(1)}h`;
// Phase F repair-funding pot label (mirrors CrisisSweepRequest.RepairTier: 1 = municipal, 2 = shadow).
const repairTierLabel = (t: number) => (t === 2 ? "shadow" : t === 1 ? "municipal" : "none");

interface CrisisSweepBranchesProps {
    sweep: CrisisSweepDto;
    textColor: string;
    warningColor: string;
    successColor: string;
    errorColor: string;
    // Recovery rows are only meaningful for invariant / severity (modes that drive a survivability
    // verdict); pacing has no Patriot branches, so it suppresses them.
    showRecovery: boolean;
}

// Both Patriot branches side by side — the sweep is a DECISION tool: it computes both options every
// run so the player can weigh the trade-off without flipping the live toggle. Branch A = "Patriot →
// Ballistic only" (weaker drones, stronger ballistic shield); branch B = "Patriot → Mixed" (stronger
// drones, ballistic shield down to the Gepard backstop). The branch matching the live toggle is
// marked "(current)". Each branch shows its OWN recovery% + recoverable verdict — folding the leaked-
// ballistic damage in, so Mixed is no longer automatically the better branch. Severity reuses this
// with the campaign-mean values in the same DTO fields.
const CrisisSweepBranches: React.FC<CrisisSweepBranchesProps> = ({ sweep, textColor, warningColor, successColor, errorColor, showRecovery }) => {
    const hasBallistic = sweep.BallisticTargets > 0;
    const ballisticOnlyActive = !sweep.PatriotInterceptsDrones;
    const ballisticText = (v: number) => (hasBallistic ? pct(v) : "—");
    const recoveryText = (recovery: number, recoverable: boolean) =>
        ` / recovery ${pct(recovery)} (${recoverable ? "OK" : "NO"})`;
    return (
        <>
            <StatRow
                label={`Patriot → Ballistic${ballisticOnlyActive ? " (current)" : ""}`}
                value={`drone ${pct(sweep.DroneInterceptBallisticOnly)} / ballistic ${ballisticText(sweep.BallisticInterceptBallisticOnly)}${showRecovery ? recoveryText(sweep.WorstCaseRecoveryBallisticOnly, sweep.IsRecoverableBallisticOnly) : ""}`}
                color={ballisticOnlyActive ? successColor : textColor}
            />
            <StatRow
                label={`Patriot → Mixed${ballisticOnlyActive ? "" : " (current)"}`}
                value={`drone ${pct(sweep.DroneInterceptMixed)} / ballistic ${ballisticText(sweep.BallisticInterceptMixed)}${showRecovery ? recoveryText(sweep.WorstCaseRecoveryMixed, sweep.IsRecoverableMixed) : ""}`}
                color={ballisticOnlyActive ? textColor : successColor}
            />
            {showRecovery && !sweep.IsRecoverableBallisticOnly && !sweep.IsRecoverableMixed && (
                <StatRow label="Survivability" value="neither branch recoverable" color={errorColor} />
            )}
            {hasBallistic && (
                <>
                    <StatRow label="Ballistic Targets / Wave" value={`${sweep.BallisticTargets}`} color={textColor} />
                    <StatRow
                        label="Missiles → Drones (branch B)"
                        value={`${sweep.MissilesSpentOnDrones} (Patriot magazine leaves ballistics)`}
                        color={warningColor}
                    />
                </>
            )}
        </>
    );
};

const CrisisSweepVerdict: React.FC<CrisisSweepVerdictProps> = ({ sweep, theme, successColor, errorColor, warningColor }) => {
    const textColor = theme.colors.textPrimary;
    const mutedColor = theme.colors.textMuted;

    if (!sweep.HasResult) {
        return <StatRow label="Verdict" value="no result yet — run a sweep" color={mutedColor} />;
    }

    const header = (
        <>
            <StatRow label="Computed At" value={`${sweep.ComputedAtGameHours.toFixed(1)}h`} color={mutedColor} />
            <StatRow label="Archetype" value={`#${sweep.ArchetypeId}`} color={textColor} />
            <StatRow label="Pop Peak" value={sweep.PopulationPeak.toLocaleString()} color={textColor} />
            <StatRow label="War Day" value={`${sweep.WarDay}`} color={textColor} />
        </>
    );

    if (sweep.Mode === SWEEP_MODE_INVARIANT) {
        const fleetLive = sweep.AaHeritage + sweep.AaBofors + sweep.AaGepard + sweep.AaPatriot > 0;
        return (
            <>
                {header}
                <StatRow label="Mode" value="deterministic worst-case strike snapshot" color={mutedColor} />
                <StatRow label="Grace Window" value={hours(sweep.GraceWindowHours)} color={textColor} />
                {/* Both Patriot branches side by side — the decision the player weighs. Each branch shows
                    its OWN recovery% + recoverable verdict (toggle-free: the survivability verdict no
                    longer flips with the live toggle). For a Patriot-less fleet the two rows are identical
                    (no ballistic line), so a single recovery row is shown instead. */}
                {fleetLive ? (
                    <CrisisSweepBranches
                        sweep={sweep}
                        textColor={textColor}
                        warningColor={warningColor}
                        successColor={successColor}
                        errorColor={errorColor}
                        showRecovery
                    />
                ) : (
                    <>
                        <StatRow label="Two-Layer Intercept" value={pct(sweep.DroneInterceptBallisticOnly)} color={textColor} />
                        <StatRow
                            label="Recoverable"
                            value={sweep.IsRecoverableBallisticOnly ? "YES" : "NO"}
                            color={sweep.IsRecoverableBallisticOnly ? successColor : errorColor}
                        />
                        <StatRow label="Worst-Case Recovery" value={pct(sweep.WorstCaseRecoveryBallisticOnly)} color={textColor} />
                    </>
                )}
                <StatRow label="Free Heritage Grant" value={`${sweep.FreeHeritageGrant}`} color={textColor} />
                <StatRow label="Operational AA" value={`${sweep.OperationalAaAtVerdict}`} color={textColor} />
                <StatRow label="Coverage" value={pct(sweep.CoveragePct)} color={textColor} />
                {sweep.AreaKm2 > 0 && (
                    <StatRow label="Area" value={`${sweep.AreaKm2.toFixed(2)} km²`} color={textColor} />
                )}
                {fleetLive && (
                    <>
                        <StatRow label="AA Heritage" value={sweep.AaHeritage.toLocaleString()} color={textColor} />
                        <StatRow label="AA Bofors" value={sweep.AaBofors.toLocaleString()} color={textColor} />
                        <StatRow label="AA Gepard" value={sweep.AaGepard.toLocaleString()} color={textColor} />
                        <StatRow label="AA Patriot" value={sweep.AaPatriot.toLocaleString()} color={textColor} />
                    </>
                )}
                {(sweep.ManpowerTotal > 0 || sweep.ManpowerUsed > 0 || sweep.ManpowerCasualties > 0 || sweep.ManpowerAvailable > 0) && (
                    <>
                        <StatRow label="Manpower Total" value={sweep.ManpowerTotal.toLocaleString()} color={textColor} />
                        <StatRow label="Manpower Used" value={sweep.ManpowerUsed.toLocaleString()} color={textColor} />
                        <StatRow label="Manpower Casualties" value={sweep.ManpowerCasualties.toLocaleString()} color={textColor} />
                        <StatRow label="Manpower Available" value={sweep.ManpowerAvailable.toLocaleString()} color={textColor} />
                    </>
                )}
            </>
        );
    }

    if (sweep.Mode === SWEEP_MODE_PACING) {
        return (
            <>
                {header}
                <StatRow label="Mode" value="phase-cycle timing (calm fraction)" color={mutedColor} />
                <StatRow label="Calm Hours" value={hours(sweep.CalmHours)} color={textColor} />
                <StatRow label="Wave Pressure @ Peak" value={pct(sweep.WavePressureAtPeak)} color={textColor} />
            </>
        );
    }

    if (sweep.Mode === SWEEP_MODE_SEVERITY) {
        return (
            <>
                {header}
                <StatRow label="Mode" value="Monte-Carlo campaign with repairs" color={mutedColor} />
                <StatRow label="Samples" value={`${sweep.SampleCount}`} color={textColor} />
                <StatRow label="Blackout Probability" value={pct(sweep.BlackoutProbabilityPct)} color={textColor} />
                <StatRow label="Median Collapse Day" value={`${sweep.MedianCollapseDay}`} color={textColor} />
                <StatRow label="Unsheddable Floor" value={`${sweep.UnsheddableFloorMW} MW`} color={textColor} />
                {/* Campaign-mean of both Patriot branches (same DTO fields as the invariant, averaged
                    over the timeline's waves) — intercept + recovery + recoverable per branch. */}
                <StatRow label="Intercept + recovery (campaign mean)" value="" color={mutedColor} />
                <CrisisSweepBranches
                    sweep={sweep}
                    textColor={textColor}
                    warningColor={warningColor}
                    successColor={successColor}
                    errorColor={errorColor}
                    showRecovery
                />
                <StatRow
                    label="Repair Slots"
                    value={
                        sweep.RepairBudgetLive
                            ? `${sweep.RepairSlots} (${repairTierLabel(sweep.RepairTier)} $${sweep.RepairFundingCash.toLocaleString()})`
                            : `${sweep.RepairSlots} (manual)`
                    }
                    color={textColor}
                />
            </>
        );
    }

    return <StatRow label="Verdict" value={`unknown mode ${sweep.Mode}`} color={errorColor} />;
};

// ============================================================================
// SCENARIO TAB
// ============================================================================

export const ScenarioTab: React.FC<TabProps> = ({ debug, styles, theme, accents }) => {
    const [subTab, setSubTab] = useState<ScenarioSubTab>("presets");
    const [draggingSlider, setDraggingSlider] = useState<ScenarioSliderId | null>(null);
    const raw = debug.raw;
    const scenario = debug.scenario;
    const slidersDisabled = debug.status !== "ready";

    const successColor = debug.colors.success;
    const warningColor = debug.colors.warning;
    const errorColor = debug.colors.error;

    const [stressVal, setStressVal] = useSyncedSlider(raw.scGridStressHours, draggingSlider === "stress");
    const [shockVal, setShockVal] = useSyncedSlider(raw.scShockLevel, draggingSlider === "shock");
    const [corruptionVal, setCorruptionVal] = useSyncedSlider(raw.scCorruptionScore, draggingSlider === "corruption");
    const [integrityVal, setIntegrityVal] = useSyncedSlider(raw.scCityIntegrity, draggingSlider === "integrity");
    const [pressureVal, setPressureVal] = useSyncedSlider(raw.scEnemyPressure, draggingSlider === "pressure");
    const [trustVal, setTrustVal] = useSyncedSlider(raw.scTrustLevel, draggingSlider === "trust");
    const [moraleVal, setMoraleVal] = useSyncedSlider(raw.scMoraleFactor, draggingSlider === "morale");

    const btnDanger = useMemo(() => styles.debugBtn(errorColor), [styles, errorColor]);
    const btnSuccess = useMemo(() => styles.debugBtn(successColor), [styles, successColor]);
    const btnWarn = useMemo(() => styles.debugBtn(warningColor), [styles, warningColor]);

    const handlePreset = useCallback((id: number) => {
        triggerCivic(B.DebugRunPreset, id);
    }, []);
    const sliderDragProps = useMemo<Record<ScenarioSliderId, SliderDragProps>>(() => {
        const makeProps = (id: ScenarioSliderId): SliderDragProps => ({
            disabled: slidersDisabled,
            onDragStart: () => setDraggingSlider(id),
            onDragEnd: () => setDraggingSlider((current) => current === id ? null : current),
        });

        return {
            pressure: makeProps("pressure"),
            stress: makeProps("stress"),
            shock: makeProps("shock"),
            morale: makeProps("morale"),
            integrity: makeProps("integrity"),
            corruption: makeProps("corruption"),
            trust: makeProps("trust"),
        };
    }, [slidersDisabled]);

    return (
        <>
            <ScenarioSubTabBar
                activeTab={subTab}
                theme={theme}
                accents={accents}
                onTabChange={setSubTab}
            />

            {/* === PRESETS === */}
            {subTab === "presets" && (
                <ScenarioPresetsPanel styles={styles} onPreset={handlePreset} />
            )}

            {/* === MILITARY === */}
            {subTab === "military" && (
                <>
                    <div style={styles.section}>
                        <DebugSectionTitle>Wave Status</DebugSectionTitle>
                        <StatRow label="Wave" value={scenario.waveDisplay} color={theme.colors.textPrimary} />
                        <StatRow label="Under Attack" value={scenario.underAttackDisplay} color={scenario.underAttackColor} />
                        <StatRow label="Stance" value={scenario.stanceDisplay} color={warningColor} />
                        <StatRow label="Pressure" value={scenario.enemyPressureDisplay} color={scenario.enemyPressureColor} />
                        <StatRow label="AA Ammo" value={raw.scAaAmmo} color={scenario.aaAmmoColor} />
                    </div>
                    <div style={styles.sectionNoBorder}>
                        <DebugSectionTitle>Controls</DebugSectionTitle>
                        <div style={{ display: "flex", flexWrap: "wrap", marginBottom: "6rem" }}>
                            <button onClick={() => triggerCivic(B.DebugForceWave)} style={{ ...btnDanger, marginRight: "4rem" }}>Force Wave</button>
                            <button onClick={() => triggerCivic(B.DebugTestExplosion)} style={btnWarn}>Test Explosion</button>
                        </div>
                        <SliderRow label="Pressure" value={pressureVal} min={0} max={100} step={5}
                            {...sliderDragProps.pressure}
                            onChange={(v) => { setPressureVal(v); triggerCivic(B.DebugSetEnemyPressure, v); }}
                            format={(v) => `${v.toFixed(0)}%`} />
                    </div>
                </>
            )}

            {/* === POWER GRID === */}
            {subTab === "grid" && (
                <>
                    <div style={styles.section}>
                        <DebugSectionTitle>Grid Status</DebugSectionTitle>
                        <StatRow label="Collapsed" value={scenario.gridCollapsedDisplay} color={scenario.gridCollapsedColor} />
                        <StatRow label="Stress" value={scenario.gridStressDisplay} color={theme.colors.textPrimary} />
                        <StatRow label="Zone" value={raw.scGridZoneName} color={scenario.gridZoneColor} />
                        <StatRow label="Recovery" value={scenario.gridRecoveryDisplay} color={theme.colors.textPrimary} />
                    </div>
                    <div style={styles.sectionNoBorder}>
                        <DebugSectionTitle>Controls</DebugSectionTitle>
                        <div style={{ display: "flex", flexWrap: "wrap", marginBottom: "6rem" }}>
                            <button onClick={() => triggerCivic(B.DebugForceGridCollapse)} style={btnDanger}>Force Collapse</button>
                            <button onClick={() => triggerCivic(B.DebugResetGridStress)} style={btnSuccess}>Reset Stress</button>
                        </div>
                        <SliderRow label="Stress" value={stressVal} min={0} max={5} step={0.1}
                            {...sliderDragProps.stress}
                            onChange={(v) => { setStressVal(v); triggerCivic(B.DebugSetStress, v); }}
                            format={(v) => `${v.toFixed(1)}h`} />
                    </div>
                </>
            )}

            {/* === CRISIS SWEEP (forecast) === */}
            {subTab === "sweep" && (
                <>
                    <div style={styles.section}>
                        <DebugSectionTitle>Crisis Sweep</DebugSectionTitle>
                        {/* In-game crisis model (replaces Tools/crisis_model.py). Verdict goes to
                            the [SWEEP] log line; 0 = invariant, 1 = pacing, 2 = severity. */}
                        <div style={{ display: "flex", flexWrap: "wrap", marginBottom: "6rem" }}>
                            <button onClick={() => triggerCivic(B.TriggerCrisisSweep, 0)} style={{ ...btnSuccess, marginRight: "4rem" }}>Sweep: Invariant</button>
                            <button onClick={() => triggerCivic(B.TriggerCrisisSweep, 1)} style={{ ...btnWarn, marginRight: "4rem" }}>Sweep: Pacing</button>
                            <button onClick={() => triggerCivic(B.TriggerCrisisSweep, 2)} style={btnDanger}>Sweep: Severity</button>
                        </div>
                    </div>
                    <div style={styles.sectionNoBorder}>
                        <DebugSectionTitle>Verdict</DebugSectionTitle>
                        <CrisisSweepVerdict
                            sweep={raw.crisisSweep}
                            theme={theme}
                            successColor={successColor}
                            errorColor={errorColor}
                            warningColor={warningColor}
                        />
                    </div>
                </>
            )}

            {/* === SOCIAL === */}
            {subTab === "social" && (
                <>
                    <div style={styles.section}>
                        <DebugSectionTitle>Shock & Exodus</DebugSectionTitle>
                        <StatRow label="Shock" value={scenario.shockDisplay} color={scenario.shockColor} />
                        <StatRow label="Exodus" value={scenario.exodusDisplay} color={scenario.exodusColor} />
                    </div>
                    <div style={styles.sectionNoBorder}>
                        <DebugSectionTitle>Controls</DebugSectionTitle>
                        <div style={{ display: "flex", flexWrap: "wrap", marginBottom: "6rem" }}>
                            <button onClick={() => triggerCivic(B.DebugToggleExodus)} style={btnWarn}>Toggle Exodus</button>
                        </div>
                        <SliderRow label="Shock" value={shockVal} min={0} max={100} step={5}
                            {...sliderDragProps.shock}
                            onChange={(v) => { setShockVal(v); triggerCivic(B.DebugSetShock, v); }}
                            format={(v) => `${v.toFixed(0)}%`} />
                        <SliderRow label="Morale" value={moraleVal} min={0} max={1} step={0.05}
                            {...sliderDragProps.morale}
                            onChange={(v) => { setMoraleVal(v); triggerCivic(B.DebugSetMoraleFactor, v); }}
                            format={(v) => `${(v * 100).toFixed(0)}%`} />
                    </div>
                </>
            )}

            {/* === INTEL (Info Space + Diplomacy + Time) === */}
            {subTab === "intel" && (
                <>
                    <div style={styles.section}>
                        <DebugSectionTitle>Information Space</DebugSectionTitle>
                        <StatRow label="Infection Rate" value={scenario.infectionRateDisplay} color={scenario.infectionRateColor} />
                        <StatRow label="Integrity" value={scenario.cityIntegrityDisplay} color={theme.colors.textPrimary} />
                        <StatRow label="Media Trust" value={scenario.mediaTrustDisplay} color={scenario.mediaTrustColor} />
                        <StatRow label="Telemarathon" value={scenario.telemarathonDisplay} color={scenario.telemarathonColor} />
                        <SliderRow label="Integrity" value={integrityVal} min={0} max={1} step={0.05}
                            {...sliderDragProps.integrity}
                            onChange={(v) => { setIntegrityVal(v); triggerCivic(B.DebugSetCityIntegrity, v); }}
                            format={(v) => `${(v * 100).toFixed(0)}%`} />
                    </div>
                    <div style={styles.section}>
                        <DebugSectionTitle>Diplomacy & Corruption</DebugSectionTitle>
                        <StatRow label="Trust" value={scenario.trustDisplay} color={scenario.trustColor} />
                        <StatRow label="Score" value={scenario.corruptionScoreDisplay} color={theme.colors.textPrimary} />
                        <StatRow label="Heat (auto)" value={scenario.corruptionHeatDisplay} color={scenario.corruptionHeatColor} />
                        <SliderRow label="Corruption" value={corruptionVal} min={0} max={100} step={5}
                            {...sliderDragProps.corruption}
                            onChange={(v) => { setCorruptionVal(v); triggerCivic(B.DebugSetCorruption, v); }}
                            format={(v) => `${v.toFixed(0)}%`} />
                        <SliderRow label="Trust" value={trustVal} min={0} max={100} step={5}
                            {...sliderDragProps.trust}
                            onChange={(v) => { setTrustVal(v); triggerCivic(B.DebugSetTrust, v); }}
                            format={(v) => v.toFixed(0)} />
                    </div>
                    <div style={styles.sectionNoBorder}>
                        <DebugSectionTitle>Time</DebugSectionTitle>
                        <div style={{ display: "flex", flexWrap: "wrap" }}>
                            <button onClick={() => triggerCivic(B.DebugForceDayChange)} style={{ ...btnWarn, marginRight: "4rem", marginBottom: "4rem" }}>Force Day Change</button>
                        </div>
                    </div>
                </>
            )}
        </>
    );
};
