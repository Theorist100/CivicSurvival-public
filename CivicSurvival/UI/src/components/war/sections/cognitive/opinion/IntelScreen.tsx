/**
 * IntelScreen — the enemy-intelligence picture (military, not demographics):
 * a live "state of the info-war" readout (the harm the enemy's psy raids do),
 * the active raids + interception windows (landed raids name the vector they
 * hit), and the enemy delivery channels. Everything but the channels is LIVE
 * off CognitiveDto / the PsyOpsAttack contacts; enemy channels await Phase 04.
 */

import React, { memo } from "react";
import { Column, Row } from "../../../../coherent";
import { useTheme, useAccents, hexToRgba } from "../../../../../themes";
import { useLocale } from "../../../../../locales";
import { bindingDataOrDefault, isBindingLive, useCognitive, useMobilizationDomain, type CognitiveDto, type MobilizationDto } from "@hooks/domain";
import { DEFAULT_COGNITIVE_DTO, DEFAULT_MOBILIZATION_DTO } from "../../../../../types/domainDtos";
import { LabeledBar, StatTile } from "../../../../shared/ui";
import { HelpSection } from "../../../../shared/common/HelpSection";
import { IconAlert, IconSatellite } from "../../../../shared/common/Icons";
import {
    RAID_TYPE_LABEL, RAID_HARM_LABEL, raidStatus, raidAccentState, fogLabelKey, COUNTER_ARCHETYPE_BY_TYPE, ARCHETYPES,
    ARCHETYPE_BY_INT, forecastTypeLabelKey, forecastStratumNameKey,
} from "./opinionData";
import { useOpinion } from "./useOpinion";
import { createBoardStyles, raidAccent } from "./opinionStyles";
import { RiskRibbon } from "./RiskRibbon";

// Compact a household COUNT for a stat tile ("12463" -> "12.4k"). This is a
// population count, not currency — the money-format analyzer mis-reads the
// k-suffix divide, so it is suppressed on the expression below.
const compactCount = (n: number): string =>
    // eslint-disable-next-line civic/format-money-consistency
    (n >= 1000 ? `${(n / 1000).toFixed(1)}k` : `${n}`);

// AWAITING BACKEND (Phase 04): enemy delivery vectors are sample data.
const SAMPLE_CHANNELS = [
    { id: "bots", labelKey: "UI_OB_CH_BOTS" as const, intensity: 64 },
    { id: "leaflets", labelKey: "UI_OB_CH_LEAFLETS" as const, intensity: 38 },
    { id: "telegram", labelKey: "UI_OB_CH_TELEGRAM" as const, intensity: 71 },
];

interface StateTile {
    label: string;
    value: string;
    sub: string | undefined;
    color: string;
    alert: boolean;
}

interface TilePalette { cGood: string; cBad: string; cWarn: string; cMuted: string }

type StateTiles = [StateTile, StateTile, StateTile, StateTile, StateTile, StateTile];

