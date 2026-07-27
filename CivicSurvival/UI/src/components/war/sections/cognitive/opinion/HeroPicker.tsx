/**
 * HeroPicker — archetype selector (Voice / Arestovych / Patriot) on top of the
 * LIVE hero controls (OpsTab: deploy/recall/mode, reused verbatim).
 *
 * LIVE (Phase 04): the active archetype is read from the cognitive DTO
 * (HeroDeploymentState.Archetype) and a switch is sent through the existing
 * SetArchetype trigger (HeroActionRequest.SetArchetype) — free while no hero is
 * deployed, cost + cooldown while deployed. Arestovych's trust-debt (live %) and
 * its collapse state are surfaced from the DTO; buff/debuff/best-vs text stays
 * hand-written (no backend field for the copy).
 */

import React, { memo, useMemo } from "react";
import { Column, Row } from "../../../../coherent";
import { useTheme, useAccents, hexToRgba } from "../../../../../themes";
import { useLocale } from "../../../../../locales";
import { useOptimisticChoice, type useCognitiveActions } from "@hooks/actions";
import { bindingDataOrDefault, useCognitive } from "@hooks/domain";
import { HeroStatus } from "@hooks/domain/cognitiveLabels";
import { DEFAULT_COGNITIVE_DTO } from "../../../../../types/domainDtos";
import { IconAlert } from "../../../../shared/common/Icons";
import {
    ARCHETYPES, DEFAULT_ARCHETYPE, ARCHETYPE_BY_INT, ARCHETYPE_TO_INT,
    COUNTER_ARCHETYPE_BY_TYPE, type ArchetypeId,
} from "./opinionData";
import { useOpinion } from "./useOpinion";
import { createBoardStyles } from "./opinionStyles";
import { OpsTab } from "../tabs";

interface HeroPickerProps {
    actions: ReturnType<typeof useCognitiveActions>;
}

