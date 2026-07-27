/**
 * StratumCard — a single stratum drawn as a dot cloud (the crowd, not a bar).
 * Dominance reads off the mass colour; an active raid clumps enemy dots into a
 * contagion head + marks a corner raid badge. Width ∝ household count.
 */

import React, { memo, useMemo } from "react";
import { Column, Row } from "../../../../coherent";
import { useTheme, useAccents, hexToRgba } from "../../../../../themes";
import { useLocale } from "../../../../../locales";
import { IconArrowUp, IconArrowDown, IconArrowRight, IconAlert, IconCheck } from "../../../../shared/common/Icons";
import {
    type Axis, type Stratum, type ArchetypeId, dotCloud, shareOf, trendOf, formatHouseholds, raidStatus, raidAccentState,
    RAID_TYPE_LABEL, ARCHETYPES, COUNTER_ARCHETYPE_BY_TYPE,
} from "./opinionData";
import { allegianceColor, trendColor, raidAccent } from "./opinionStyles";
import { Sep } from "./Sep";

interface StratumCardProps {
    axis: Axis;
    stratum: Stratum;
    selected: boolean;
    householdsPerDot: number;
    dimDoubters: boolean;
    /** The currently deployed hero archetype, or null when none is deployed. */
    currentArchetypeId: ArchetypeId | null;
    /**
     * Live contacts whose target seam is still fogged. They attach to NO card (the board
     * must not leak the enemy's aim), so without this the cards would all claim "no incoming
     * ops" while the INTEL list shows raids in the air.
     */
    seamUnknownCount: number;
    onSelect: (id: string) => void;
}

const TREND_ICON = {
    holding: IconArrowUp,
    enemy: IconArrowDown,
    contested: IconArrowRight,
} as const;

/**
 * Counter slot content — the coverage-teaching chip (contained / covered / mismatch),
 * the muted unknown-carrier chip, or the calm no-threat state. Always renders a chip
 * of the same shape so the card height stays fixed.
 */
const CounterSlot = ({ raid, neededId, neededNameKey, currentNameKey, currentArchetypeId, seamUnknownCount, chipStyle }: {
    raid: Stratum["raid"];
    neededId: ArchetypeId | undefined;
    neededNameKey: (typeof ARCHETYPES)[number]["nameKey"] | undefined;
    currentNameKey: (typeof ARCHETYPES)[number]["nameKey"] | undefined;
    currentArchetypeId: ArchetypeId | null;
    /** Live contacts whose SEAM is still fogged — they belong to no card, but they exist. */
    seamUnknownCount: number;
    chipStyle: React.CSSProperties;
}) => {
    const theme = useTheme();
    const accents = useAccents();
    const l = useLocale();

    if (raid && neededId && neededNameKey) {
        // Held/contained raid — under control, not a hero mismatch.
        const contained = raid.held;
        // The deployed hero hard-counters this attack's carrier.
        const covered = currentArchetypeId === neededId;
        const chipColor = contained ? theme.colors.textMuted
            : covered ? theme.colors.success
                : theme.colors.error;
        const ChipIcon = contained || covered ? IconCheck : IconAlert;
        return (
            <span style={{
                ...chipStyle,
                color: chipColor,
                border: `1rem solid ${hexToRgba(chipColor, contained ? 0.32 : 0.42)}`,
                backgroundColor: hexToRgba(chipColor, contained ? 0.06 : 0.1),
            }}>
                <span style={{ marginRight: "4rem", display: "flex" }}><ChipIcon /></span>
                {contained ? (
                    l.t("UI_OB_CTR_CONTAINED")
                ) : covered ? (
                    // Covered: current === needed, so needed's name is the deployed hero.
                    l.t("UI_OB_CTR_COVERS", l.t(neededNameKey))
                ) : (
                    <>
                        {l.t("UI_OB_CTR_NEED", l.t(neededNameKey))}
                        <Sep />
                        <span style={{ color: theme.colors.textMuted }}>
                            {currentNameKey
                                ? l.t("UI_OB_CTR_NOW", l.t(currentNameKey))
                                : l.t("UI_OB_CTR_NO_HERO")}
                        </span>
                    </>
                )}
            </span>
        );
    }

    // A raid with an unread carrier keeps the slot (stable card layout) but holds it
    // muted — no counter advice can be derived from an unknown type.
    const unknownCarrier = raid !== undefined && raid !== null;
    if (unknownCarrier) {
        return (
            <span style={{
                ...chipStyle,
                color: theme.colors.textMuted,
                border: `1rem solid ${theme.colors.border}`,
                backgroundColor: "transparent",
            }}>
                <span style={{ marginRight: "4rem", display: "flex" }}><IconAlert /></span>
                {l.t("UI_OB_RAID_UNKNOWN")}
            </span>
        );
    }

    // No raid is attached to THIS card — but a contact whose seam is still fogged belongs to
    // no card at all (buildLiveWealthAxis only attaches revealed ones, so the enemy's aim is
    // not leaked). Saying "no incoming ops" while such contacts are in the air states SAFETY
    // where we only have IGNORANCE — the INTEL list shows them plainly. So the calm chip is
    // held back until the sky is genuinely empty; until then it names the raids without
    // naming their target, which is exactly what raising intel buys.
    if (seamUnknownCount > 0) {
        const warn = accents.resilience.accent;
        return (
            <span style={{
                ...chipStyle,
                color: warn,
                border: `1rem solid ${hexToRgba(warn, 0.42)}`,
                backgroundColor: hexToRgba(warn, 0.1),
            }}>
                <span style={{ marginRight: "4rem", display: "flex" }}><IconAlert /></span>
                {l.t("UI_OB_CTR_SEAM_UNKNOWN", seamUnknownCount)}
            </span>
        );
    }

    return (
        <span style={{
            ...chipStyle,
            color: theme.colors.textMuted,
            border: `1rem solid ${theme.colors.border}`,
            backgroundColor: "transparent",
        }}>
            <span style={{ marginRight: "4rem", display: "flex" }}><IconCheck /></span>
            {l.t("UI_OB_CTR_NO_THREAT")}
        </span>
    );
};

