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
import { useLocale } from "../../../../../locales";
import { useRequestAction } from "@hooks/actions";
import { type useCognitiveActions } from "@hooks/actions";

const formatCost = (cost: number) => {
    if (cost >= 1000) return `$${(cost / 1000).toFixed(1)}k`; // eslint-disable-line civic/format-money-consistency
    return `$${cost}`;
};

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
        fontSize: "13rem",
        fontWeight: 700,
        color: theme.colors.textPrimary,
    } as React.CSSProperties,

    heroSubtitle: {
        fontSize: "10rem",
        color: theme.colors.textMuted,
    } as React.CSSProperties,

    statusBadge: (isActive: boolean, color: string) => ({
        padding: "3rem 8rem",
        fontSize: "10rem",
        fontWeight: 700,
        borderRadius: "4rem",
        backgroundColor: isActive ? hexToRgba(color, 0.12) : hexToRgba(theme.colors.textMuted, 0.12),
        color: isActive ? color : theme.colors.textMuted,
        textTransform: "uppercase" as const,
    }) as React.CSSProperties,

    actionButton: (color: string, disabled = false) => ({
        width: "100%",
        padding: "8rem",
        marginBottom: theme.spacing.xs,
        fontSize: "11rem",
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
        fontSize: "10rem",
        opacity: 0.8,
    } as React.CSSProperties,

    effectsBox: {
        marginTop: theme.spacing.sm,
        padding: theme.spacing.xs,
        backgroundColor: theme.colors.surface,
        borderRadius: "4rem",
    } as React.CSSProperties,

    effectsTitle: {
        fontSize: "10rem",
        fontWeight: 700,
        color: theme.colors.textMuted,
        textTransform: "uppercase" as const,
        marginBottom: "4rem",
    } as React.CSSProperties,

    effectRow: {
        fontSize: "11rem",
        color: theme.colors.textSecondary,
        marginBottom: "2rem",
    } as React.CSSProperties,

    effectBullet: (color: string) => ({
        color,
        marginRight: "4rem",
    }) as React.CSSProperties,
});

type LocaleApi = ReturnType<typeof useLocale>;
type OpsStyles = ReturnType<typeof createOpsStyles>;

const getHeroStatusLabel = (cw: CognitiveDto, l: LocaleApi) => {
    if (cw.HeroStatus === HeroStatus.Inactive) return l.t("UI_OPS_STATUS_INACTIVE");
    if (cw.HeroStatus === HeroStatus.Deployed) return l.t("UI_OPS_STATUS_COUNTER");
    if (cw.HeroStatus === HeroStatus.Lecturing) return l.t("UI_OPS_STATUS_LECTURES");
    return l.t("UI_OPS_STATUS_ACTIVE");
};

const getSwitchText = (cw: CognitiveDto, l: LocaleApi, pending: boolean, isCounterMode: boolean) => {
    if (pending) return l.t("UI_PROCESSING");
    if (isCounterMode) {
        return cw.CanSetHeroLecturing ? l.t("UI_OPS_SWITCH_LECTURES") : l.tDynamic(cw.SetHeroLecturingLockedReasonId);
    }
    return cw.CanSetHeroCounter ? l.t("UI_OPS_SWITCH_COUNTER") : l.tDynamic(cw.SetHeroCounterLockedReasonId);
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
                    <span>{l.t("UI_OPS_DEPLOY_COUNTER")}</span>
                    <span style={s.buttonCost}>{deployCostText}</span>
                </button>
                <button
                    style={s.actionButton(theme.colors.success, deployDisabled)}
                    onClick={!deployDisabled ? onDeployLecturing : undefined}
                    disabled={deployDisabled}
                >
                    <span>{l.t("UI_OPS_DEPLOY_LECTURES")}</span>
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
    const heroActionPending = heroAction.isPending || cw?.HeroActionRequest.Status === "pending";
    const heroErrorKey = cw?.HeroActionRequest.Status === "failed" ? cw.HeroActionRequest.ReasonId : "";
    const deployLockedText = cw?.DeployHeroLockedReasonId ? l.tDynamic(cw.DeployHeroLockedReasonId) : formatCost(cw?.HeroDeployCost ?? 0);

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

    return (
        <Column style={s.container}>
            <Row align="center" style={s.heroHeader}>
                <span style={s.heroIcon}><IconBrain /></span>
                <Column gap="2rem" style={{ flex: 1 }}>
                    <span style={s.heroTitle}>{l.t("UI_OPS_HERO_NAME")}</span>
                    <span style={s.heroSubtitle}>{l.t("UI_OPS_HERO_SUBTITLE")}</span>
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
                    {l.t("UI_OPS_EFFECT_COUNTER", heroReductionPct)}
                </div>
                <div style={s.effectRow}>
                    <span style={s.effectBullet(theme.colors.success)}>•</span>
                    {l.t("UI_OPS_EFFECT_LECTURES", heroBonusPct)}
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