// The live STATE-OF-THE-INFO-WAR tiles: the DAMAGE the enemy's raids do. Protest,
// commerce, happiness and exodus are the STAKES and live in the RiskRibbon above —
// they used to be shown in both places, which is what pushed this screen past the
// panel height. Two fixed rows of three (no wrapping grid — Coherent flex-wrap
// mis-sizes intrinsic height): city state on top, one tile per raid carrier below
// (Rumor → food panic, FakeVideo → sick-out, Propaganda → draft evasion), so the
// row reads as the "attack type → your pain" map. Extracted so the component body
// stays a flat "live or awaiting" switch (cognitive-complexity lint budget).
const buildLiveStateTiles = (
    cw: CognitiveDto,
    mob: MobilizationDto,
    mobLive: boolean,
    l: ReturnType<typeof useLocale>,
    p: TilePalette,
): StateTiles => {
    const trustPct = Math.round(cw.MediaTrust * 100);
    const integrityPct = Math.round(cw.AvgIntegrity * 100);
    const foodPct = Math.round(Math.max(0, Math.min(1, cw.RumorFoodRunIntensity)) * 100);
    const foodActive = foodPct > 0;
    const compromised = cw.CompromisedDistricts > 0;
    const integrityBad = cw.AvgIntegrity < cw.PenaltyThreshold;
    const sickOutActive = cw.SickLeaveActiveCount > 0;
    // Dodger data rides the Mobilization binding (a different publisher): until it
    // delivers, the tile is an honest muted "—", not a fake zero.
    const dodgerCount = mobLive ? mob.ManpowerDodgerCount : 0;
    const dodgerPct = mobLive ? Math.max(0, 100 - mob.ManpowerDodgerFactor) : 0;
    const dodgersActive = dodgerCount > 0;
    return [
        {
            label: l.t("UI_IW_MEDIA_TRUST"),
            value: `${trustPct}%`,
            sub: undefined,
            color: cw.MediaTrust >= 0.5 ? p.cGood : p.cBad,
            alert: cw.MediaTrust < 0.5,
        },
        {
            label: l.t("UI_OB_TILE_INFECTION"),
            value: compactCount(cw.HouseholdsInfected),
            sub: `${cw.CompromisedDistricts}/${cw.TotalDistricts} ${l.t("UI_OB_TILE_DISTRICTS")}`,
            color: cw.HouseholdsInfected > 0 ? p.cWarn : p.cMuted,
            alert: compromised,
        },
        {
            label: l.t("UI_OB_TILE_INTEGRITY"),
            value: `${integrityPct}%`,
            sub: undefined,
            color: integrityBad ? p.cBad : p.cGood,
            alert: integrityBad,
        },
        {
            label: l.t("UI_OB_TILE_FOODPANIC"),
            value: foodActive ? l.t("STATUS_ACTIVE") : "—",
            sub: foodActive ? `${foodPct}%` : undefined,
            color: foodActive ? p.cBad : p.cGood,
            alert: foodActive,
        },
        {
            label: l.t("UI_OB_TILE_SICKOUT"),
            value: sickOutActive ? l.t("STATUS_ACTIVE") : "—",
            sub: sickOutActive ? l.t("UI_OB_TILE_SICKOUT_SUB", compactCount(cw.SickLeaveActiveCount)) : undefined,
            color: sickOutActive ? p.cBad : p.cGood,
            alert: sickOutActive,
        },
        {
            label: l.t("UI_OB_TILE_DODGERS"),
            value: dodgersActive ? compactCount(dodgerCount) : "—",
            sub: dodgersActive && dodgerPct > 0 ? l.t("UI_OB_TILE_DODGERS_SUB", `${dodgerPct}`) : undefined,
            color: dodgersActive ? p.cBad : (mobLive ? p.cGood : p.cMuted),
            alert: dodgersActive,
        },
    ];
};

