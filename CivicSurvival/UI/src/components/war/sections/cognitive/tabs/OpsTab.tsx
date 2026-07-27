/**
 * OpsTab - Hero Operations (Gerda)
 * Zone 3, Tab 3 of Cognitive Warfare Sandwich
 */

import React, { memo, useCallback, useMemo, useRef } from "react";
import { Column, Row } from "../../../../coherent";
import { hexToRgba, useAccents, useTheme } from "../../../../../themes";
import { type Accents, type Theme } from "../../../../../themes/types";
import { bindingDataOrDefault, useCognitive, HeroStatus, type CognitiveDto } from "@hooks/domain";
import { DEFAULT_COGNITIVE_DTO } from "../../../../../types/domainDtos";
import { IconBrain } from "../../../../shared/common/Icons";
import { useLocale, type TranslationKey } from "../../../../../locales";
import { useRequestAction } from "@hooks/actions";
import { type useCognitiveActions } from "@hooks/actions";

const formatCost = (cost: number) => {
    if (cost >= 1000) return `$${(cost / 1000).toFixed(1)}k`; // eslint-disable-line civic/format-money-consistency
    return `$${cost}`;
};

/**
 * The speaker's face by archetype (C# HeroArchetype int: 0 Voice / 1 Arestovych / 2 Patriot —
 * the same mapping as ARCHETYPE_BY_INT on the opinion board). Keyed by the DTO's int so the
 * card always names whoever is actually selected.
 */
interface HeroFace {
    nameKey: TranslationKey;
    subKey: TranslationKey;
    /** Mode names. The two MODES are the same for everyone — counter blunts the enemy's push
     *  (-infection rate), recover heals the already-swayed (+recovery rate) — but what the act
     *  is CALLED belongs to the speaker: a frontline veteran does not lecture at a university. */
    counterKey: TranslationKey;
    recoverKey: TranslationKey;
}

const HERO_FACE_VOICE: HeroFace = {
    nameKey: "UI_OPS_HERO_NAME_VOICE",
    subKey: "UI_OPS_HERO_SUB_VOICE",
    counterKey: "UI_OPS_MODE_COUNTER_VOICE",
    recoverKey: "UI_OPS_MODE_RECOVER_VOICE",
};

const HERO_FACE: Record<number, HeroFace> = {
    0: HERO_FACE_VOICE,
    1: {
        nameKey: "UI_OPS_HERO_NAME_ARESTOVYCH",
        subKey: "UI_OPS_HERO_SUB_ARESTOVYCH",
        counterKey: "UI_OPS_MODE_COUNTER_ARESTOVYCH",
        recoverKey: "UI_OPS_MODE_RECOVER_ARESTOVYCH",
    },
    2: {
        nameKey: "UI_OPS_HERO_NAME_PATRIOT",
        subKey: "UI_OPS_HERO_SUB_PATRIOT",
        counterKey: "UI_OPS_MODE_COUNTER_PATRIOT",
        recoverKey: "UI_OPS_MODE_RECOVER_PATRIOT",
    },
};

const heroFaceOf = (archetype: number): HeroFace => HERO_FACE[archetype] ?? HERO_FACE_VOICE;