export const StratumCard = memo(({
    axis, stratum, selected, householdsPerDot, dimDoubters, currentArchetypeId, seamUnknownCount, onSelect,
}: StratumCardProps) => {
    const theme = useTheme();
    const accents = useAccents();
    const l = useLocale();

    // Count 0 = the resolver has not classified this stratum yet (or it is genuinely
    // empty). Render an honest "classifying…" placeholder instead of a fake single-dot
    // crowd or 0/0 allegiance numbers.
    const classifying = stratum.count === 0;
    const share = shareOf(axis, stratum);
    const trend = trendOf(stratum);
    const tColor = trendColor(trend, theme, accents);
    const TrendIcon = TREND_ICON[trend];
    // While classifying there is no allegiance read yet — a muted tag, not a "winning" green
    // (0 > 0 is false, which would otherwise paint an unclassified stratum success).
    const tagColor = classifying ? theme.colors.textMuted
        : stratum.enemy > stratum.you ? theme.colors.error : theme.colors.success;
    const control = accents.crisis.accent;

    const dots = useMemo(
        () => dotCloud(stratum, householdsPerDot),
        [stratum, householdsPerDot]
    );

    const s = useMemo(() => ({
        card: {
            // Width ∝ √(household count): bigger strata read wider, but the grow
            // ratio is compressed so a dominant stratum (e.g. 84% of the city) no
            // longer sprawls across the row while the small ones are pinched — √
            // pulls 94k/6k ≈ 15.7 down to ≈ 3.9. The three cards' grow values
            // sum-share the row.
            flex: `${Math.sqrt(stratum.count)} 1 168rem`,
            minWidth: "168rem",
            boxSizing: "border-box" as const,
            // Flex column with a FIXED-ish minHeight and a flex:1 crowd (basis 0)
            // below. The crowd's zero basis means it does NOT inflate the card's
            // preferred height — the card stays this compact fixed height and the
            // crowd merely fills the middle, pushing the footer to the bottom.
            // (Dropping flex:1 from the crowd, as an earlier refactor did, let its
            // buggy flex-wrap intrinsic height stretch the card into a tall band.)
            display: "flex",
            flexDirection: "column" as const,
            minHeight: "180rem",
            padding: theme.spacing.md,
            textAlign: "left" as const,
            // Only the selection ring: crimson when this stratum is selected (its
            // countermeasures show below), a neutral border otherwise. The former
            // green/red "who-leads" left spine is dropped — the footer's You/Enemy
            // figures and the trend arrow already carry that read.
            border: selected ? `3rem solid ${control}` : `2rem solid ${theme.colors.border}`,
            borderRadius: theme.layout.borderRadiusLg,
            backgroundColor: selected ? theme.colors.paperHover : theme.colors.surface,
            cursor: "pointer",
            transition: "border-color 0.12s ease, background-color 0.12s ease",
        } as React.CSSProperties,
        name: {
            fontSize: theme.typography.sizeSM,
            fontWeight: 800,
            color: theme.colors.textPrimary,
            letterSpacing: "-0.2rem",
        } as React.CSSProperties,
        tag: {
            padding: "1rem 5rem",
            fontSize: theme.typography.sizeXS,
            fontWeight: 700,
            fontFamily: theme.typography.fontFamilyMono,
            color: tagColor,
            backgroundColor: hexToRgba(tagColor, 0.14),
            borderRadius: theme.layout.borderRadius,
            textTransform: "uppercase" as const,
            marginLeft: "6rem",
        } as React.CSSProperties,
        share: {
            fontSize: theme.typography.sizeXS,
            fontFamily: theme.typography.fontFamilyMono,
            color: theme.colors.textMuted,
        } as React.CSSProperties,
        cloud: {
            // DEFINITE height (not flex:1, not intrinsic): Coherent's old Chromium
            // mis-sizes a flex-wrap box's intrinsic height (as if every dot stacked
            // in one column) AND ignores flex-basis:0, so any content-driven height
            // inflated the card into a tall band. A fixed height + overflow:hidden
            // gives the crowd a stable frame — dots pack top-left, rare overflow of
            // a huge stratum clips its grey tail instead of stretching the card.
            display: "flex",
            flexWrap: "wrap" as const,
            alignContent: "flex-start" as const,
            // Kept deliberately short so the cards region stays compact and the
            // context row below (countermeasures + network rail) gets the height it
            // needs — the propaganda-center controls no longer clip. A big stratum
            // clips its grey tail rather than growing the card.
            height: "78rem",
            overflow: "hidden",
            margin: `${theme.spacing.sm} 0`,
        } as React.CSSProperties,
        dot: (kind: "you" | "neu" | "enemy") => ({
            width: "8rem",
            height: "8rem",
            margin: "2.5rem",
            borderRadius: "5rem",
            backgroundColor: allegianceColor(kind, theme),
            opacity: dimDoubters && kind === "neu" ? 0.25 : 1,
        }) as React.CSSProperties,
        footer: {
            // Trend + You/Enemy block. It follows the crowd directly (the card is
            // content-height, packed top-down), so a small stratum yields a short
            // card instead of a tall one with a black band above the footer.
            marginTop: theme.spacing.xs,
            fontSize: theme.typography.sizeXS,
            fontFamily: theme.typography.fontFamilyMono,
            fontWeight: 700,
        } as React.CSSProperties,
        raidBadge: {
            display: "flex",
            alignItems: "center",
            flexWrap: "wrap" as const,
            minWidth: 0,
            padding: "1rem 5rem",
            fontSize: theme.typography.sizeXS,
            fontWeight: 700,
            fontFamily: theme.typography.fontFamilyMono,
            borderRadius: theme.layout.borderRadius,
            textTransform: "uppercase" as const,
        } as React.CSSProperties,
        counterChip: {
            display: "flex",
            alignItems: "center",
            minWidth: 0,
            padding: "1rem 5rem",
            fontSize: theme.typography.sizeXS,
            fontWeight: 700,
            fontFamily: theme.typography.fontFamilyMono,
            borderRadius: theme.layout.borderRadius,
            textTransform: "uppercase" as const,
        } as React.CSSProperties,
    }), [theme, selected, control, tagColor, dimDoubters, stratum.count]);

    const raid = stratum.raid;
    // Colour contract (raidAccentState): landed-blunted green, landed-clean red, in-flight amber.
    const raidColor = raid ? raidAccent(raidAccentState(raid), theme, accents) : theme.colors.border;
    // Response relative to the currently deployed hero (one global lever): the hero
    // that hard-counters this attack vs the one actually deployed. Reuses the
    // countermeasures type→hero map, so the card teaches coverage without opening
    // the panel — change the hero and the covered/mismatch state flips across cards.
    // Gated on `known`: an unread carrier must not leak its counter recommendation.
    const neededId = raid && raid.known ? COUNTER_ARCHETYPE_BY_TYPE[raid.type] : undefined;
    const neededNameKey = neededId
        ? ARCHETYPES.find((a) => a.id === neededId)?.nameKey
        : undefined;
    const currentNameKey = currentArchetypeId
        ? ARCHETYPES.find((a) => a.id === currentArchetypeId)?.nameKey
        : undefined;

    return (
        // A div, not a <button>: Coherent's old Chromium lays a <button>'s content
        // out as a stretching flex box that ignores display:block, which vertically
        // inflated the dot cloud and pushed the footer to the card bottom. A plain
        // block div shrink-wraps its content, so the card is exactly as tall as it
        // needs to be.
        <div
            style={s.card}
            role="button"
            tabIndex={0}
            onClick={() => onSelect(stratum.id)}
            onKeyDown={(e) => { if (e.key === "Enter" || e.key === " ") onSelect(stratum.id); }}
        >
            {/* Header, raid + counter slots, then the crowd — all direct children
                of the flex-column card so the flex:1 crowd pushes the footer down. */}
            <Row justify="space-between" align="center" wrap="wrap" gap={theme.spacing.xs}>
                    <Row align="center" wrap="wrap">
                        <span style={s.name}>{l.t(stratum.nameKey)}</span>
                        <span style={s.tag}>{l.t(stratum.tagKey)}</span>
                    </Row>
                    <span style={{ ...s.share, whiteSpace: "nowrap" }}>{share}%<Sep />{formatHouseholds(stratum.count)}</span>
                </Row>

                {/* Raid slot — always present. A live raid shows the alert badge;
                    with no incoming operation the same box shows a calm, honest
                    "clear" state (muted, not a fabricated attack). The slot never
                    appears/disappears, so the card height stays fixed. */}
                <Row align="center" style={{ marginTop: theme.spacing.xs }}>
                    {raid ? (
                        <span style={{ ...s.raidBadge, color: raidColor, backgroundColor: "transparent", border: `1rem solid ${raidColor}` }}>
                            <span style={{ marginRight: "4rem", display: "flex" }}><IconAlert /></span>
                            {raid.known ? l.t(RAID_TYPE_LABEL[raid.type]) : l.t("UI_OB_RAID_UNKNOWN")}
                            {(() => {
                                const status = raidStatus(raid);
                                return <><Sep />{l.t(status.key, ...(status.arg !== undefined ? [status.arg] : []))}</>;
                            })()}
                        </span>
                    ) : (
                        <span style={{ ...s.raidBadge, color: theme.colors.textMuted, backgroundColor: "transparent", border: `1rem solid ${theme.colors.border}` }}>
                            <span style={{ marginRight: "4rem", display: "flex" }}><IconCheck /></span>
                            {l.t("UI_OB_RAID_CLEAR")}
                        </span>
                    )}
                </Row>

                {/* Counter slot — always present. With a raid it teaches coverage
                    (covered / mismatch / contained); with no raid it holds a calm
                    muted "no incoming ops" state in the same chip shape. */}
                <Row align="center" style={{ marginTop: theme.spacing.xs }}>
                    <CounterSlot
                        raid={raid}
                        neededId={neededId}
                        neededNameKey={neededNameKey}
                        currentNameKey={currentNameKey}
                        currentArchetypeId={currentArchetypeId}
                        seamUnknownCount={seamUnknownCount}
                        chipStyle={s.counterChip}
                    />
                </Row>

                <div style={s.cloud}>
                    {classifying ? (
                        <span style={{
                            fontSize: theme.typography.sizeXS,
                            fontFamily: theme.typography.fontFamilyMono,
                            color: theme.colors.textMuted,
                            alignSelf: "center",
                        }}>
                            {l.t("UI_OB_CLASSIFYING")}
                        </span>
                    ) : (
                        dots.map((kind, i) => (
                            <div key={`${stratum.id}-${i}`} style={s.dot(kind)} />
                        ))
                    )}
                </div>

            {/* Two lines — trend above, the You / Enemy pair on its own nowrap
                line (narrow strata used to wrap). The slot always renders; while
                classifying it shows an explicit muted placeholder ("Classifying…"
                + em-dash values) rather than a false 0/0 or a hidden footer. */}
            <Column gap={theme.spacing.xs} style={s.footer}>
                <Row align="center" style={{ color: classifying ? theme.colors.textMuted : tColor, whiteSpace: "nowrap" }}>
                    {classifying ? (
                        <span>{l.t("UI_OB_CLASSIFYING")}</span>
                    ) : (
                        <>
                            <span style={{ marginRight: "3rem", display: "flex" }}><TrendIcon /></span>
                            {l.t(
                                trend === "holding" ? "UI_OB_TREND_HOLDING"
                                    : trend === "enemy" ? "UI_OB_TREND_ENEMY"
                                        : "UI_OB_TREND_CONTESTED"
                            )}
                        </>
                    )}
                </Row>
                <Row justify="space-between" align="center" gap={theme.spacing.xs} style={{ whiteSpace: "nowrap" }}>
                    <span style={{ color: classifying ? theme.colors.textMuted : theme.colors.success }}>{classifying ? `${l.t("UI_OB_YOU")} —` : `${l.t("UI_OB_YOU")} ${stratum.you}%`}</span>
                    <span style={{ color: classifying ? theme.colors.textMuted : theme.colors.error }}>{classifying ? `${l.t("UI_OB_ENEMY")} —` : `${l.t("UI_OB_ENEMY")} ${stratum.enemy}%`}</span>
                </Row>
            </Column>
        </div>
    );
});

StratumCard.displayName = "StratumCard";