export const IntelScreen = memo(() => {
    const theme = useTheme();
    const accents = useAccents();
    const l = useLocale();
    const b = createBoardStyles(theme, accents);
    const cwState = useCognitive();
    const cw = bindingDataOrDefault(cwState, DEFAULT_COGNITIVE_DTO);
    // Until the cognitive feed delivers, every readout below is a constructed
    // default (MediaTrust 0 reads as a red alert, integrity 0% green as "no
    // penalty"). Tiles render muted "—" placeholders instead of fake verdicts.
    const live = isBindingLive(cwState);
    // Draft-evasion tile data rides the Mobilization publisher (Propaganda's vanilla
    // effect lands in manpower, not in CognitiveDto) — its own live flag.
    const mobState = useMobilizationDomain();
    const mob = bindingDataOrDefault(mobState, DEFAULT_MOBILIZATION_DTO);
    const mobLive = isBindingLive(mobState);

    // Live discrete PSYOPS contacts (PsyOpsAttack entities) — display-only.
    const { raids } = useOpinion();

    // ENEMY THREAT / FORECAST (13B) — the next raid's likely carrier + seam, one step ahead, gated
    // by intel fog. Null keys ⇒ "unknown" (fogged / tie / nothing classified). The fog badge reuses
    // the raid fog labels (Signal only / Detected / — when revealed).
    const forecastTypeKey = forecastTypeLabelKey(cw.ForecastType);
    const forecastStratumKey = forecastStratumNameKey(cw.ForecastStratum);
    const forecastFogKey = fogLabelKey(cw.ForecastFog);

    const cGood = theme.colors.success;
    const cBad = accents.crisis.accent;
    const cWarn = accents.resilience.accent;
    const cMuted = theme.colors.textMuted;
    const mono = theme.typography.fontFamilyMono;

    // STATE OF THE INFO-WAR — live consequence readout, all off CognitiveDto
    // (draft evasion off MobilizationDto). Awaiting posture keeps the same six
    // tiles with muted "—" values.
    const liveTiles = buildLiveStateTiles(cw, mob, mobLive, l, { cGood, cBad, cWarn, cMuted });
    const dim = (t: StateTile): StateTile => ({ ...t, value: "—", sub: undefined, color: cMuted, alert: false });
    const stateTiles: StateTiles = live
        ? liveTiles
        : [dim(liveTiles[0]), dim(liveTiles[1]), dim(liveTiles[2]), dim(liveTiles[3]), dim(liveTiles[4]), dim(liveTiles[5])];

    // One stat tile: label + big value + optional sub, flipping crisis-red on alert.
    const tile = (t: StateTile): React.ReactNode => (
        <StatTile label={t.label} value={t.value} sub={t.sub} color={t.color} alertColor={t.alert ? cBad : undefined} />
    );

    // xs gaps + tightened section paddings below: the five stacked regions must
    // fit the fixed 750rem war panel with no outer scroll.
    return (
        <Column gap={theme.spacing.xs} style={{ width: "100%", minHeight: "200rem" }}>
            {/* Stakes / damage band — moved off the PSYOPS operate board to sit above
                the enemy picture it pairs with (the harm the enemy's minds-war does). */}
            <RiskRibbon />

            {/* ENEMY THREAT / FORECAST (13B) — live, intel-gated one-step-ahead read of the enemy's
                next likely carrier + seam. Only meaningful while the info-war is live. The right-side
                note shows the intel fidelity (Signal only / Detected / — once fully revealed). */}
            {cw.CognitiveActive && (
                <div style={b.region}>
                    <Row align="center" justify="space-between" style={b.headerStrip}>
                        <Row align="center">
                            <span style={{ color: theme.colors.error, marginRight: "5rem", display: "flex" }}><IconAlert /></span>
                            <span style={b.eyebrow}>{l.t("UI_OB_INTEL_THREAT")}</span>
                        </Row>
                        {forecastFogKey !== null && <span style={b.sampleNote}>{l.t(forecastFogKey)}</span>}
                    </Row>
                    <Column gap="3rem" style={{ padding: `${theme.spacing.xs} ${theme.spacing.sm}`, minHeight: "24rem" }}>
                        <span style={{ fontSize: theme.typography.sizeSM, color: theme.colors.textSecondary }}>
                            {forecastTypeKey !== null
                                ? l.t("UI_OB_FORECAST_TYPE", l.t(forecastTypeKey))
                                : l.t("UI_OB_FORECAST_UNKNOWN")}
                        </span>
                        {forecastStratumKey !== null && (
                            <span style={{ fontSize: theme.typography.sizeSM, fontFamily: mono, color: accents.resilience.accent }}>
                                {l.t("UI_OB_FORECAST_TARGET", l.t(forecastStratumKey))}
                            </span>
                        )}
                    </Column>
                </div>
            )}

            {/* STATE OF THE INFO-WAR — the DAMAGE the enemy's psy raids do. Two fixed
                rows of three (Coherent flex-wrap mis-sizes intrinsic height, so no
                wrapping grid): city state (media trust, infection, integrity) on top,
                one tile per raid carrier below — Rumor → food panic, FakeVideo →
                sick-out, Propaganda → draft evasion — so the band reads as the
                "attack type → your pain" map. A tile flips crisis-red on its
                threshold. The stakes (protest, commerce, happiness, exodus, network)
                belong to the RiskRibbon above and are NOT repeated here. */}
            <div style={b.region}>
                <Row align="center" style={b.headerStrip}>
                    <span style={b.eyebrow}>{l.t("UI_OB_STATE_TITLE")}</span>
                    <HelpSection id="infowar-state" title={l.t("UI_OB_STATE_TITLE")}>{l.t("HELP_IW_STATE")}</HelpSection>
                </Row>
                <Column gap={theme.spacing.xs} style={{ padding: `${theme.spacing.xs} ${theme.spacing.sm}` }}>
                    <Row gap={theme.spacing.xs} align="stretch">
                        {tile(stateTiles[0])}
                        {tile(stateTiles[1])}
                        {tile(stateTiles[2])}
                    </Row>
                    <Row gap={theme.spacing.xs} align="stretch">
                        {tile(stateTiles[3])}
                        {tile(stateTiles[4])}
                        {tile(stateTiles[5])}
                    </Row>
                </Column>
            </div>

            {/* ACTIVE RAIDS — the "?" explains the whole raid mechanic (flight window, the three
                carriers, why a landed contact keeps working, why only the speaker blunts it). It
                hangs here because this is the section where a contact is actually watched. */}
            <div style={b.region}>
                <Row align="center" style={b.headerStrip}>
                    {/* One string, not text between JSX expressions — cohtml drops the
                        whitespace-only text node and the count glued to the title. */}
                    <span style={b.eyebrow}>{`${l.t("UI_OB_ACTIVE_RAIDS")} (${raids.length})`}</span>
                    <HelpSection id="psyops-raids" title={l.t("UI_OB_ACTIVE_RAIDS")}>{l.t("HELP_PSYOPS_RAIDS")}</HelpSection>
                </Row>
                <Column gap={theme.spacing.xs} style={{ padding: `${theme.spacing.xs} ${theme.spacing.sm}`, minHeight: "30rem" }}>
                    {raids.length === 0 && (
                        <span style={{ fontSize: theme.typography.sizeSM, color: theme.colors.textMuted }}>
                            {/* "No raids" is a claim about the sky; before the feed is live we
                                only know we have no data (same publisher as the cognitive DTO). */}
                            {live ? l.t("UI_OB_NO_RAIDS") : l.t("UI_NO_DATA")}
                        </span>
                    )}
                    {raids.map((raid) => {
                        const landed = raid.phase === 1;
                        // Colour contract: landed-blunted green, landed-clean red, in-flight amber.
                        const color = raidAccent(raidAccentState(raid), theme, accents);
                        const status = raidStatus(raid);
                        // "Countered by {0}" names the speaker of the LANDING MOMENT (the frozen
                        // snapshot), not whoever is active now — history must not mutate.
                        const bluntedByKey = raid.landedByArchetype >= 0
                            ? ARCHETYPES.find((a) => a.id === ARCHETYPE_BY_INT[raid.landedByArchetype])?.nameKey ?? "UI_OB_ARCH_VOICE"
                            : "UI_OB_ARCH_VOICE";
                        const fogKey = fogLabelKey(raid.fog);
                        // Best response for the dominant contact — only when its carrier is read, and
                        // only while it is still IN THE AIR. The shield is decided at the landing
                        // moment, so telling a player to deploy a speaker against a raid that already
                        // landed is advice he cannot act on.
                        const counterKey = !landed && raid.isDominant && raid.known
                            ? ARCHETYPES.find((a) => a.id === COUNTER_ARCHETYPE_BY_TYPE[raid.type])?.nameKey
                            : undefined;
                        return (
                            <Row key={raid.id} align="center" justify="space-between" style={{
                                padding: "4rem 8rem",
                                borderLeft: `4rem solid ${color}`,
                                backgroundColor: hexToRgba(color, 0.06),
                                borderRadius: theme.layout.borderRadius,
                            }}>
                                <Column gap="2rem">
                                    <Row align="center" gap={theme.spacing.xs}>
                                        <span style={{
                                            fontSize: theme.typography.sizeSM, fontWeight: 700, fontFamily: theme.typography.fontFamilyMono,
                                            color, textTransform: "uppercase", letterSpacing: "0.4rem",
                                        }}>{raid.known ? l.t(RAID_TYPE_LABEL[raid.type]) : l.t("UI_OB_RAID_UNKNOWN")}</span>
                                        {fogKey !== null && (
                                            <span style={{
                                                fontSize: theme.typography.sizeSM, fontFamily: theme.typography.fontFamilyMono,
                                                color: theme.colors.textMuted, textTransform: "uppercase", letterSpacing: "0.3rem",
                                                padding: "1rem 4rem", border: `1rem solid ${theme.colors.border}`,
                                                borderRadius: theme.layout.borderRadius,
                                            }}>{l.t(fogKey)}</span>
                                        )}
                                    </Row>
                                    {counterKey !== undefined && (
                                        <span style={{ fontSize: theme.typography.sizeSM, color: accents.resilience.accent, fontFamily: theme.typography.fontFamilyMono }}>
                                            {`${l.t("UI_OB_BEST_RESPONSE")}: ${l.t(counterKey)}`}
                                        </span>
                                    )}
                                    {/* One line for the landing, not two. The verdict (did the speaker
                                        blunt it AT THE LANDING MOMENT — a frozen snapshot, so a later
                                        speaker switch changes neither it nor the name) and the harm
                                        vector it hit are one fact about one contact; magnitudes live in
                                        the STATE readout above. Reach stays its own line — it is a pair
                                        of numbers, not part of the verdict. */}
                                    {landed && raid.known && (
                                        <span style={{ fontSize: theme.typography.sizeSM, fontFamily: mono, color: raid.blunted ? cGood : cBad }}>
                                            {`${raid.blunted
                                                ? l.t("UI_OB_HARM_BLUNTED", l.t(bluntedByKey))
                                                : l.t("UI_OB_HARM_CLEAN")} · ${l.t(RAID_HARM_LABEL[raid.type])}`}
                                        </span>
                                    )}
                                    {landed && raid.known && raid.reachHouseholds > 0 && (
                                        <span style={{ fontSize: theme.typography.sizeSM, fontFamily: mono, color: cMuted }}>
                                            {l.t("UI_OB_HARM_REACH", compactCount(raid.reachHouseholds), compactCount(Math.round(raid.reachHouseholds * raid.heroShield)))}
                                        </span>
                                    )}
                                </Column>
                                <Row align="center" gap={theme.spacing.sm}>
                                    <span style={{ fontSize: theme.typography.sizeSM, color: theme.colors.error }}>
                                        {Math.round(raid.intensity * 100)}%
                                    </span>
                                    <span style={{ fontSize: theme.typography.sizeSM, fontFamily: theme.typography.fontFamilyMono, color }}>
                                        {l.t(status.key, ...(status.arg !== undefined ? [status.arg] : []))}
                                    </span>
                                </Row>
                            </Row>
                        );
                    })}
                </Column>
            </div>

            {/* ENEMY CHANNELS */}
            <div style={b.region}>
                {/* headerStrip is space-between: icon and title must travel as ONE child,
                    otherwise they get pushed to opposite ends of the strip. */}
                <div style={b.headerStrip}>
                    <Row align="center">
                        <span style={{ color: theme.colors.textMuted, marginRight: "5rem", display: "flex" }}><IconSatellite /></span>
                        <span style={b.eyebrow}>{l.t("UI_OB_ENEMY_CHANNELS")}</span>
                    </Row>
                </div>
                <Column gap={theme.spacing.xs} style={{ padding: `${theme.spacing.xs} ${theme.spacing.sm}`, minHeight: "30rem" }}>
                    {/* Label column is pinned (see LabeledBar): "Bot network" / "Мережа ботів" is the
                        longest caption here, and a content-sized column would start its bar further
                        right than the shorter two. */}
                    {SAMPLE_CHANNELS.map((ch) => (
                        <LabeledBar
                            key={ch.id}
                            label={<span style={{
                                fontSize: theme.typography.sizeSM, color: theme.colors.textSecondary,
                                textTransform: "uppercase",
                            }}>{l.t(ch.labelKey)}</span>}
                            value={ch.intensity}
                            color={theme.colors.error}
                            labelWidth="124rem"
                            valueText={`${ch.intensity}%`}
                            valueWidth="40rem"
                        />
                    ))}
                </Column>
            </div>
        </Column>
    );
});

IntelScreen.displayName = "IntelScreen";