const createOpsStyles = (theme: Theme, accents: Accents) => ({
    container: {
        padding: theme.spacing.sm,
        height: "100%",
    } as React.CSSProperties,

    heroHeader: {
        marginBottom: theme.spacing.sm,
    } as React.CSSProperties,

    heroIcon: {
        fontSize: "24rem",
        color: accents.schemes.accent,
        marginRight: theme.spacing.sm,
    } as React.CSSProperties,

    heroTitle: {
        fontSize: "16rem",
        fontWeight: 700,
        color: theme.colors.textPrimary,
    } as React.CSSProperties,

    heroSubtitle: {
        fontSize: "12rem",
        color: theme.colors.textMuted,
    } as React.CSSProperties,

    statusBadge: (isActive: boolean, color: string) => ({
        padding: "4rem 9rem",
        fontSize: "12rem",
        fontWeight: 700,
        borderRadius: "4rem",
        backgroundColor: isActive ? hexToRgba(color, 0.12) : hexToRgba(theme.colors.textMuted, 0.12),
        color: isActive ? color : theme.colors.textMuted,
        textTransform: "uppercase" as const,
    }) as React.CSSProperties,

    actionButton: (color: string, disabled = false) => ({
        width: "100%",
        padding: "11rem 10rem",
        marginBottom: theme.spacing.xs,
        fontSize: "13rem",
        fontWeight: 700,
        textTransform: "uppercase" as const,
        border: `2rem solid ${disabled ? theme.colors.border : color}`,
        borderRadius: "4rem",
        backgroundColor: disabled ? "transparent" : hexToRgba(color, 0.06),
        color: disabled ? theme.colors.textMuted : color,
        cursor: disabled ? "not-allowed" : "pointer",
        opacity: disabled ? 0.5 : 1,
        display: "flex",
        justifyContent: "space-between",
        alignItems: "center",
    }) as React.CSSProperties,

    buttonCost: {
        fontSize: "12rem",
        opacity: 0.8,
    } as React.CSSProperties,

    effectsBox: {
        marginTop: theme.spacing.sm,
        padding: theme.spacing.xs,
        backgroundColor: theme.colors.surface,
        borderRadius: "4rem",
    } as React.CSSProperties,

    effectsTitle: {
        fontSize: "12rem",
        fontWeight: 700,
        color: theme.colors.textMuted,
        textTransform: "uppercase" as const,
        marginBottom: "6rem",
    } as React.CSSProperties,

    // Bullet + effect text top-aligned: cohtml does not support
    // align-items:baseline (it silently falls back to flex-start), so name the
    // valid value explicitly — same avoidance ReliefTab makes for its reserve row.
    effectRow: {
        display: "flex",
        alignItems: "flex-start",
        fontSize: "13rem",
        color: theme.colors.textSecondary,
        marginBottom: "6rem",
    } as React.CSSProperties,

    effectBullet: (color: string) => ({
        color,
        marginRight: "6rem",
    }) as React.CSSProperties,
});

type LocaleApi = ReturnType<typeof useLocale>;
type OpsStyles = ReturnType<typeof createOpsStyles>;

const getHeroStatusLabel = (cw: CognitiveDto, l: LocaleApi) => {
    const face = heroFaceOf(cw.HeroArchetype);
    if (cw.HeroStatus === HeroStatus.Inactive) return l.t("UI_OPS_STATUS_INACTIVE");
    if (cw.HeroStatus === HeroStatus.Deployed) return l.t(face.counterKey);
    if (cw.HeroStatus === HeroStatus.Lecturing) return l.t(face.recoverKey);
    return l.t("UI_OPS_STATUS_ACTIVE");
};

const getSwitchText = (cw: CognitiveDto, l: LocaleApi, pending: boolean, isCounterMode: boolean) => {
    if (pending) return l.t("UI_PROCESSING");
    const face = heroFaceOf(cw.HeroArchetype);
    if (isCounterMode) {
        return cw.CanSetHeroLecturing
            ? l.t("UI_OPS_SWITCH_MODE", l.t(face.recoverKey))
            : l.tDynamic(cw.SetHeroLecturingLockedReasonId);
    }
    return cw.CanSetHeroCounter
        ? l.t("UI_OPS_SWITCH_MODE", l.t(face.counterKey))
        : l.tDynamic(cw.SetHeroCounterLockedReasonId);
};

interface HeroActionControlsProps {
    cw: CognitiveDto;
    l: LocaleApi;
    theme: Theme;
    accents: Accents;
    styles: OpsStyles;
    pending: boolean;
    deployLockedText: string;
    onDeployCounter: () => void;
    onDeployLecturing: () => void;
    onSwitchToCounter: () => void;
    onSwitchToLecturing: () => void;
    onRecall: () => void;
}