export const HeroPicker = memo(({ actions }: HeroPickerProps) => {
    const theme = useTheme();
    const accents = useAccents();
    const l = useLocale();
    const b = createBoardStyles(theme, accents);
    const control = accents.crisis.accent;

    const cw = bindingDataOrDefault(useCognitive(), DEFAULT_COGNITIVE_DTO);
    const active: ArchetypeId = ARCHETYPE_BY_INT[cw.HeroArchetype] ?? "voice";
    const deployed = cw.HeroStatus !== HeroStatus.Inactive;
    // While a hero is deployed, switching is friction-gated (cost + cooldown); reflect it.
    const switchLocked = deployed && !cw.HeroArchetypeSwitchReady;

    // Optimistic highlight, reconciled against the backend: the reject of a
    // switch (budget / pending-budget / gametime) ships in the same DTO snapshot
    // as HeroArchetype, so the highlight rolls back to the real archetype on the
    // next publish (~500ms), with the reason surfaced in the global RejectToast.
    const choice = useOptimisticChoice<ArchetypeId>(
        active,
        (id) => actions.setHeroArchetype(ARCHETYPE_TO_INT[id]),
        cw.HeroActionRequest
    );
    const shown = choice.shown;
    const arch = ARCHETYPES.find((a) => a.id === shown) ?? DEFAULT_ARCHETYPE;

    // Live response (Phase 09): the best answer to the dominant incoming telegraph, once its
    // carrier is read (fog ≥ Detected). One click re-postures the speaker to hard-counter it —
    // pause-safe (the switch commits synchronously). Offered only when it differs from the active
    // archetype and the switch is not on cooldown.
    const { raids } = useOpinion();
    const bestResponse = useMemo<ArchetypeId | null>(() => {
        const dominant = raids.find((r) => r.isDominant && r.known && r.phase === 0);
        return dominant ? COUNTER_ARCHETYPE_BY_TYPE[dominant.type] : null;
    }, [raids]);
    const respondArch = bestResponse !== null ? ARCHETYPES.find((a) => a.id === bestResponse) : undefined;

    const onPick = (id: ArchetypeId): void => {
        if (switchLocked) return;
        choice.pick(id);
    };

    const debtPct = Math.round(Math.max(0, Math.min(1, cw.ArestovychDebtRatio)) * 100);

    return (
        <Column gap={theme.spacing.xs} style={{ width: "100%", minHeight: "120rem" }}>
            {/* No "HERO" label — the sub-view header already names it. Only the
                switch-cooldown chip surfaces here when relevant. */}
            {switchLocked && (
                <Row justify="flex-end">
                    <span style={b.chip(accents.crisis.accent)}>{l.t("UI_OB_SWITCH_COOLDOWN")}</span>
                </Row>
            )}

            {respondArch !== undefined && respondArch.id !== active && (
                <Row align="center" justify="space-between" style={{
                    padding: "3rem 6rem",
                    backgroundColor: hexToRgba(accents.resilience.accent, 0.1),
                    border: `1rem solid ${hexToRgba(accents.resilience.accent, 0.4)}`,
                    borderRadius: theme.layout.borderRadius,
                }}>
                    <span style={{ fontSize: theme.typography.sizeXS, color: accents.resilience.accent, fontFamily: theme.typography.fontFamilyMono }}>
                        {`${l.t("UI_OB_BEST_RESPONSE")}: ${l.t(respondArch.nameKey)}`}
                    </span>
                    <button
                        onClick={() => onPick(respondArch.id)}
                        disabled={switchLocked}
                        style={{
                            padding: "3rem 10rem", fontSize: theme.typography.sizeXS, fontWeight: 700,
                            fontFamily: theme.typography.fontFamilyMono,
                            color: switchLocked ? theme.colors.textMuted : accents.resilience.accent,
                            backgroundColor: "transparent",
                            border: `2rem solid ${switchLocked ? theme.colors.border : accents.resilience.accent}`,
                            borderRadius: theme.layout.borderRadius,
                            textTransform: "uppercase", letterSpacing: "0.3rem",
                            cursor: switchLocked ? "not-allowed" : "pointer",
                            opacity: switchLocked ? 0.5 : 1,
                        }}
                    >
                        {l.t("UI_OB_RESPOND")}
                    </button>
                </Row>
            )}

            <Row gap={theme.spacing.xs}>
                {ARCHETYPES.map((a) => {
                    const isActive = a.id === shown;
                    const disabled = switchLocked && a.id !== active;
                    return (
                        <button
                            key={a.id}
                            onClick={() => onPick(a.id)}
                            disabled={disabled}
                            style={{
                                flex: 1,
                                padding: "10rem 6rem",
                                fontSize: theme.typography.sizeSM,
                                fontWeight: 700,
                                fontFamily: theme.typography.fontFamilyMono,
                                color: isActive ? control : theme.colors.textMuted,
                                backgroundColor: isActive ? hexToRgba(control, 0.12) : "transparent",
                                border: isActive ? `3rem solid ${control}` : `2rem solid ${theme.colors.border}`,
                                borderRadius: theme.layout.borderRadius,
                                textTransform: "uppercase",
                                letterSpacing: "0.3rem",
                                cursor: disabled ? "not-allowed" : "pointer",
                                opacity: disabled ? 0.5 : 1,
                            }}
                        >
                            {l.t(a.nameKey)}
                        </button>
                    );
                })}
            </Row>

            <div style={{ ...b.card, padding: theme.spacing.xs }}>
                {/* Colored dot marks upside/downside (green = good, red = bad) — a "+/−"
                    bullet clashed with the "+"/"−" signs inside the effect text itself. */}
                <Row align="center" style={{ fontSize: theme.typography.sizeSM, marginBottom: "4rem" }}>
                    <span style={{ color: theme.colors.success, marginRight: "6rem" }}>•</span>
                    <span style={{ color: theme.colors.textSecondary }}>{l.t(arch.buffKey)}</span>
                </Row>
                <Row align="center" style={{ fontSize: theme.typography.sizeSM, marginBottom: "4rem" }}>
                    <span style={{ color: theme.colors.error, marginRight: "6rem" }}>•</span>
                    <span style={{ color: theme.colors.textSecondary }}>{l.t(arch.debuffKey)}</span>
                </Row>
                <Row align="center" style={{ fontSize: theme.typography.sizeSM }}>
                    <span style={{
                        color: theme.colors.textMuted, fontFamily: theme.typography.fontFamilyMono,
                        textTransform: "uppercase", letterSpacing: "0.3rem", marginRight: "6rem",
                    }}>{l.t("UI_OB_ARCH_BESTVS")}</span>
                    {/* Same mono font as the label so both sit on the same line — a body
                        font here dropped the value below the pixel-font label. */}
                    <span style={{ color: accents.resilience.accent, fontWeight: 700, fontFamily: theme.typography.fontFamilyMono }}>{l.t(arch.bestVsKey)}</span>
                </Row>
                {arch.trustDebt && (
                    <Row align="center" style={{
                        marginTop: "4rem", padding: "3rem 6rem",
                        fontSize: theme.typography.sizeXS, color: accents.crisis.accent,
                        backgroundColor: hexToRgba(accents.crisis.accent, 0.1),
                        borderRadius: theme.layout.borderRadius,
                    }}>
                        <span style={{ marginRight: "4rem", display: "flex" }}><IconAlert /></span>
                        {shown === active && deployed
                            ? (cw.ArestovychDebtCollapsing
                                ? l.t("UI_OB_TRUSTDEBT_COLLAPSING")
                                : l.t("UI_OB_TRUSTDEBT_NOW", debtPct))
                            : l.t("UI_OB_TRUSTDEBT_WARN")}
                    </Row>
                )}
            </div>

            <OpsTab actions={actions} />
        </Column>
    );
});

HeroPicker.displayName = "HeroPicker";