const HeroActionControls: React.FC<HeroActionControlsProps> = ({
    cw,
    l,
    theme,
    accents,
    styles: s,
    pending,
    deployLockedText,
    onDeployCounter,
    onDeployLecturing,
    onSwitchToCounter,
    onSwitchToLecturing,
    onRecall,
}) => {
    const face = heroFaceOf(cw.HeroArchetype);
    const isDeployed = cw.HeroStatus !== HeroStatus.Inactive;
    if (!isDeployed) {
        const deployDisabled = !cw.CanDeployHero || pending;
        const deployCostText = pending ? l.t("UI_PROCESSING") : cw.CanDeployHero ? formatCost(cw.HeroDeployCost) : deployLockedText;
        return (
            <>
                <button
                    style={s.actionButton(accents.schemes.accent, deployDisabled)}
                    onClick={!deployDisabled ? onDeployCounter : undefined}
                    disabled={deployDisabled}
                >
                    <span>{l.t("UI_OPS_DEPLOY_MODE", l.t(face.counterKey))}</span>
                    <span style={s.buttonCost}>{deployCostText}</span>
                </button>
                <button
                    style={s.actionButton(theme.colors.success, deployDisabled)}
                    onClick={!deployDisabled ? onDeployLecturing : undefined}
                    disabled={deployDisabled}
                >
                    <span>{l.t("UI_OPS_DEPLOY_MODE", l.t(face.recoverKey))}</span>
                    <span style={s.buttonCost}>{deployCostText}</span>
                </button>
            </>
        );
    }

    const isCounterMode = cw.HeroStatus === HeroStatus.Deployed;
    const switchDisabled = pending || (isCounterMode ? !cw.CanSetHeroLecturing : !cw.CanSetHeroCounter);
    const recallDisabled = !cw.CanRecallHero || pending;
    const switchColor = isCounterMode ? theme.colors.success : accents.schemes.accent;
    const switchHandler = isCounterMode ? onSwitchToLecturing : onSwitchToCounter;

    return (
        <>
            <button
                style={s.actionButton(switchColor, switchDisabled)}
                onClick={!switchDisabled ? switchHandler : undefined}
                disabled={switchDisabled}
            >
                {getSwitchText(cw, l, pending, isCounterMode)}
            </button>
            <button
                style={s.actionButton(theme.colors.textMuted, recallDisabled)}
                onClick={!recallDisabled ? onRecall : undefined}
                disabled={recallDisabled}
            >
                {pending ? l.t("UI_PROCESSING") : cw.CanRecallHero ? l.t("UI_OPS_RECALL_HERO") : l.tDynamic(cw.RecallHeroLockedReasonId)}
            </button>
        </>
    );
};

interface OpsTabProps {
    actions: ReturnType<typeof useCognitiveActions>;
}

export const OpsTab = memo(({ actions }: OpsTabProps) => {
    const theme = useTheme();
    const accents = useAccents();
    const l = useLocale();
    const cw = bindingDataOrDefault(useCognitive(), DEFAULT_COGNITIVE_DTO);
    const heroActionRef = useRef<() => boolean>(() => false);
    const heroAction = useRequestAction(() => heroActionRef.current(), cw?.HeroActionRequest);
    const heroActionPending = heroAction.isPending;
    const heroErrorKey = heroAction.failureReasonId;
    // Shown only when deploy is locked: name the reason if the backend gave one,
    // otherwise a neutral "unavailable" — never the price, which would read as an
    // affordable action. The affordable price comes from formatCost directly.
    const deployLockedText = cw?.DeployHeroLockedReasonId ? l.tDynamic(cw.DeployHeroLockedReasonId) : l.t("UI_OPS_DEPLOY_UNAVAILABLE");

    const heroReductionPct = Math.round((cw?.HeroInfectionReduction ?? 0) * 100);
    const heroBonusPct = Math.round((cw?.HeroRecoveryBonus ?? 0) * 100);

    const runHeroAction = useCallback((action: () => void) => {
        if (heroActionPending) return;
        heroActionRef.current = () => {
            action();
            return true;
        };
        heroAction.execute();
    }, [heroAction, heroActionPending]);

    const handleDeployCounterPropaganda = useCallback(() => {
        runHeroAction(() => actions.deployHero(HeroStatus.Deployed));
    }, [actions, runHeroAction]);

    const handleDeployLecturing = useCallback(() => {
        runHeroAction(() => actions.deployHero(HeroStatus.Lecturing));
    }, [actions, runHeroAction]);

    const handleRecall = useCallback(() => {
        runHeroAction(() => actions.recallHero());
    }, [actions, runHeroAction]);

    const handleSwitchToDeployed = useCallback(() => {
        runHeroAction(() => actions.setHeroMode(HeroStatus.Deployed));
    }, [actions, runHeroAction]);

    const handleSwitchToLecturing = useCallback(() => {
        runHeroAction(() => actions.setHeroMode(HeroStatus.Lecturing));
    }, [actions, runHeroAction]);

    const s = useMemo(() => createOpsStyles(theme, accents), [theme, accents]);
    const heroIsDeployed = cw != null && cw.HeroStatus !== HeroStatus.Inactive;
    const statusColor = heroIsDeployed ? accents.schemes.accent : theme.colors.textMuted;
    // The card names the SELECTED speaker. It used to hard-code Gerda (the single hero of the
    // pre-archetype design), so picking Patriot or Arestovych still showed "GERDA - The Voice"
    // — the switch worked, the card lied about it.
    const heroFace = heroFaceOf(cw.HeroArchetype);

    return (
        <Column style={s.container}>
            <Row align="center" style={s.heroHeader}>
                <span style={s.heroIcon}><IconBrain /></span>
                <Column gap="2rem" style={{ flex: 1 }}>
                    <span style={s.heroTitle}>{l.t(heroFace.nameKey)}</span>
                    <span style={s.heroSubtitle}>{l.t(heroFace.subKey)}</span>
                </Column>
                <span style={s.statusBadge(heroIsDeployed, statusColor)}>
                    {getHeroStatusLabel(cw, l)}
                </span>
            </Row>

            <HeroActionControls
                cw={cw}
                l={l}
                theme={theme}
                accents={accents}
                styles={s}
                pending={heroActionPending}
                deployLockedText={deployLockedText}
                onDeployCounter={handleDeployCounterPropaganda}
                onDeployLecturing={handleDeployLecturing}
                onSwitchToCounter={handleSwitchToDeployed}
                onSwitchToLecturing={handleSwitchToLecturing}
                onRecall={handleRecall}
            />

            <div style={s.effectsBox}>
                <div style={s.effectsTitle}>{l.t("UI_OPS_EFFECTS_TITLE")}</div>
                <div style={s.effectRow}>
                    <span style={s.effectBullet(accents.schemes.accent)}>•</span>
                    {l.t("UI_OPS_EFFECT_COUNTER_MODE", l.t(heroFace.counterKey), heroReductionPct)}
                </div>
                <div style={s.effectRow}>
                    <span style={s.effectBullet(theme.colors.success)}>•</span>
                    {l.t("UI_OPS_EFFECT_RECOVER_MODE", l.t(heroFace.recoverKey), heroBonusPct)}
                </div>
                {heroErrorKey && (
                    <div style={s.effectRow}>
                        <span style={s.effectBullet(theme.colors.error)}>•</span>
                        {l.tDynamic(heroErrorKey)}
                    </div>
                )}
            </div>
        </Column>
    );
});

OpsTab.displayName = "OpsTab";
